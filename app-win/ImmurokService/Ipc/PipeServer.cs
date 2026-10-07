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

    /// <summary>第一个实例创建失败（管道名被其他进程占用）时为 true，供 SECURITY:STATUS 报 contended。</summary>
    public bool Contended { get; private set; }

    public PipeServer(ILogger<PipeServer> log, CommandHandlers handlers, SecurityStatus status)
    {
        _log = log;
        _handlers = handlers;
        status.RegisterPipe(PipeNames.ClientService, () => Contended);
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
        bool first = true;
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                try
                {
                    server = CreateServerStream(first);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    // 名字被占（FirstPipeInstance 创建失败）或实例创建被拒。不静默：每次都记 error、
                    // 5s 重试，Contended 供状态页。进程不退出——BLE 与锁屏解锁不依赖这条管道，
                    // 退出等于把指纹解锁一起交给抢占者（设计稿 §3.2 / §9.3）。
                    failures++;
                    Contended = true;
                    _log.LogError(ex, "管道创建失败（第 {N} 次，名字可能被其他进程占用）: {Pipe}",
                        failures, PipeNames.ClientServiceFull);
                    await Task.Delay(5000, ct).ContinueWith(_ => { }, CancellationToken.None);
                    continue;
                }
                first = false;
                if (failures > 0)
                {
                    _log.LogInformation("管道名已恢复可用（此前失败 {N} 次）", failures);
                    failures = 0;
                }
                Contended = false;
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
            // 对端身份每连接取一次。只用于拒绝（owner 校验等），不用于放行。
            CallerIdentity? caller = PipeCaller.Identify(server);
            _log.LogDebug("客户端身份: {Caller}", caller?.ToString() ?? "unknown");

            while (server.IsConnected && !ct.IsCancellationRequested)
            {
                string? request = await IpcFraming.ReadFrameAsync(server, ct).ConfigureAwait(false);
                if (request is null) break; // 连接关闭

                // 分级授权（含流式命令）：不可信调用方 / 指纹门未过 → 明确错误码，连接保留。
                string? deny = await _handlers.AuthorizeAsync(request, caller, ct).ConfigureAwait(false);
                if (deny is not null)
                {
                    await IpcFraming.WriteFrameAsync(server, deny, ct).ConfigureAwait(false);
                    continue;
                }

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
                        caller,
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

                string response = await _handlers.HandleAsync(request, caller, ct).ConfigureAwait(false);
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

    /// <summary>
    /// 创建管道实例。ACL 见 <see cref="PipeAcl"/>：Authenticated Users 只读写（Client 以登录用户运行），
    /// 不再给 CreateNewInstance。原来的注释说「没有它并发创建下一个实例会抛 UnauthorizedAccessException」，
    /// 那是在控制台调试模式下以普通用户身份跑出来的现象——后续实例的访问检查对的是创建者的 token，
    /// 服务身份（LocalSystem）本来就有 FullControl；调试模式由 PipeAcl 给本进程用户 FullControl 兜住。
    /// 第一个实例带 FirstPipeInstance：名字已被别人占住时直接失败，而不是静默共存（设计稿 §3.2）。
    /// </summary>
    private static NamedPipeServerStream CreateServerStream(bool first)
        => NamedPipeServerStreamAcl.Create(
            PipeNames.ClientService,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,       // 用字节流 + 4 字节长度前缀分帧
            PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None),
            0, 0, PipeAcl.Build(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null)));

    /// <summary>
    /// 可重入：Worker.StopAsync 显式调一次，宿主释放单例时再调一次。原来第二次会对已 Dispose 的
    /// CancellationTokenSource 调 Cancel() 抛 ObjectDisposedException，每次正常停止都被记成
    /// 「ImmurokService 异常终止」、进程非零退出。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var cts = _cts;
        var loop = _acceptLoop;
        _cts = null;
        _acceptLoop = null;
        if (cts is null) return;
        cts.Cancel();
        if (loop is not null)
        {
            try { await loop; } catch { /* ignore */ }
        }
        cts.Dispose();
    }
}
