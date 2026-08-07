using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using ImmurokService.Ble;
using ImmurokService.Platform;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Ipc;

/// <summary>
/// imk 命令行工具的服务端：命名管道 <c>\\.\pipe\immurok-cli</c>，行式文本协议（对齐 macOS CLISocketServer）。
/// 每个连接一问一答后关闭：
///   LIST:&lt;ssh|otp|api&gt;          -&gt; OK\n&lt;name&gt;\n&lt;name&gt;...      （无条目则仅 OK）
///   GET:&lt;cat&gt;:&lt;name&gt;           -&gt; OK:&lt;value&gt; | ERROR:&lt;reason&gt;
///     · otp -&gt; 当前 6 位验证码（指纹门）
///     · api -&gt; 明文密钥值
///     · ssh -&gt; OpenSSH 公钥行（私钥永不导出）
///   APPROVE:&lt;command&gt;          -&gt; OK | DENY   （imk agent 运行前的裸指纹认证）
///   CANCEL                     -&gt; OK          （取消进行中的指纹门；imk Ctrl+C 时用）
/// imk 读到 EOF（服务端写完即关闭）为止。
/// </summary>
public sealed class CliServer : IAsyncDisposable
{
    private const string PipeName = "immurok-cli";

    private readonly ILogger<CliServer> _log;
    private readonly BleManager _ble;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public bool IsRunning => _loop is not null;

    public CliServer(ILogger<CliServer> log, BleManager ble, AppSettings settings)
    {
        _log = log;
        _ble = ble;
        _settings = settings;
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _log.LogInformation("imk CLI 服务已启动: \\\\.\\pipe\\{Pipe}", PipeName);
    }

    public async Task StopAsync()
    {
        if (_loop is null) return;
        _cts?.Cancel();
        try { await _loop; } catch { /* ignore */ }
        _loop = null;
        _cts?.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _ = HandleAsync(pipe, ct);
            }
            catch (OperationCanceledException) { pipe?.Dispose(); break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "imk CLI accept 异常");
                pipe?.Dispose();
                await Task.Delay(1000, ct).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        var authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        security.AddAccessRule(new PipeAccessRule(authUsers,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            var enc = new UTF8Encoding(false);
            string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            string response = await DispatchAsync(line?.Trim() ?? "").ConfigureAwait(false);
            byte[] bytes = enc.GetBytes(response);
            await pipe.WriteAsync(bytes, ct).ConfigureAwait(false);
            await pipe.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "imk CLI 连接处理结束");
        }
        finally { pipe.Dispose(); }
    }

    private async Task<string> DispatchAsync(string cmd)
    {
        // CANCEL 无需连接检查：取消进行中的指纹门（imk Ctrl+C 时用）。
        if (cmd == "CANCEL")
        {
            await _ble.CancelGateAsync().ConfigureAwait(false);
            return "OK";
        }

        if (!_ble.IsConnected) return "ERROR:NOT_CONNECTED";

        if (cmd.StartsWith("APPROVE:", StringComparison.Ordinal))
        {
            // imk agent 运行前授权：命令串仅供日志（Windows 无浮层，展示在 imk 终端）。
            string commandStr = cmd[8..];
            if (!_settings.ImkAgentEnabled)
            {
                _log.LogInformation("imk agent 授权请求被拒：功能未开启");
                return "DENY";
            }
            _log.LogInformation("imk agent 授权请求：{Cmd}", commandStr);
            bool ok = await _ble.AuthenticateAsync().ConfigureAwait(false);
            return ok ? "OK" : "DENY";
        }

        if (cmd.StartsWith("LIST:", StringComparison.Ordinal))
        {
            int cat = CatOf(cmd[5..]);
            if (cat < 0) return "ERROR:BAD_CATEGORY";
            return await HandleListAsync((byte)cat).ConfigureAwait(false);
        }
        if (cmd.StartsWith("GET:", StringComparison.Ordinal))
        {
            // GET:<cat>:<name>（name 可含冒号，故只切两段）
            string rest = cmd[4..];
            int sep = rest.IndexOf(':');
            if (sep <= 0) return "ERROR:BAD_REQUEST";
            int cat = CatOf(rest[..sep]);
            string name = rest[(sep + 1)..];
            if (cat < 0) return "ERROR:BAD_CATEGORY";
            if (name.Length == 0) return "ERROR:BAD_REQUEST";
            return await HandleGetAsync((byte)cat, name).ConfigureAwait(false);
        }
        return "ERROR:UNKNOWN_COMMAND";
    }

    private static int CatOf(string s) => s.Trim() switch
    {
        "ssh" => BleManager.CatSsh,
        "otp" => BleManager.CatOtp,
        "api" => BleManager.CatApi,
        _ => -1,
    };

    private async Task<string> HandleListAsync(byte cat)
    {
        int count = await _ble.GetKeyCountAsync(cat).ConfigureAwait(false);
        var names = new List<string>();
        for (byte i = 0; i < count; i++)
        {
            string? n = await NameAtAsync(cat, i).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(n)) names.Add(n);
        }
        return names.Count == 0 ? "OK" : "OK\n" + string.Join("\n", names);
    }

    private async Task<string> HandleGetAsync(byte cat, string name)
    {
        int idx = await FindIndexAsync(cat, name).ConfigureAwait(false);
        if (idx < 0) return "ERROR:NOT_FOUND";

        if (cat == BleManager.CatOtp)
        {
            if (!_settings.OtpEnabled) return "ERROR:DISABLED"; // 功能开关：OTP 已关闭
            string? code = await _ble.GetOtpCodeAsync((byte)idx).ConfigureAwait(false);
            return code is null ? "ERROR:DENY" : "OK:" + code;
        }
        if (cat == BleManager.CatApi)
        {
            byte[]? entry = await _ble.ReadKeyEntryAsync(BleManager.CatApi, (byte)idx).ConfigureAwait(false);
            if (entry is null || entry.Length <= 32) return "ERROR:READ_FAILED";
            // api_entry: name[32] + key[128]。取 key 区，截到首个 NUL。
            int end = 32;
            while (end < entry.Length && entry[end] != 0) end++;
            string value = Encoding.UTF8.GetString(entry, 32, end - 32);
            return value.Length == 0 ? "ERROR:EMPTY" : "OK:" + value;
        }
        // ssh：返回 OpenSSH 公钥行（不导出私钥）
        byte[]? pub = await _ble.SshGetPubAsync((byte)idx).ConfigureAwait(false);
        if (pub is null) return "ERROR:READ_FAILED";
        return "OK:" + BleManager.SshAuthorizedKey(pub, name);
    }

    private async Task<int> FindIndexAsync(byte cat, string name)
    {
        int count = await _ble.GetKeyCountAsync(cat).ConfigureAwait(false);
        for (byte i = 0; i < count; i++)
        {
            string? n = await NameAtAsync(cat, i).ConfigureAwait(false);
            if (n == name) return i;
        }
        return -1;
    }

    private async Task<string?> NameAtAsync(byte cat, byte idx)
    {
        if (cat == BleManager.CatOtp)
            return (await _ble.ReadOtpNameServiceAsync(idx).ConfigureAwait(false))?.Name;
        return await _ble.ReadKeyNameAsync(cat, idx).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
