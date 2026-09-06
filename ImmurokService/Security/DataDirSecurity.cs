using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Security;

/// <summary>
/// <c>%ProgramData%\immurok</c> 的目录 ACL（设计稿 §3.4）。ProgramData 默认继承给 Users 读 + 建文件，
/// 所以 pairing.dat、settings.json 原来任意用户可读、任何人都能往里放文件。这里断开继承，只留
/// SYSTEM / Administrators 完全控制；<c>logs</c> 子目录另给 owner 读——不是 Users：多用户机器上
/// 日志会公开每一次认证事件与 agent 命令文本（Linux 端 0640 是同一个理由）。
/// 安装包（Inno <c>[Dirs]</c>）也设一遍，服务启动与 owner 变更时再对账一次，谁先到都行。
/// </summary>
public sealed class DataDirSecurity
{
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier[] LooseSids =
    {
        new(WellKnownSidType.BuiltinUsersSid, null),
        new(WellKnownSidType.WorldSid, null),
        new(WellKnownSidType.AuthenticatedUserSid, null),
        new(WellKnownSidType.InteractiveSid, null),
    };

    private readonly ILogger<DataDirSecurity> _log;
    private readonly OwnerStore _owner;

    public string Dir { get; }
    public string LogsDir { get; }

    public DataDirSecurity(ILogger<DataDirSecurity> log, OwnerStore owner)
    {
        _log = log;
        _owner = owner;
        Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "immurok");
        LogsDir = Path.Combine(Dir, "logs");
        // 配对 / PASS:SET 换了 owner，logs 的读权限跟着换。
        _owner.Changed += () => Apply();
    }

    /// <summary>目录 DACL 里是否还有 Users / Everyone / Authenticated Users / Interactive 的允许项（<c>data_acl: loose</c>）。</summary>
    public bool IsLoose()
    {
        try
        {
            var rules = new DirectoryInfo(Dir).GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule r in rules)
            {
                if (r.AccessControlType != AccessControlType.Allow) continue;
                foreach (var sid in LooseSids)
                    if (sid.Equals(r.IdentityReference)) return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "读取数据目录 ACL 失败: {Dir}", Dir);
            return true;
        }
    }

    /// <summary>收紧 ACL。非 SYSTEM / 管理员身份（控制台调试）时只告警不动。</summary>
    public void Apply()
    {
        try
        {
            var me = WindowsIdentity.GetCurrent();
            bool privileged = me.User?.Equals(LocalSystem) == true
                              || new WindowsPrincipal(me).IsInRole(WindowsBuiltInRole.Administrator);
            if (!privileged)
            {
                _log.LogWarning("非 SYSTEM / 管理员身份运行，不改数据目录 ACL: {Dir}", Dir);
                return;
            }

            Directory.CreateDirectory(LogsDir);

            var flags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

            var dir = new DirectorySecurity();
            dir.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            dir.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, flags, PropagationFlags.None, AccessControlType.Allow));
            dir.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, flags, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(Dir).SetAccessControl(dir);

            var logs = new DirectorySecurity();
            logs.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            logs.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, flags, PropagationFlags.None, AccessControlType.Allow));
            logs.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, flags, PropagationFlags.None, AccessControlType.Allow));
            var ownerSid = _owner.Sid;
            if (ownerSid is not null)
                logs.AddAccessRule(new FileSystemAccessRule(ownerSid, FileSystemRights.ReadAndExecute, flags, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(LogsDir).SetAccessControl(logs);

            _log.LogInformation("数据目录 ACL 已收紧（SYSTEM / Administrators；logs 另给 owner 读）: {Dir}", Dir);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "收紧数据目录 ACL 失败: {Dir}", Dir);
        }
    }
}
