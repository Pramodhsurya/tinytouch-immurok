using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ImmurokCommon.Protocol;

/// <summary>
/// 管道文本帧编解码：<c>[4 字节小端长度][UTF-8 文本]</c>。
/// 纯文本长度前缀帧，避免 BinaryFormatter（.NET 8 已移除且不安全），
/// 与 macOS 的文本行协议对齐，OTA 二进制块以 base64 内嵌进文本命令。
/// </summary>
public static class IpcFraming
{
    private const int MaxFrame = 4 * 1024 * 1024; // 4MB 上限，防恶意超长帧

    public static async Task WriteFrameAsync(Stream stream, string message, CancellationToken ct = default)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message);
        byte[] len = BitConverter.GetBytes(payload.Length); // 小端（x64）
        await stream.WriteAsync(len.AsMemory(0, 4), ct).ConfigureAwait(false);
        await stream.WriteAsync(payload.AsMemory(0, payload.Length), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>读取一帧；连接关闭返回 null。</summary>
    public static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] lenBuf = new byte[4];
        if (!await ReadExactAsync(stream, lenBuf, 4, ct).ConfigureAwait(false))
            return null;

        int len = BitConverter.ToInt32(lenBuf, 0);
        if (len <= 0 || len > MaxFrame)
            throw new InvalidDataException($"非法帧长度: {len}");

        byte[] buf = new byte[len];
        if (!await ReadExactAsync(stream, buf, len, ct).ConfigureAwait(false))
            return null;

        return Encoding.UTF8.GetString(buf, 0, len);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int total = 0;
        while (total < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, count - total), ct).ConfigureAwait(false);
            if (read == 0) return false; // 连接关闭
            total += read;
        }
        return true;
    }
}
