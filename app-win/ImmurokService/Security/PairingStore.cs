using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Security;

/// <summary>
/// 配对 shared_key（32B）与已验证设备 UUID 的持久化，DPAPI 加密后存到 %ProgramData%\immurok。
/// 作用域 <see cref="DataProtectionScope.CurrentUser"/>（Service 以 LocalSystem 运行，即 SYSTEM 的用户主密钥）：
/// 原来的 LocalMachine 作用域同机任意进程都能解，配对密钥等于明文落盘（设计稿 §1.4 / §3.4）。
/// 老文件首次读到时迁移：CurrentUser 重加密 → 解回来验证 → 原文件改名 <c>.machine</c> 后替换（§9.5）。
/// 新格式文件带 <see cref="Magic"/> 头；**不能靠 Unprotect 的 scope 参数区分**——CryptUnprotectData 不看它，
/// 作用域记在 blob 里，LocalMachine 的旧文件用 CurrentUser 参数照样解开，没有头就分不出新旧。
/// 收益只对普通用户进程有效：SYSTEM 的主密钥在 %WINDIR%\System32\Microsoft\Protect\S-1-5-18，
/// 管理员可读——他们本来就在攻击者模型之外。
/// 对应 macOS 的 Keychain <c>com.immurok.shared-key</c> / <c>com.immurok.verified-device</c>。
/// </summary>
public sealed class PairingStore
{
    /// <summary>pairing.dat 的加密作用域（供 SECURITY:STATUS 的 <c>pairing_scope</c>）。</summary>
    public enum Scope { None, User, Machine, Unreadable }

    private static readonly byte[] Entropy = "immurok-dpapi-v1"u8.ToArray();
    /// <summary>新格式（CurrentUser 作用域）文件头；没有这个头的是老的 LocalMachine 文件。</summary>
    private static readonly byte[] Magic = "IMKDPU1"u8.ToArray();
    private static readonly bool IsSystem =
        WindowsIdentity.GetCurrent().User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true;

    private readonly ILogger<PairingStore> _log;
    private readonly string _dir;
    private readonly string _keyPath;
    private readonly string _verifiedPath;
    private readonly object _lock = new();
    private bool _warnedUnreadable;
    private bool _warnedNotSystem;

    public PairingStore(ILogger<PairingStore> log)
    {
        _log = log;
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "immurok");
        Directory.CreateDirectory(_dir);
        _keyPath = Path.Combine(_dir, "pairing.dat");
        _verifiedPath = Path.Combine(_dir, "verified.dat");
    }

    public Scope KeyScope { get; private set; } = Scope.None;

    public void SaveSharedKey(byte[] key)
    {
        ProtectToFile(_keyPath, key);
        KeyScope = Scope.User;
    }

    public byte[]? LoadSharedKey()
    {
        var (data, scope) = UnprotectFromFile(_keyPath);
        KeyScope = scope;
        return data;
    }

    public void ClearSharedKey()
    {
        TryDelete(_keyPath);
        TryDelete(_keyPath + ".machine");   // 迁移留下的 LocalMachine 副本同机可解，清配对时一并删
        TryDelete(_verifiedPath);
        TryDelete(_verifiedPath + ".machine");
        KeyScope = Scope.None;
        _log.LogInformation("配对数据已清除");
    }

    public void SaveVerifiedDevice(string uuid)
        => ProtectToFile(_verifiedPath, System.Text.Encoding.UTF8.GetBytes(uuid));

    public bool IsVerifiedDevice(string uuid)
    {
        byte[]? data = UnprotectFromFile(_verifiedPath).Data;
        if (data is null) return false;
        return System.Text.Encoding.UTF8.GetString(data) == uuid;
    }

    private void ProtectToFile(string path, byte[] plaintext)
    {
        lock (_lock)
        {
            try
            {
                File.WriteAllBytes(path, WithMagic(ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser)));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "DPAPI 写入失败: {Path}", path);
            }
        }
    }

    private (byte[]? Data, Scope Scope) UnprotectFromFile(string path)
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(path)) return (null, Scope.None);
                byte[] file = File.ReadAllBytes(path);

                if (HasMagic(file))
                {
                    // 新格式：CurrentUser。解不开 = 服务账户被改过（主密钥不是现在这个账户的）。
                    try
                    {
                        return (ProtectedData.Unprotect(file[Magic.Length..], Entropy, DataProtectionScope.CurrentUser), Scope.User);
                    }
                    catch (CryptographicException ex)
                    {
                        if (!_warnedUnreadable)
                        {
                            _log.LogError(ex, "配对数据用当前服务账户解不开（服务账户被改过？），需要重新配对: {Path}", path);
                            _warnedUnreadable = true;
                        }
                        return (null, Scope.Unreadable);
                    }
                }

                // 老格式：LocalMachine（scope 参数对 Unprotect 无意义，blob 自带）。
                byte[] plain;
                try
                {
                    plain = ProtectedData.Unprotect(file, Entropy, DataProtectionScope.LocalMachine);
                }
                catch (CryptographicException ex)
                {
                    if (!_warnedUnreadable)
                    {
                        _log.LogError(ex, "既有配对数据解不开，需要重新配对: {Path}", path);
                        _warnedUnreadable = true;
                    }
                    return (null, Scope.Unreadable);
                }

                return Migrate(path, plain) ? (plain, Scope.User) : (plain, Scope.Machine);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "DPAPI 读取失败: {Path}", path);
                return (null, Scope.Unreadable);
            }
        }
    }

    /// <summary>
    /// LocalMachine → CurrentUser。先验证再替换：重加密 → 解回来比对 → 写临时文件 → 原文件改名
    /// <c>.machine</c>（回滚时改回来即可）→ 临时文件改名到位。任何一步失败都不动原文件。
    /// 只在以 SYSTEM 运行时做：控制台调试模式下用开发者的用户密钥重写，会把真正的服务锁在外面。
    /// </summary>
    private bool Migrate(string path, byte[] plain)
    {
        if (!IsSystem)
        {
            if (!_warnedNotSystem)
            {
                _log.LogWarning("非 SYSTEM 身份运行，不迁移 DPAPI 作用域（仍按 LocalMachine 读取）");
                _warnedNotSystem = true;
            }
            return false;
        }

        try
        {
            byte[] enc = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            byte[] back = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
            if (!CryptographicOperations.FixedTimeEquals(back, plain))
            {
                _log.LogError("DPAPI 迁移验证失败（解回来的内容不一致），保留原文件: {Path}", path);
                return false;
            }

            string tmp = path + ".tmp";
            string backup = path + ".machine";
            File.WriteAllBytes(tmp, WithMagic(enc));
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(path, backup);
            File.Move(tmp, path);
            _log.LogInformation("DPAPI 作用域已从 LocalMachine 迁移到 CurrentUser（旧文件保留为 .machine）: {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DPAPI 迁移失败，保留原文件: {Path}", path);
            return false;
        }
    }

    private static bool HasMagic(byte[] file)
        => file.Length > Magic.Length && file.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    private static byte[] WithMagic(byte[] blob)
    {
        byte[] outp = new byte[Magic.Length + blob.Length];
        Magic.CopyTo(outp, 0);
        blob.CopyTo(outp, Magic.Length);
        return outp;
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _log.LogWarning(ex, "删除失败: {Path}", path); }
    }
}
