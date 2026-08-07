using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using ImmurokCommon.Protocol;

namespace ImmurokClient.Services;

/// <summary>
/// 连接 Service 的命名管道客户端（文本协议）。每次请求短连接：连接→发一帧→收一帧→关闭。
/// </summary>
public sealed class PipeClient
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string?> SendAsync(string request, int timeoutMs = 15000, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var client = new NamedPipeClientStream(".", PipeNames.ClientService,
                PipeDirection.InOut, PipeOptions.Asynchronous);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            await client.ConnectAsync(cts.Token).ConfigureAwait(false);
            await IpcFraming.WriteFrameAsync(client, request, cts.Token).ConfigureAwait(false);
            return await IpcFraming.ReadFrameAsync(client, cts.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null; // Service 未运行 / 超时
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- 便捷封装 ----

    public Task<string?> StatusAsync(CancellationToken ct = default)
        => SendAsync(IpcProtocol.Status, ct: ct);

    public Task<string?> InfoAsync(CancellationToken ct = default)
        => SendAsync(IpcProtocol.Info, ct: ct);

    /// <summary>流式 OTA 推送：发整个 .imfw（base64），回调百分比，返回终态帧。</summary>
    public async Task<string> OtaPushStreamAsync(byte[] imfw, Action<int> onPercent, CancellationToken ct = default)
    {
        try
        {
            string b64 = Convert.ToBase64String(imfw);
            using var client = new NamedPipeClientStream(".", PipeNames.ClientService,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000, ct).ConfigureAwait(false);
            await IpcFraming.WriteFrameAsync(client, $"{IpcProtocol.Ota}:{IpcProtocol.OtaPush}:{b64}", ct)
                .ConfigureAwait(false);
            while (true)
            {
                string? frame = await IpcFraming.ReadFrameAsync(client, ct).ConfigureAwait(false);
                if (frame is null) return "ERROR:DISCONNECTED";
                if (frame.StartsWith("PROGRESS:", StringComparison.Ordinal))
                {
                    if (int.TryParse(frame.AsSpan(9), out int pct)) onPercent(pct);
                    continue;
                }
                return frame;
            }
        }
        catch (Exception ex) { return $"ERROR:{ex.Message}"; }
    }

    public Task<string?> PairStatusAsync(CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Pair}:{IpcProtocol.PairStatus}", ct: ct);

    public Task<string?> PairStartAsync(CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Pair}:{IpcProtocol.PairStart}", timeoutMs: 90000, ct: ct);

    public Task<string?> PairResetAsync(CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Pair}:{IpcProtocol.PairReset}", ct: ct);

    // ---- 双主机 ----
    public Task<string?> SlotStatusAsync(CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Slot}:{IpcProtocol.SlotStatus}", ct: ct);

    public Task<string?> SlotClearOwnAsync(CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Slot}:{IpcProtocol.SlotClearOwn}", timeoutMs: 15000, ct: ct);

    /// <summary>解绑另一台（需在设备上触摸已登记指纹）。</summary>
    public Task<string?> SlotClearAsync(byte slot, CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Slot}:{IpcProtocol.SlotClear}:{slot}", timeoutMs: 35000, ct: ct);

    public Task<string?> FpListAsync(CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Fp}:{IpcProtocol.FpList}", ct: ct);

    public Task<string?> FpSlotsAsync(CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Fp}:{IpcProtocol.FpSlots}", ct: ct);

    /// <summary>
    /// 流式录入：连接后发 FP:ENROLL，循环读取进度帧（PROGRESS:...）回调 onProgress，
    /// 直到收到终态帧（OK:COMPLETE / ERROR:*）并返回之。
    /// </summary>
    public async Task<string> EnrollStreamAsync(byte slot, Action<string> onProgress, CancellationToken ct = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeNames.ClientService,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(3000, ct).ConfigureAwait(false);
            await IpcFraming.WriteFrameAsync(client, $"{IpcProtocol.Fp}:{IpcProtocol.FpEnroll}:{slot}", ct)
                .ConfigureAwait(false);

            while (true)
            {
                string? frame = await IpcFraming.ReadFrameAsync(client, ct).ConfigureAwait(false);
                if (frame is null) return "ERROR:DISCONNECTED";
                if (frame.StartsWith("PROGRESS:", StringComparison.Ordinal))
                {
                    onProgress(frame);
                    continue;
                }
                return frame; // 终态
            }
        }
        catch (Exception ex)
        {
            return $"ERROR:{ex.Message}";
        }
    }

    /// <summary>删除指纹（设备端要过 30s 指纹门，故本地超时留出余量）。</summary>
    public Task<string?> FpDeleteAsync(byte slot, CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Fp}:{IpcProtocol.FpDelete}:{slot}", timeoutMs: 40000, ct: ct);

    /// <summary>读功能开关 -> OK:&lt;unlock&gt;:&lt;lock&gt;:&lt;ssh&gt;:&lt;agent&gt;:&lt;otp&gt;（各 0/1）。</summary>
    public Task<string?> FeatureGetAsync(CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Feature}:{IpcProtocol.FeatureGet}", ct: ct);

    /// <summary>写功能开关。ssh 会连带启停 agent，故留长一点超时。</summary>
    public Task<string?> FeatureSetAsync(string name, bool on, CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Feature}:{IpcProtocol.FeatureSet}:{name}:{(on ? 1 : 0)}", timeoutMs: 20000, ct: ct);

    /// <summary>取消进行中的指纹门（认证弹窗点「取消」）。</summary>
    public Task<string?> CancelGateAsync(CancellationToken ct = default)
        => SendAsync(IpcProtocol.CancelGate, timeoutMs: 5000, ct: ct);

    public Task<string?> PassStatusAsync(CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Pass}:{IpcProtocol.PassStatus}", ct: ct);

    public Task<string?> PassSetAsync(string user, string password, CancellationToken ct = default)
    {
        string bu = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(user));
        string bp = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
        return SendAsync($"{IpcProtocol.Pass}:{IpcProtocol.PassSet}:{bu}:{bp}", ct: ct);
    }

    public Task<string?> PassClearAsync(CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Pass}:{IpcProtocol.PassClear}", ct: ct);

    // ---- 密钥库 ----
    public Task<string?> KeyListAsync(byte cat, CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Key}:{IpcProtocol.KeyList}:{cat}", timeoutMs: 30000, ct: ct);

    public Task<string?> KeyOtpAsync(byte idx, CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Key}:{IpcProtocol.KeyOtp}:{idx}", timeoutMs: 35000, ct: ct);

    public Task<string?> KeyDeleteAsync(byte cat, byte idx, CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Key}:{IpcProtocol.KeyDelete}:{cat}:{idx}", timeoutMs: 35000, ct: ct);

    public Task<string?> KeySshPubAsync(byte idx, CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Key}:{IpcProtocol.KeySshPub}:{idx}", ct: ct);

    public Task<string?> KeyAddOtpAsync(string name, string service, string secretB32, CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Key}:{IpcProtocol.KeyAddOtp}:{B64(name)}:{B64(service)}:{B64(secretB32)}", timeoutMs: 35000, ct: ct);

    public Task<string?> KeyAddApiAsync(string name, string value, CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Key}:{IpcProtocol.KeyAddApi}:{B64(name)}:{B64(value)}", timeoutMs: 35000, ct: ct);

    public Task<string?> KeySshGenAsync(string name, CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Key}:{IpcProtocol.KeySshGen}:{B64(name)}", timeoutMs: 35000, ct: ct);

    /// <summary>重命名条目（OTP 可同时改服务名；SSH/API 的 service 传空）。</summary>
    public Task<string?> KeyUpdateAsync(byte cat, byte idx, string name, string service, CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.Key}:{IpcProtocol.KeyUpdate}:{cat}:{idx}:{B64(name)}:{B64(service)}", timeoutMs: 35000, ct: ct);

    private static string B64(string s)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));

    // ---- SSH agent ----
    public Task<string?> SshAgentStatusAsync(CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.SshAgent}:STATUS", ct: ct);

    public Task<string?> SshAgentSetAsync(bool on, CancellationToken ct = default)
        => SendAsync($"{IpcProtocol.SshAgent}:{(on ? "ON" : "OFF")}", timeoutMs: 20000, ct: ct);
}
