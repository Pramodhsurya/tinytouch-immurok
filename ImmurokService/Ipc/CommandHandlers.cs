using System.Threading.Channels;
using ImmurokCommon.Ble;
using ImmurokCommon.Models;
using ImmurokCommon.Protocol;
using ImmurokService.Ble;
using ImmurokService.Ota;
using ImmurokService.Platform;
using ImmurokService.Security;
using ImmurokService.Ssh;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Ipc;

/// <summary>
/// 处理 Client ↔ Service 文本协议请求（复用 macOS PAMSocketServer 协议）。
/// 每个请求返回一行响应字符串。
/// </summary>
public sealed class CommandHandlers
{
    private readonly ILogger<CommandHandlers> _log;
    private readonly BleManager _ble;
    private readonly ImmurokSecurity _security;
    private readonly CredentialStore _creds;
    private readonly SshAgentServer _sshAgent;
    private readonly AppSettings _settings;

    public CommandHandlers(ILogger<CommandHandlers> log, BleManager ble, ImmurokSecurity security,
        CredentialStore creds, SshAgentServer sshAgent, AppSettings settings)
    {
        _log = log;
        _ble = ble;
        _security = security;
        _creds = creds;
        _sshAgent = sshAgent;
        _settings = settings;
    }

    public async Task<string> HandleAsync(string request, CancellationToken ct)
    {
        _log.LogDebug("IPC 请求: {Request}", request.Split(':')[0]);
        string[] parts = request.Split(IpcProtocol.Sep);
        string verb = parts[0];

        try
        {
            return verb switch
            {
                IpcProtocol.Status => HandleStatus(),
                IpcProtocol.Info   => await HandleInfoAsync(),
                IpcProtocol.Fp     => await HandleFpAsync(parts, ct),
                IpcProtocol.Pair   => await HandlePairAsync(parts, ct),
                IpcProtocol.Slot   => await HandleSlotAsync(parts),
                IpcProtocol.Key    => await HandleKeyAsync(parts),
                IpcProtocol.SshAgent => await HandleSshAgentAsync(parts),
                IpcProtocol.Pass   => HandlePass(parts),
                IpcProtocol.Auth   => await HandleAuthAsync(parts, ct),
                // 取消进行中的指纹门：不做连接检查，尽力而为（客户端认证弹窗点取消时用）。
                IpcProtocol.CancelGate => await HandleCancelGateAsync(),
                // 功能开关：不要求设备在线（纯本地设置）。
                IpcProtocol.Feature => await HandleFeatureAsync(parts),
                IpcProtocol.Ota    => IpcProtocol.ErrOtaNotAvailable, // OTA 待阶段5实现
                _ => IpcProtocol.ErrUnknownCommand,
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "处理请求异常: {Verb}", verb);
            return IpcProtocol.Error;
        }
    }

    /// <summary>
    /// 功能开关读写。纯本地设置，不要求设备在线。
    /// ssh 需要连带启停 agent 进程，其余只落设置、由各功能入口自行判定。
    /// </summary>
    private async Task<string> HandleFeatureAsync(string[] parts)
    {
        if (parts.Length < 2) return IpcProtocol.ErrInvalidFormat;

        if (parts[1] == IpcProtocol.FeatureGet)
        {
            static string B(bool v) => v ? "1" : "0";
            return $"{IpcProtocol.Ok}:{B(_settings.UnlockEnabled)}:{B(_settings.LockEnabled)}:" +
                   $"{B(_settings.SshAgentEnabled)}:{B(_settings.ImkAgentEnabled)}:{B(_settings.OtpEnabled)}";
        }

        if (parts[1] == IpcProtocol.FeatureSet)
        {
            // FEATURE:SET:<name>:<0|1>
            if (parts.Length < 4) return IpcProtocol.ErrInvalidFormat;
            bool on = parts[3] == "1";
            switch (parts[2])
            {
                case IpcProtocol.FeatureUnlock: _settings.UnlockEnabled = on; break;
                case IpcProtocol.FeatureLock:   _settings.LockEnabled = on; break;
                case IpcProtocol.FeatureAgent:  _settings.ImkAgentEnabled = on; break;
                case IpcProtocol.FeatureOtp:    _settings.OtpEnabled = on; break;
                case IpcProtocol.FeatureSsh:
                    _settings.SshAgentEnabled = on;
                    if (on) { _sshAgent.Start(); await _sshAgent.RefreshIdentitiesAsync(); }
                    else await _sshAgent.StopAsync();
                    break;
                default: return IpcProtocol.ErrInvalidFormat;
            }
            _log.LogInformation("功能开关 {Name} -> {On}", parts[2], on ? "ON" : "OFF");
            return IpcProtocol.Ok;
        }

        return IpcProtocol.ErrUnknownCommand;
    }

