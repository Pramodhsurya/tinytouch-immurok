using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1.Sec;
// 注意：不整体 using Org.BouncyCastle.Math.EC，避免其 ECPoint 与 System.Security.Cryptography.ECPoint 冲突。

namespace ImmurokService.Security;

/// <summary>
/// ECDH 配对、HKDF 派生、HMAC 校验、挑战响应。
/// 参数逐字节对齐 macOS <c>ImmurokSecurity.swift</c> / 固件，切勿改动常量。
/// </summary>
public sealed class ImmurokSecurity
{
    // ⚠️ 权威常量：与固件一致。ARCHITECTURE.md 早期把 salt/info 写反，以此为准。
    private static readonly byte[] HkdfSalt = Encoding.ASCII.GetBytes("immurok-pairing-salt");
    private static readonly byte[] HkdfInfo = Encoding.ASCII.GetBytes("immurok-shared-key");
    private const int SharedKeyLen = 32;

    private readonly ILogger<ImmurokSecurity> _log;
    private readonly PairingStore _store;

    // 配对期间的临时私钥（startPairing → completePairing 之间）。
    private ECDiffieHellman? _ephemeral;

    public ImmurokSecurity(ILogger<ImmurokSecurity> log, PairingStore store)
    {
        _log = log;
        _store = store;
    }

    public bool IsPaired => _store.LoadSharedKey() is not null;

    // ---- ECDH 配对 ----

    /// <summary>生成临时密钥对，返回我方 compressed 公钥（33 字节）发给设备。</summary>
    public byte[] StartPairing()
    {
        _ephemeral?.Dispose();
        _ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        ECParameters p = _ephemeral.ExportParameters(false);
        return CompressPoint(p.Q.X!, p.Q.Y!);
    }

    /// <summary>
    /// 用设备的 compressed 公钥（33 字节）完成配对：ECDH → HKDF → 存 32B shared_key。
    /// </summary>
    public bool CompletePairing(byte[] deviceCompressedPubKey)
    {
        if (_ephemeral is null)
        {
            _log.LogError("CompletePairing: 无临时密钥");
            return false;
        }

        try
        {
            var (x, y) = DecompressPoint(deviceCompressedPubKey);
            using var peer = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y },
            });

            // 原始 ECDH 共享密钥 = 共享点的 X 坐标（32B），与 CryptoKit sharedSecretFromKeyAgreement 一致。
            byte[] ikm = _ephemeral.DeriveRawSecretAgreement(peer.PublicKey);

            // HKDF-SHA256 → 32B shared_key。
            byte[] sharedKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, SharedKeyLen, HkdfSalt, HkdfInfo);
            CryptographicOperations.ZeroMemory(ikm);

            _store.SaveSharedKey(sharedKey);
            CryptographicOperations.ZeroMemory(sharedKey);

            _ephemeral.Dispose();
            _ephemeral = null;
            _log.LogInformation("配对完成，shared_key 已保存");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "配对失败");
            _ephemeral?.Dispose();
            _ephemeral = null;
            return false;
        }
    }

    // ---- 0x21 签名指纹匹配校验 ----

    /// <summary>
    /// 校验签名指纹匹配通知。格式：<c>[0x21][page_id:2B LE][hmac:8B]</c>（11 字节）。
    /// message = data[0..3]；expected = HMAC-SHA256(shared_key, message)[0..8]。
    /// </summary>
    public (ushort PageId, bool Valid) VerifyFingerprintMatch(ReadOnlySpan<byte> data)
    {
        if (data.Length != 11 || data[0] != 0x21)
        {
            _log.LogWarning("0x21 通知长度非法: {Len}", data.Length);
            return (0, false);
        }

        byte[]? sharedKey = _store.LoadSharedKey();
        if (sharedKey is null)
        {
            _log.LogWarning("无 shared_key");
            return (0, false);
        }

        try
        {
            ushort pageId = (ushort)(data[1] | (data[2] << 8));
            ReadOnlySpan<byte> received = data.Slice(3, 8);

            Span<byte> full = stackalloc byte[32];
            HMACSHA256.HashData(sharedKey, data.Slice(0, 3), full);
            bool valid = CryptographicOperations.FixedTimeEquals(full[..8], received);

            if (!valid) _log.LogWarning("HMAC 不匹配");
            return (pageId, valid);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedKey);
        }
    }

    // ---- 挑战响应 ----

    /// <summary>生成 8 字节随机 nonce。</summary>
    public byte[] GenerateChallenge() => RandomNumberGenerator.GetBytes(8);

    /// <summary>校验设备响应：HMAC-SHA256(shared_key, nonce)[0..8] == response。</summary>
    public bool VerifyChallengeResponse(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> response)
    {
        byte[]? sharedKey = _store.LoadSharedKey();
        if (sharedKey is null) return false;
        try
        {
            Span<byte> full = stackalloc byte[32];
            HMACSHA256.HashData(sharedKey, nonce, full);
            return CryptographicOperations.FixedTimeEquals(full[..8], response);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedKey);
        }
    }

    public void ClearPairing() => _store.ClearSharedKey();

    // ---- P-256 点压缩/解压 ----

    private static byte[] CompressPoint(byte[] x, byte[] y)
    {
        // x/y 为 32 字节大端。前缀 0x02（y 为偶）/ 0x03（y 为奇）。
        byte[] result = new byte[33];
        result[0] = (byte)(0x02 | (y[^1] & 1));
        Array.Copy(x, 0, result, 1, 32);
        return result;
    }

    private static (byte[] X, byte[] Y) DecompressPoint(byte[] compressed)
    {
        if (compressed.Length != 33 || (compressed[0] != 0x02 && compressed[0] != 0x03))
            throw new ArgumentException("非法 compressed P-256 公钥");

        var curve = SecNamedCurves.GetByName("secp256r1");
        var point = curve.Curve.DecodePoint(compressed).Normalize();
        byte[] x = To32(point.AffineXCoord.ToBigInteger().ToByteArrayUnsigned());
        byte[] y = To32(point.AffineYCoord.ToBigInteger().ToByteArrayUnsigned());
        return (x, y);
    }

    private static byte[] To32(byte[] b)
    {
        if (b.Length == 32) return b;
        byte[] r = new byte[32];
        Array.Copy(b, 0, r, 32 - b.Length, b.Length); // 左侧补零到 32 字节大端
        return r;
    }
}
