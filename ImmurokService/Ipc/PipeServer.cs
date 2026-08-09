using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using ImmurokCommon.Protocol;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Ipc;

/// <summary>
/// Client ↔ Service 命名管道服务端（文本协议）。移植自 macOS <c>PAMSocketServer.swift</c>。
/// 帧格式见 <see cref="IpcFraming"/>。多实例并发，每连接一个处理循环。
/// </summary>
public sealed class PipeServer : IAsyncDisposable
{
    private readonly ILogger<PipeServer> _log;
    private readonly CommandHandlers _handlers;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public PipeServer(ILogger<PipeServer> log, CommandHandlers handlers)
    {
        _log = log;
        _handlers = handlers;
    }

    public void Start()
    {
        if (_acceptLoop is not null) return;
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _log.LogInformation("管道服务端已启动: {Pipe}", PipeNames.ClientServiceFull);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServerStream();
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _log.LogDebug("客户端已连接");
                // 每个连接独立处理，不阻塞下一个 accept。
                _ = HandleConnectionAsync(server, ct);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "accept 循环异常");
                server?.Dispose();
                await Task.Delay(1000, ct).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        try
        {
            while (server.IsConnected && !ct.IsCancellationRequested)
            {
                string? request = await IpcFraming.ReadFrameAsync(server, ct).ConfigureAwait(false);
                if (request is null) break; // 连接关闭

                // 录入是流式命令：一路推进度帧，最后一帧为终态。
                if (request.StartsWith("FP:ENROLL:", StringComparison.Ordinal))
                {
                    await _handlers.HandleEnrollStreamAsync(
                        request,
                        frame => IpcFraming.WriteFrameAsync(server, frame, ct),
                        ct).ConfigureAwait(false);
                    continue;
                }

                // 配对也是流式命令：设备的指纹门/按键阶段要实时推给界面。
                // CliServer 仍走 HandleAsync 里的非流式 PAIR:START（imk 不需要分步提示）。
                if (request == $"{IpcProtocol.Pair}:{IpcProtocol.PairStart}")
                {
                    await _handlers.HandlePairStartStreamAsync(
                        frame => IpcFraming.WriteFrameAsync(server, frame, ct),
                        ct).ConfigureAwait(false);
                    continue;
                }

                // OTA 推送也是流式命令。
                if (request.StartsWith("OTA:PUSH:", StringComparison.Ordinal))
                {
                    await _handlers.HandleOtaPushStreamAsync(
                        request,
                        frame => IpcFraming.WriteFrameAsync(server, frame, ct),
                        ct).ConfigureAwait(false);
                    continue;
                }

                string response = await _handlers.HandleAsync(request, ct).ConfigureAwait(false);
                await IpcFraming.WriteFrameAsync(server, response, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "连接处理结束");
        }
        finally
        {
            try { if (server.IsConnected) server.Disconnect(); } catch { /* ignore */ }
            server.Dispose();
        }
    }

    /// <summary>创建管道实例，ACL 限当前交互用户 + SYSTEM（收紧本地攻击面）。</summary>
    private NamedPipeServerStream CreateServerStream()
    {
        var security = new PipeSecurity();
        // 允许本机已认证用户读写（Client 以登录用户运行）。
        // 必须含 CreateNewInstance：否则当已有实例开着、再并发创建下一个实例时，
        // DACL 校验会因缺该权限而抛 UnauthorizedAccessException（长命令如 PAIR:START
        // 会占着实例，恰好触发此路径）。
        var authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        security.AddAccessRule(new PipeAccessRule(authUsers,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        // SYSTEM 完全控制。
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(system,
            PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeNames.ClientService,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,       // 用字节流 + 4 字节长度前缀分帧
            PipeOptions.Asynchronous,
            0, 0, security);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { /* ignore */ }
        }
        _cts?.Dispose();
    }
}
