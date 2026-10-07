using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ImmurokCommon.Protocol;

namespace ImmurokClient.Services;

/// <summary>固件语义化版本 "MAJOR.MINOR.PATCH"（对齐 macOS FirmwareVersion）。</summary>
public readonly record struct FwVersion(int Major, int Minor, int Patch) : IComparable<FwVersion>
{
    /// <summary>设备 getStatus 可能报第 4 段 build（"a.b.c.build"），归一到 3 段 semver。</summary>
    public static string Normalize(string raw)
    {
        var parts = raw.Split('.');
        return parts.Length >= 3 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : raw;
    }

    public static FwVersion? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var p = s.Split('.');
        if (p.Length != 3) return null;
        if (int.TryParse(p[0], out int ma) && int.TryParse(p[1], out int mi) && int.TryParse(p[2], out int pa)
            && ma >= 0 && mi >= 0 && pa >= 0)
            return new FwVersion(ma, mi, pa);
        return null;
    }

    public int CompareTo(FwVersion o)
    {
        int c = Major.CompareTo(o.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(o.Minor);
        if (c != 0) return c;
        return Patch.CompareTo(o.Patch);
    }

    public static bool operator <(FwVersion a, FwVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(FwVersion a, FwVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(FwVersion a, FwVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(FwVersion a, FwVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>升级规划（对齐 macOS UpdatePlan）。</summary>
public enum FwPlan { UpToDate, Direct, BridgeOnly, TwoHops, Unknown }

/// <summary>官网固件更新清单（schema=1，对齐 macOS UpdateManifest）。</summary>
public sealed class FwManifest
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("latest")] public FwAsset? Latest { get; set; }
    [JsonPropertyName("bridge")] public FwAsset? Bridge { get; set; }
}

public sealed class FwAsset
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("sec_version")] public int? SecVersion { get; set; }
    [JsonPropertyName("format")] public string? Format { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public int? Size { get; set; }
    [JsonPropertyName("min_direct")] public string? MinDirect { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}

/// <summary>升级流程阶段（AboutPage 据此映射本地化提示）。</summary>
public enum FwStage { Downloading, DownloadingBridge, Pushing, PushingBridge, WaitingReconnect }

/// <summary>检查结果。</summary>
public sealed record FwCheckResult(bool Available, FwPlan Plan, string? DeviceVersion, string? LatestVersion, string? Notes);

/// <summary>升级结果：Ok / 失败错误码（AboutPage 映射本地化）。</summary>
public sealed record FwUpdateResult(bool Ok, string? ErrorCode, string? Version);

/// <summary>
/// 客户端固件自动更新（对齐 macOS FirmwareUpdateService）：
/// 拉取 manifest → 版本比较与规划 → 下载并校验 sha256 → 逐跳经 Service 的 OTA 通道推送 → 等待重连核对版本。
/// HTTP 在用户会话内完成；实际 BLE OTA 仍由 Service 执行（复用 OTA:PUSH 流式管道）。
/// </summary>
public sealed class FirmwareUpdateService
{
    /// <summary>官网清单地址（与 macOS 一致）。</summary>
    public const string ManifestUrl = "https://immurok.com/fw/manifest.json";

    /// <summary>桥版底线（低于此版本需先经桥升级；与 macOS fallbackMinDirect 一致）。</summary>
    public const string FallbackMinDirect = "1.6.0";

    /// <summary>升级所需的最低剩余电量（%）。传输中断电会导致设备变砖，故升级前硬性预检。</summary>
    public const int MinBatteryPct = 50;

    private const int ReconnectTimeoutSec = 60;

    private static readonly HttpClient Http = CreateHttp();

    private FwManifest? _manifest;

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("immurok-win/0.1");
        return h;
    }

    /// <summary>升级规划（对齐 macOS UpdatePlanner.plan）。</summary>
    public static FwPlan Plan(string device, string latest, string? minDirect)
    {
        var dev = FwVersion.Parse(FwVersion.Normalize(device));
        var tgt = FwVersion.Parse(latest);
        var gate = FwVersion.Parse(minDirect ?? FallbackMinDirect);
        if (dev is null || tgt is null || gate is null) return FwPlan.Unknown;
        if (dev.Value >= tgt.Value) return FwPlan.UpToDate;
        if (dev.Value >= gate.Value) return FwPlan.Direct;
        return tgt.Value == gate.Value ? FwPlan.BridgeOnly : FwPlan.TwoHops;
    }

    /// <summary>拉取 manifest 并与设备版本比较。失败抛异常（AboutPage 提示"检查失败"）。</summary>
    public async Task<FwCheckResult> CheckAsync(string deviceVersion, CancellationToken ct = default)
    {
        string json = await Http.GetStringAsync(ManifestUrl, ct).ConfigureAwait(false);
        var m = JsonSerializer.Deserialize<FwManifest>(json)
                ?? throw new InvalidOperationException("manifest parse failed");
        if (m.Schema != 1) throw new InvalidOperationException($"unsupported schema {m.Schema}");
        if (m.Latest is null || string.IsNullOrEmpty(m.Latest.Version))
            throw new InvalidOperationException("manifest missing latest");
        _manifest = m;

        var plan = Plan(deviceVersion, m.Latest.Version, m.Latest.MinDirect);
        bool available = plan != FwPlan.UpToDate && plan != FwPlan.Unknown;
        return new FwCheckResult(available, plan, FwVersion.Normalize(deviceVersion), m.Latest.Version, m.Latest.Notes);
    }

    /// <summary>
    /// 执行升级：按规划逐跳下载→校验→推送→等重连。onStage/onPercent 驱动 UI。
    /// 须在 CheckAsync 之后调用（复用其缓存的 manifest）。
    /// </summary>
    public async Task<FwUpdateResult> RunUpdateAsync(
        string deviceVersion,
        Action<FwStage> onStage,
        Action<int> onPercent,
        CancellationToken ct = default)
    {
        var m = _manifest;
        if (m?.Latest is null) return new FwUpdateResult(false, "no_manifest", null);

        // 预检：剩余电量必须 >= MinBatteryPct（对齐 macOS preflight）。
        // 读不到电量（null）时不阻断，避免旧固件无法升级。
        if (await ReadBatteryAsync(ct).ConfigureAwait(false) is int batt && batt < MinBatteryPct)
            return new FwUpdateResult(false, "battery", null);

        var plan = Plan(deviceVersion, m.Latest.Version, m.Latest.MinDirect);

        // 规划跳数（对齐 macOS runUpdate）
        var hops = new List<(string role, string version)>();
        switch (plan)
        {
            case FwPlan.Direct: hops.Add(("final", m.Latest.Version)); break;
            case FwPlan.BridgeOnly: hops.Add(("bridge", m.Bridge?.Version ?? m.Latest.Version)); break;
            case FwPlan.TwoHops:
                hops.Add(("bridge", m.Bridge?.Version ?? FallbackMinDirect));
                hops.Add(("final", m.Latest.Version));
                break;
            default: return new FwUpdateResult(false, "up_to_date", null);
        }

        // 预下载并校验目标包
        byte[] targetData;
        try
        {
            onStage(FwStage.Downloading);
            targetData = await DownloadVerifiedAsync(m.Latest, ct).ConfigureAwait(false);
        }
        catch (Exception) { return new FwUpdateResult(false, "download", null); }

        for (int i = 0; i < hops.Count; i++)
        {
            var hop = hops[i];
            bool isLast = i == hops.Count - 1;
            byte[] pkg;
            if (hop.role == "bridge")
            {
                if (m.Bridge is null) return new FwUpdateResult(false, "bridge", null);
                try
                {
                    onStage(FwStage.DownloadingBridge);
                    pkg = await DownloadVerifiedAsync(m.Bridge, ct).ConfigureAwait(false);
                }
                catch (Exception) { return new FwUpdateResult(false, "bridge", null); }
            }
            else
            {
                pkg = targetData;
            }

            // 经 Service 的 OTA 通道推送。
            // 多跳时把每跳的 0~100 折算进整体进度，进度条保持单调递增（对齐 macOS baseFraction+hopWeight）。
            int baseP = i * 100 / hops.Count;
            int weight = 100 / hops.Count;
            void OnHopPercent(int p) => onPercent(baseP + p * weight / 100);

            onStage(hop.role == "bridge" ? FwStage.PushingBridge : FwStage.Pushing);
            OnHopPercent(0);
            string terminal = await AppServices.Pipe.OtaPushStreamAsync(pkg, OnHopPercent, ct).ConfigureAwait(false);
            if (!terminal.Contains("DONE"))
            {
                string code = terminal.Contains("OTA_NOT_AVAILABLE") ? "no_channel"
                    : terminal.Contains("TIMEOUT") ? "timeout"
                    : "transfer";
                return new FwUpdateResult(false, code, null);
            }

            // 等设备重启重连并核对版本。
            onStage(FwStage.WaitingReconnect);
            bool reconnected = await WaitForVersionAsync(hop.version, ReconnectTimeoutSec, ct).ConfigureAwait(false);
            // 中间跳（桥）必须重连成功才能推下一跳；最后一跳 DONE 已代表写入成功，
            // 重连仅作核对，超时也按成功返回（设备正在重启，稍后自动重连）。
            if (!reconnected && !isLast) return new FwUpdateResult(false, "reconnect", null);
        }

        return new FwUpdateResult(true, null, m.Latest.Version);
    }

    /// <summary>读设备剩余电量（%）。读不到返回 null（不阻断升级）。</summary>
    private static async Task<int?> ReadBatteryAsync(CancellationToken ct)
    {
        string? info = await AppServices.Pipe.InfoAsync(ct).ConfigureAwait(false);
        // INFO -> OK:<paired>:<battery 或 -1>:<fw>:<fpcount>
        if (info is null || !info.StartsWith(IpcProtocol.Ok)) return null;
        string[] p = info.Split(IpcProtocol.Sep);
        if (p.Length >= 3 && int.TryParse(p[2], out int b) && b >= 0) return b;
        return null;
    }

    /// <summary>下载资源并校验 sha256（不符按下载失败）。</summary>
    private static async Task<byte[]> DownloadVerifiedAsync(FwAsset asset, CancellationToken ct)
    {
        byte[] data = await Http.GetByteArrayAsync(asset.Url, ct).ConfigureAwait(false);
        string digest = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        if (!string.IsNullOrEmpty(asset.Sha256) &&
            !string.Equals(digest, asset.Sha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidOperationException("sha256 mismatch");
        return data;
    }

    /// <summary>轮询 INFO，直到设备重连且上报版本 &gt;= 目标版本（容忍第 4 段 build）。</summary>
    private static async Task<bool> WaitForVersionAsync(string version, int timeoutSec, CancellationToken ct)
    {
        var target = FwVersion.Parse(FwVersion.Normalize(version));
        if (target is null) return false;
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            string? info = await AppServices.Pipe.InfoAsync(ct).ConfigureAwait(false);
            // INFO -> OK:<paired>:<battery>:<fw>:<fpcount>
            if (info is not null && info.StartsWith(IpcProtocol.Ok))
            {
                string[] p = info.Split(IpcProtocol.Sep);
                if (p.Length >= 4)
                {
                    var dev = FwVersion.Parse(FwVersion.Normalize(p[3]));
                    if (dev is not null && dev.Value >= target.Value) return true;
                }
            }
            try { await Task.Delay(1000, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }
}