    /// <summary>取消进行中的指纹门，让设备停止闪灯等待，等待中的操作立即以失败收尾。</summary>
    private async Task<string> HandleCancelGateAsync()
    {
        await _ble.CancelGateAsync();
        return IpcProtocol.Ok;
    }

    private string HandleStatus()
    {
        // STATUS → STATUS:1:deviceName | STATUS:0:
        return _ble.IsConnected
            ? $"{IpcProtocol.Status}:1:{_ble.DeviceName}"
            : $"{IpcProtocol.Status}:0:";
    }

    /// <summary>INFO → OK:&lt;paired 0/1&gt;:&lt;battery 或 -1&gt;:&lt;fw 或 -&gt;:&lt;fpcount&gt;</summary>
    private async Task<string> HandleInfoAsync()
    {
        if (!_ble.IsConnected) return IpcProtocol.ErrNotConnected;
        var info = await _ble.GetDeviceInfoAsync();
        if (info is null) return IpcProtocol.Error;
        var (paired, battery, fw, fpCount) = info.Value;
        return $"{IpcProtocol.Ok}:{(paired ? 1 : 0)}:{battery ?? -1}:{fw ?? "-"}:{fpCount}";
    }

    private async Task<string> HandleFpAsync(string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2) return IpcProtocol.ErrInvalidFormat;
        if (!_ble.IsConnected) return IpcProtocol.ErrNotConnected;

