namespace ImmurokCommon.Models;

/// <summary>设备整体状态（Client 状态面板用）。</summary>
public sealed class DeviceStatus
{
    public bool Connected { get; init; }
    public string? DeviceName { get; init; }
    public bool Paired { get; init; }
    public bool PasswordConfigured { get; init; }
    public int? BatteryPercent { get; init; }
    public string? FirmwareVersion { get; init; }
    public int FingerprintCount { get; init; }
}

/// <summary>单个指纹槽。</summary>
public sealed class FingerprintSlot
{
    public byte SlotId { get; init; }
    public bool Enrolled { get; init; }
    public string? Name { get; set; }
}

/// <summary>配对失败原因，对齐 macOS <c>PairFailureReason</c>。</summary>
public enum PairFailureReason
{
    None = 0,
    Generic,
    NeedsReset, // 设备仍有指纹，需先复位（0xF1）
    LinkParams, // BLE 连接参数不达标（0xE1）
    WaitButtonTimeout,
}

/// <summary>OTA 进度。</summary>
public sealed class OtaProgress
{
    public long BytesWritten { get; init; }
    public long TotalBytes { get; init; }
    public string Phase { get; init; } = "";
    public double Fraction => TotalBytes > 0 ? (double)BytesWritten / TotalBytes : 0;
}
