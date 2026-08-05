using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Security;

/// <summary>
/// 配对 shared_key（32B）与已验证设备 UUID 的持久化。
/// 用 DPAPI（LocalMachine 作用域，因 Service 以 LocalSystem 运行）加密后存到 %ProgramData%\immurok。
/// 对应 macOS 的 Keychain <c>com.immurok.shared-key</c> / <c>com.immurok.verified-device</c>。
/// </summary>
public sealed class PairingStore
{
    private static readonly byte[] Entropy = "immurok-dpapi-v1"u8.ToArray();

    private readonly ILogger<PairingStore> _log;
    private readonly string _dir;
    private readonly string _keyPath;
    private readonly string _verifiedPath;

    public PairingStore(ILogger<PairingStore> log)
    {
        _log = log;
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "immurok");
        Directory.CreateDirectory(_dir);
        _keyPath = Path.Combine(_dir, "pairing.dat");
        _verifiedPath = Path.Combine(_dir, "verified.dat");
    }

    public void SaveSharedKey(byte[] key) => ProtectToFile(_keyPath, key);

    public byte[]? LoadSharedKey() => UnprotectFromFile(_keyPath);

    public void ClearSharedKey()
    {
        TryDelete(_keyPath);
        TryDelete(_verifiedPath);
        _log.LogInformation("配对数据已清除");
    }

    public void SaveVerifiedDevice(string uuid)
        => ProtectToFile(_verifiedPath, System.Text.Encoding.UTF8.GetBytes(uuid));

    public bool IsVerifiedDevice(string uuid)
    {
        byte[]? data = UnprotectFromFile(_verifiedPath);
        if (data is null) return false;
        return System.Text.Encoding.UTF8.GetString(data) == uuid;
    }

    private void ProtectToFile(string path, byte[] plaintext)
    {
        try
        {
            byte[] enc = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(path, enc);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DPAPI 写入失败: {Path}", path);
        }
    }

    private byte[]? UnprotectFromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            byte[] enc = File.ReadAllBytes(path);
            return ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.LocalMachine);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DPAPI 读取失败: {Path}", path);
            return null;
        }
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _log.LogWarning(ex, "删除失败: {Path}", path); }
    }
}
