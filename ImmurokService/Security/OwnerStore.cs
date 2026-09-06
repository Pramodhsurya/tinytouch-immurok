using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Security;

/// <summary>
/// 设备主人（owner）的持久化：<c>%ProgramData%\immurok\owner</c>，两行——SID 与账户名（DOMAIN\user）。
/// 对应 Linux 的 <c>/var/lib/immurok/owner</c>。
///
/// Service 是全机一个、会话有多个，凭据库里只有一份登录密码。不记 owner 的话，同机第二个账号
/// 锁屏后触摸传感器，Service 会拿 owner 的凭据去解他的会话；AUTH / APPROVE 也会把 owner 的
/// 一次触摸授权给别人的请求。owner 在配对成功和 PASS:SET 时由调用方 token 记录，PAIR:RESET 清除。
/// </summary>
public sealed class OwnerStore
{
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private readonly ILogger<OwnerStore> _log;
    private readonly string _path;
    private readonly object _lock = new();
    private SecurityIdentifier? _sid;
    private string? _account;
    private bool _loaded;

    /// <summary>owner 记录 / 清除之后触发（DataDirSecurity 据此换 logs 的读权限）。</summary>
    public event Action? Changed;

    public OwnerStore(ILogger<OwnerStore> log)
    {
        _log = log;
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "immurok");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "owner");
    }

    public bool IsSet => Sid is not null;

    public SecurityIdentifier? Sid
    {
        get { lock (_lock) { EnsureLoaded(); return _sid; } }
    }

    /// <summary>owner 的账户名（DOMAIN\user），供 PASS:SET 未带用户名、或凭据里没存用户名时回退。</summary>
    public string? Account
    {
        get { lock (_lock) { EnsureLoaded(); return _account; } }
    }

    /// <summary>调用方是否为 owner。没有 owner 记录时返回 null（调用方按「告警放行」处理）。</summary>
    public bool? IsOwner(SecurityIdentifier sid)
    {
        var owner = Sid;
        return owner is null ? null : owner.Equals(sid);
    }

    public void Save(SecurityIdentifier sid, string account)
    {
        lock (_lock)
        {
            try
            {
                // 目录 ACL 在 §3.4 收紧之前对 Users 可写，文件必须自带紧 ACL，且先删后建——
                // 直接覆盖会保留一个别人预先创建的文件的属主。
                if (File.Exists(_path)) File.Delete(_path);

                var fs = new FileSecurity();
                fs.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                fs.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, AccessControlType.Allow));
                fs.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, AccessControlType.Allow));

                using (var stream = new FileInfo(_path).Create(FileMode.CreateNew, FileSystemRights.Write,
                           FileShare.None, 4096, FileOptions.None, fs))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(sid.Value);
                    writer.Write('\n');
                    writer.Write(account);
                    writer.Write('\n');
                }

                _sid = sid;
                _account = account;
                _loaded = true;
                _log.LogInformation("owner 已记录");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "写 owner 文件失败: {Path}", _path);
            }
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            try { if (File.Exists(_path)) File.Delete(_path); }
            catch (Exception ex) { _log.LogWarning(ex, "删 owner 文件失败: {Path}", _path); }
            _sid = null;
            _account = null;
            _loaded = true;
            _log.LogInformation("owner 已清除");
        }
        Changed?.Invoke();
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(_path)) return;

            // 文件属主必须是 SYSTEM / Administrators / 本进程用户（控制台调试）。
            // 目录对 Users 可写时，任何人都能抢先放一个文件把自己写成 owner。
            var owner = new FileInfo(_path).GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            var self = WindowsIdentity.GetCurrent().User;
            if (owner is null || !(owner.Equals(LocalSystem) || owner.Equals(Administrators) || (self is not null && owner.Equals(self))))
            {
                _log.LogWarning("owner 文件属主可疑（{Owner}），忽略该文件: {Path}", owner?.Value ?? "?", _path);
                return;
            }

            string[] lines = File.ReadAllLines(_path);
            if (lines.Length < 1 || string.IsNullOrWhiteSpace(lines[0])) return;
            _sid = new SecurityIdentifier(lines[0].Trim());
            _account = lines.Length >= 2 && !string.IsNullOrWhiteSpace(lines[1]) ? lines[1].Trim() : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "读 owner 文件失败，按无 owner 处理: {Path}", _path);
            _sid = null;
            _account = null;
        }
    }
}