        switch (parts[1])
        {
            case IpcProtocol.FpList:
            {
                List<FingerprintSlot>? slots = await _ble.GetFingerprintListAsync();
                if (slots is null) return IpcProtocol.ErrNotConnected;
                int count = slots.Count(s => s.Enrolled);
                return $"{IpcProtocol.Ok}:{count}";
            }
            case IpcProtocol.FpSlots:
            {
                byte? bitmap = await _ble.GetFingerprintBitmapAsync();
                if (bitmap is null) return IpcProtocol.ErrNotConnected;
                return $"{IpcProtocol.Ok}:{bitmap.Value}";
            }
            case IpcProtocol.FpEnroll:
            {
                if (parts.Length < 3 || !byte.TryParse(parts[2], out byte slot))
                    return IpcProtocol.ErrInvalidSlot;
                bool ok = await _ble.StartEnrollmentAsync(slot);
                return ok ? $"{IpcProtocol.Ok}:ENROLL_STARTED" : IpcProtocol.ErrEnrollFailed;
            }
            case IpcProtocol.FpDelete:
            {
                if (parts.Length < 3 || !byte.TryParse(parts[2], out byte slot))
                    return IpcProtocol.ErrInvalidSlot;
                bool ok = await _ble.DeleteFingerprintAsync(slot);
                return ok ? $"{IpcProtocol.Ok}:DELETED" : IpcProtocol.ErrDeleteFailed;
            }
            case IpcProtocol.FpStatus:
                return $"{IpcProtocol.Ok}:IDLE";
            default:
                return IpcProtocol.ErrUnknownCommand;
        }
    }

    private async Task<string> HandlePairAsync(string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2) return IpcProtocol.ErrInvalidFormat;

        switch (parts[1])
        {
            case IpcProtocol.PairStatus:
                return _security.IsPaired ? $"{IpcProtocol.Ok}:PAIRED" : $"{IpcProtocol.Ok}:UNPAIRED";

            case IpcProtocol.PairStart:
            {
                if (!_ble.IsConnected) return IpcProtocol.ErrNotConnected;
                PairFailureReason reason = await _ble.StartPairingAsync(ct: ct);
                return reason == PairFailureReason.None
                    ? $"{IpcProtocol.Ok}:PAIRED"
                    : $"{IpcProtocol.Error}:{reason.ToString().ToUpperInvariant()}";
            }
            case IpcProtocol.PairReset:
                _ble.ResetPairing();
                return $"{IpcProtocol.Ok}:RESET";

            default:
                return IpcProtocol.ErrUnknownCommand;
        }
    }

    /// <summary>
    /// 流式指纹录入：一路把设备的 0x11 进度事件推给客户端，最后一帧为终态。
    /// 帧格式：<c>PROGRESS:&lt;Event&gt;:&lt;current&gt;:&lt;total&gt;</c>；终态 <c>OK:COMPLETE</c> / <c>ERROR:*</c>。
    /// </summary>
    public async Task HandleEnrollStreamAsync(string request, Func<string, Task> writeFrame, CancellationToken ct)
    {
        string[] parts = request.Split(IpcProtocol.Sep);
        if (parts.Length < 3 || !byte.TryParse(parts[2], out byte slot))
        {
            await writeFrame(IpcProtocol.ErrInvalidSlot);
            return;
        }
        if (!_ble.IsConnected)
        {
            await writeFrame(IpcProtocol.ErrNotConnected);
            return;
        }

        var channel = Channel.CreateUnbounded<string>();
        void OnProgress(FpEnrollEvent ev, int cur, int total)
        {
            channel.Writer.TryWrite($"PROGRESS:{ev}:{cur}:{total}");
            if (ev == FpEnrollEvent.Complete)
            {
                channel.Writer.TryWrite($"{IpcProtocol.Ok}:COMPLETE");
                channel.Writer.TryComplete();
            }
            else if (ev == FpEnrollEvent.Failed)
            {
                channel.Writer.TryWrite(IpcProtocol.ErrEnrollFailed);
                channel.Writer.TryComplete();
            }
        }

        _ble.EnrollProgress += OnProgress;
        try
        {
            bool started = await _ble.StartEnrollmentAsync(slot);
            if (!started)
            {
                await writeFrame(IpcProtocol.ErrEnrollFailed);
                return;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(90000);
            try
            {
                await foreach (string msg in channel.Reader.ReadAllAsync(timeoutCts.Token))
                    await writeFrame(msg);
            }
            catch (OperationCanceledException)
            {
                await _ble.CancelEnrollmentAsync();
                await writeFrame(IpcProtocol.Timeout);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "流式录入异常");
            await writeFrame(IpcProtocol.Error);
        }
        finally
        {
            _ble.EnrollProgress -= OnProgress;
        }
    }

    /// <summary>
    /// 流式 OTA 推送。请求 <c>OTA:PUSH:&lt;base64 整个 .imfw&gt;</c>；
    /// 推送中回 <c>PROGRESS:&lt;百分比整数&gt;</c>，终态 <c>OK:DONE</c> / <c>ERROR:&lt;原因&gt;</c>。
    /// </summary>
    public async Task HandleOtaPushStreamAsync(string request, Func<string, Task> writeFrame, CancellationToken ct)
    {
        // OTA:PUSH:<base64>
        int idx = request.IndexOf(":PUSH:", StringComparison.Ordinal);
        if (idx < 0) { await writeFrame(IpcProtocol.ErrInvalidFormat); return; }
        string b64 = request[(idx + 6)..];
        byte[] imfw;
        try { imfw = Convert.FromBase64String(b64); }
        catch { await writeFrame(IpcProtocol.ErrInvalidFormat); return; }

        if (!_ble.OtaAvailable) { await writeFrame(IpcProtocol.ErrOtaNotAvailable); return; }

        var engine = new OtaEngine(_ble, _log);
        int lastPct = -1;
        var progressQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void OnProgress(double f)
        {
            int pct = (int)(f * 100);
            if (pct != lastPct) { lastPct = pct; progressQueue.Enqueue($"PROGRESS:{pct}"); }
        }

        // 边推送边把进度刷给客户端。
        var pushTask = engine.PushAsync(imfw, OnProgress, ct);
        while (!pushTask.IsCompleted)
        {
            while (progressQueue.TryDequeue(out string? msg)) await writeFrame(msg);
            await Task.WhenAny(pushTask, Task.Delay(200, CancellationToken.None));
        }
        while (progressQueue.TryDequeue(out string? msg)) await writeFrame(msg);

        try
        {
            var result = await pushTask;
            await writeFrame(result.Ok ? $"{IpcProtocol.Ok}:DONE" : $"{IpcProtocol.Error}:{result.Message}");
        }
        catch (OperationCanceledException) { await writeFrame(IpcProtocol.Timeout); }
        catch (Exception ex)
        {
            _log.LogError(ex, "OTA 推送异常");
            await writeFrame($"{IpcProtocol.Error}:{ex.Message}");
        }
    }

    /// <summary>密钥库：SSH / OTP / API 列表、TOTP 取码、删除、SSH 公钥导出。</summary>
    private async Task<string> HandleKeyAsync(string[] parts)
    {
        if (parts.Length < 2) return IpcProtocol.ErrInvalidFormat;
        if (!_ble.IsConnected) return IpcProtocol.ErrNotConnected;

        switch (parts[1])
        {
            case IpcProtocol.KeyList:
            {
                if (parts.Length < 3 || !byte.TryParse(parts[2], out byte cat))
                    return IpcProtocol.ErrInvalidFormat;
                int count = await _ble.GetKeyCountAsync(cat);
                var entries = new List<string>();
                for (byte i = 0; i < count; i++)
                {
                    if (cat == BleManager.CatOtp)
                    {
                        var ns = await _ble.ReadOtpNameServiceAsync(i);
                        if (ns is null) continue;
                        entries.Add($"{i},{B64(ns.Value.Name)},{B64(ns.Value.Service)}");
                    }
                    else
                    {
                        string? name = await _ble.ReadKeyNameAsync(cat, i);
                        if (name is null) continue;
                        entries.Add($"{i},{B64(name)},");
                    }
                }
                return $"{IpcProtocol.Ok}:{string.Join(";", entries)}";
            }
            case IpcProtocol.KeyOtp:
            {
                if (!_settings.OtpEnabled) return IpcProtocol.Deny; // 功能开关：OTP 已关闭
                if (parts.Length < 3 || !byte.TryParse(parts[2], out byte idx))
                    return IpcProtocol.ErrInvalidFormat;
                string? code = await _ble.GetOtpCodeAsync(idx);
                return code is null ? IpcProtocol.Deny : $"{IpcProtocol.Ok}:{code}";
            }
            case IpcProtocol.KeyDelete:
            {
                if (parts.Length < 4 || !byte.TryParse(parts[2], out byte cat) || !byte.TryParse(parts[3], out byte idx))
                    return IpcProtocol.ErrInvalidFormat;
                bool ok = await _ble.DeleteKeyAsync(cat, idx);
                return ok ? IpcProtocol.Ok : IpcProtocol.Deny;
            }
            case IpcProtocol.KeySshPub:
            {
                if (parts.Length < 3 || !byte.TryParse(parts[2], out byte idx))
                    return IpcProtocol.ErrInvalidFormat;
                byte[]? pub = await _ble.SshGetPubAsync(idx);
                if (pub is null) return IpcProtocol.Error;
                string name = await _ble.ReadKeyNameAsync(BleManager.CatSsh, idx) ?? "immurok";
                return $"{IpcProtocol.Ok}:{B64(BleManager.SshAuthorizedKey(pub, name))}:{B64(BleManager.SshFingerprint(pub))}";
            }
            case IpcProtocol.KeyAddOtp:
            {
                // KEY:ADDOTP:<b64name>:<b64service>:<b64secretBase32>
                if (parts.Length < 5) return IpcProtocol.ErrInvalidFormat;
                string name = FromB64(parts[2]);
                string service = FromB64(parts[3]);
                byte[]? secret = Base32Decode(FromB64(parts[4]));
                if (secret is null || secret.Length == 0) return IpcProtocol.ErrInvalidFormat;
                byte[] secretField = new byte[32];
                Array.Copy(secret, 0, secretField, 0, Math.Min(32, secret.Length));
                byte[] blob = Concat(BleManager.BuildField(name, 30), BleManager.BuildField(service, 30), secretField);
                bool ok = await _ble.WriteKeyEntryAsync(BleManager.CatOtp, 0xFF, blob);
                return ok ? IpcProtocol.Ok : IpcProtocol.Deny;
            }
            case IpcProtocol.KeyAddApi:
            {
                // KEY:ADDAPI:<b64name>:<b64value>
                if (parts.Length < 4) return IpcProtocol.ErrInvalidFormat;
                string name = FromB64(parts[2]);
                string value = FromB64(parts[3]);
                byte[] blob = Concat(BleManager.BuildField(name, 32), BleManager.BuildField(value, 128));
                bool ok = await _ble.WriteKeyEntryAsync(BleManager.CatApi, 0xFF, blob);
                return ok ? IpcProtocol.Ok : IpcProtocol.Deny;
            }
            case IpcProtocol.KeySshGen:
            {
                // KEY:SSHGEN:<b64name>
                if (parts.Length < 3) return IpcProtocol.ErrInvalidFormat;
                string name = FromB64(parts[2]);
                var res = await _ble.SshGenerateAsync(name);
                if (res is null) return IpcProtocol.Deny;
                return $"{IpcProtocol.Ok}:{B64(BleManager.SshAuthorizedKey(res.Value.PubBe, name))}:{B64(BleManager.SshFingerprint(res.Value.PubBe))}";
            }
            case IpcProtocol.KeyUpdate:
            {
                // KEY:UPDATE:<cat>:<idx>:<b64name>:<b64service> —— 仅改 name(+OTP service)，
                // 只写 name 字段；密文区靠固件 staging(1.2.8+) 从 flash 预载保留。
                if (parts.Length < 5 || !byte.TryParse(parts[2], out byte cat) || !byte.TryParse(parts[3], out byte idx))
                    return IpcProtocol.ErrInvalidFormat;
                string name = FromB64(parts[2 + 2]);      // parts[4]
                string service = parts.Length > 5 ? FromB64(parts[5]) : "";
                byte[] payload = cat switch
                {
                    BleManager.CatSsh => BleManager.BuildField(name, 16),
                    BleManager.CatOtp => Concat(BleManager.BuildField(name, 30), BleManager.BuildField(service, 30)),
                    _                 => BleManager.BuildField(name, 32), // API
                };
                bool ok = await _ble.WriteKeyEntryAsync(cat, idx, payload);
                return ok ? IpcProtocol.Ok : IpcProtocol.Deny;
            }
            default:
                return IpcProtocol.ErrUnknownCommand;
        }
    }

    private static string FromB64(string s)
    {
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        catch { return ""; }
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int total = 0;
        foreach (var p in parts) total += p.Length;
        byte[] r = new byte[total];
        int off = 0;
        foreach (var p in parts) { Array.Copy(p, 0, r, off, p.Length); off += p.Length; }
        return r;
    }

    /// <summary>RFC 4648 Base32 解码（忽略空格/连字符/大小写/填充）。失败返回 null。</summary>
    private static byte[]? Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = new System.Text.StringBuilder();
        foreach (char ch in input.Trim())
        {
            if (ch == '=' || ch == ' ' || ch == '-') continue;
            char up = char.ToUpperInvariant(ch);
            if (alphabet.IndexOf(up) < 0) return null;
            cleaned.Append(up);
        }
        string s = cleaned.ToString();
        if (s.Length == 0) return null;

        var output = new List<byte>(s.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (char ch in s)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(ch);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        return output.ToArray();
    }

    private static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));

    /// <summary>SSH agent 开关。</summary>
    private async Task<string> HandleSshAgentAsync(string[] parts)
    {
        if (parts.Length < 2) return IpcProtocol.ErrInvalidFormat;
        switch (parts[1])
        {
            case "STATUS":
                return $"{IpcProtocol.Ok}:{(_settings.SshAgentEnabled ? "ON" : "OFF")}";
            case "ON":
                _settings.SshAgentEnabled = true;
                _sshAgent.Start();
                await _sshAgent.RefreshIdentitiesAsync();
                return IpcProtocol.Ok;
            case "OFF":
                _settings.SshAgentEnabled = false;
                await _sshAgent.StopAsync();
                return IpcProtocol.Ok;
            default:
                return IpcProtocol.ErrUnknownCommand;
        }
    }

    /// <summary>
    /// 双主机槽位管理。
    /// 注意：连接检查按子命令分别做——CLEAROWN 在设备不在线时也必须可用（只清本地绑定），
    /// 否则本机会被旧绑定卡死、无法改配新设备。
    /// </summary>
    private async Task<string> HandleSlotAsync(string[] parts)
    {
        if (parts.Length < 2) return IpcProtocol.ErrInvalidFormat;

        switch (parts[1])
        {
            case IpcProtocol.SlotStatus:
            {
                if (!_ble.IsConnected) return IpcProtocol.ErrNotConnected;
                var s = await _ble.GetHostSlotStatusAsync();
                if (s is null) return IpcProtocol.Error;
                var (supported, bitmap, active) = s.Value;
                return $"{IpcProtocol.Ok}:{(supported ? 1 : 0)}:{bitmap}:{active}";
            }
            case IpcProtocol.SlotClearOwn:
            {
                // 不要求已连接：设备在线则通知设备清槽，不在线则只清本地绑定。
                var res = await _ble.ClearOwnSlotAsync();
                return res switch
                {
                    BleManager.ClearOwnResult.ClearedOnDevice => $"{IpcProtocol.Ok}:DEVICE",
                    BleManager.ClearOwnResult.ClearedLocalOnly => $"{IpcProtocol.Ok}:LOCAL",
                    _ => IpcProtocol.Reject,
                };
            }
            case IpcProtocol.SlotClear:
            {
                // 解绑「另一台」需要设备在线并过指纹门。
                if (!_ble.IsConnected) return IpcProtocol.ErrNotConnected;
                if (parts.Length < 3 || !byte.TryParse(parts[2], out byte slot))
                    return IpcProtocol.ErrInvalidFormat;
                bool ok = await _ble.ClearOtherSlotAsync(slot);
                return ok ? IpcProtocol.Ok : IpcProtocol.Deny;
            }
            default:
                return IpcProtocol.ErrUnknownCommand;
        }
    }

    /// <summary>登录密码配置（存 Windows 凭据管理器，供 CP 解锁时读取）。</summary>
    private string HandlePass(string[] parts)
    {
        if (parts.Length < 2) return IpcProtocol.ErrInvalidFormat;
        switch (parts[1])
        {
            case IpcProtocol.PassStatus:
                return _creds.HasPassword() ? $"{IpcProtocol.Ok}:CONFIGURED" : $"{IpcProtocol.Ok}:NOTSET";

            case IpcProtocol.PassSet:
            {
                // PASS:SET:<b64user>:<b64pass>
                if (parts.Length < 4) return IpcProtocol.ErrInvalidFormat;
                string user, pass;
                try
                {
                    user = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
                    pass = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
                }
                catch { return IpcProtocol.ErrInvalidFormat; }
                if (string.IsNullOrEmpty(pass)) return IpcProtocol.ErrInvalidFormat;
                if (string.IsNullOrEmpty(user)) user = Environment.UserName;
                _creds.Save(user, pass);
                _log.LogInformation("登录密码已保存（用户 {User}）", user);
                return IpcProtocol.Ok;
            }

            case IpcProtocol.PassClear:
                _creds.Clear();
                return IpcProtocol.Ok;

            default:
                return IpcProtocol.ErrUnknownCommand;
        }
    }

    /// <summary>
    /// AUTH:username:service —— 等待一次签名指纹匹配（30s 超时），用于未来的权限授权场景。
    /// 屏幕解锁走独立的 CP 管道，不经此路径。
    /// </summary>
    private async Task<string> HandleAuthAsync(string[] parts, CancellationToken ct)
    {
        if (!_ble.IsConnected) return IpcProtocol.ErrNotConnected;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMatch(ushort _) => tcs.TrySetResult(true);
        _ble.SignedFingerprintMatched += OnMatch;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(30000);
            await tcs.Task.WaitAsync(cts.Token);
            return IpcProtocol.Ok;
        }
        catch (OperationCanceledException)
        {
            return IpcProtocol.Timeout;
        }
        finally
        {
            _ble.SignedFingerprintMatched -= OnMatch;
        }
    }
}
