using System;

namespace ImmurokCommon.Ble;

/// <summary>
/// GATT 服务 / 特征 UUID —— 权威值来自 macOS <c>app-macos/Sources/BLEManager.swift</c>，
/// 必须与固件一致，切勿改动。
/// </summary>
public static class BleConstants
{
    // immurok 自定义服务
    public static readonly Guid ImmurokService = new("45529919-7668-48f9-b9fe-e4eabe6595d9");
    public static readonly Guid CmdCharacteristic = new("8a537e1f-3992-4b2c-8b77-8d4e778186e1"); // Write
    public static readonly Guid RspCharacteristic = new("76a1660d-8cf6-44d1-b3fc-70486028e289"); // Notify

    // 标准 Device Information Service
    public static readonly Guid DeviceInfoService = ShortUuid(0x180A);
    public static readonly Guid FirmwareRevCharacteristic = ShortUuid(0x2A26);

    // 标准 Battery Service
    public static readonly Guid BatteryService = ShortUuid(0x180F);
    public static readonly Guid BatteryLevelCharacteristic = ShortUuid(0x2A19);

    // OTA 服务
    public static readonly Guid OtaService = new("d29005de-1391-4a54-8168-bf4e3c080430");
    public static readonly Guid OtaCharacteristic = new("c75f4c30-9a2d-4445-92e0-0e034c53d092");

    /// <summary>Bluetooth SIG 16-bit UUID → 128-bit（基座 0000xxxx-0000-1000-8000-00805F9B34FB）。</summary>
    public static Guid ShortUuid(ushort id) => new($"0000{id:X4}-0000-1000-8000-00805f9b34fb");
}
