using ImmurokService.Ble;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Ota;

/// <summary>
/// WCH IAP OTA 推送引擎。移植自 macOS <c>OTAEngine.swift</c> / <c>IMFWPackage.swift</c>。
/// 主机只解析 .imfw、把 header + **已加密固件原样推**给设备；解密/验签由设备完成（主机不做密码学）。
/// 所有命令写 OTA 特征（与主命令通道分开）。
/// </summary>
public sealed class OtaEngine
{
    private const int ChunkSize = 240;         // ≤243 且 16B 对齐
    private const int ImageBBlocks = 54;       // 216KB / 4KB
    private const uint Magic = 0x494D4657;     // "IMFW"
    private const int HeaderV1 = 96;
    private const int HeaderV2 = 128;
    private const int ImageBSize = 216 * 1024;

    private readonly BleManager _ble;
    private readonly ILogger _log;

    public OtaEngine(BleManager ble, ILogger log)
    {
        _ble = ble;
        _log = log;
    }

    public sealed record OtaResult(bool Ok, string Message);

    /// <summary>推送一个 .imfw。progress ∈ [0,1]。</summary>
    public async Task<OtaResult> PushAsync(byte[] imfw, Action<double> progress, CancellationToken ct)
    {
        // ---- 解析 .imfw ----
        if (imfw.Length < HeaderV1) return new(false, "文件过短");
        uint magic = ReadU32LE(imfw, 0);
        if (magic != Magic) return new(false, "非法魔数（不是 .imfw）");
        byte version = imfw[4];
        if (version != 1 && version != 2) return new(false, $"不支持的格式版本 {version}");
        int headerSize = version >= 2 ? HeaderV2 : HeaderV1;
        if (imfw.Length < headerSize) return new(false, "文件截断");

        uint fwSize = ReadU32LE(imfw, 8);
        byte[] header = imfw[..headerSize];
        byte[] encFw = imfw[headerSize..];
        if (fwSize > ImageBSize || encFw.Length > ImageBSize) return new(false, "固件过大");
        if (encFw.Length < fwSize) return new(false, "固件大小不符");

        if (!_ble.OtaAvailable) return new(false, "OTA 通道不可用（设备未暴露 OTA 特征）");

        // ---- 1. INFO 握手 ----
        if (await _ble.OtaWriteReadAsync(new byte[] { 0x84, 0x02, 0x00, 0x00 }, 5000) is null)
            return new(false, "INFO 无响应");

        // ---- 2. ERASE Image B ----
        byte[] erase = { 0x81, 0x04, 0x00, 0x00, ImageBBlocks & 0xFF, (ImageBBlocks >> 8) & 0xFF };
        byte[]? eraseResp = await _ble.OtaWriteReadAsync(erase, 15000);
        if (eraseResp is null) return new(false, "ERASE 无响应");
        if (eraseResp.Length < 1 || eraseResp[0] != 0x00) return new(false, $"ERASE 失败 0x{(eraseResp.Length > 0 ? eraseResp[0] : 0xFF):X2}");

        // ---- 3. HEADER ----
        byte[] headerCmd = new byte[2 + header.Length];
        headerCmd[0] = 0x85;
        headerCmd[1] = (byte)header.Length;
        Array.Copy(header, 0, headerCmd, 2, header.Length);
        byte[]? hdrResp = await _ble.OtaWriteReadAsync(headerCmd, 5000);
        if (hdrResp is null) return new(false, "HEADER 无响应");
        if (hdrResp.Length < 1 || hdrResp[0] != 0x00) return new(false, $"HEADER 被拒 0x{(hdrResp.Length > 0 ? hdrResp[0] : 0xFF):X2}（可能是硬件不符或版本反降级）");

        // ---- 4. PROM 分块写（无响应）----
        int total = encFw.Length;
        int offset = 0;
        while (offset < total)
        {
            ct.ThrowIfCancellationRequested();
            int end = Math.Min(offset + ChunkSize, total);
            int len = end - offset;
            int addr = offset / 16;
            byte[] cmd = new byte[4 + len];
            cmd[0] = 0x80;
            cmd[1] = (byte)len;
            cmd[2] = (byte)(addr & 0xFF);
            cmd[3] = (byte)((addr >> 8) & 0xFF);
            Array.Copy(encFw, offset, cmd, 4, len);
            if (!await _ble.OtaWriteNoResponseAsync(cmd))
                return new(false, $"数据块写失败 @offset {offset}");
            offset = end;
            progress((double)offset / total);
        }

        // ---- 5. END：设备验签后重启；无响应=已重启=成功 ----
        byte[]? endResp = await _ble.OtaWriteReadAsync(new byte[] { 0x83, 0x02, 0x00, 0x00 }, 15000);
        if (endResp is { Length: >= 1 })
        {
            switch (endResp[0])
            {
                case 0xF1: return new(false, "END：SHA256 校验失败");
                case 0xF2: return new(false, "END：签名校验失败");
            }
        }
        return new(true, "已推送并触发设备重启");
    }

    private static uint ReadU32LE(byte[] b, int i)
        => (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24));
}
