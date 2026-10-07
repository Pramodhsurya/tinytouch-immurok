using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ImmurokService.Ipc;

/// <summary>
/// Service 各命名管道共用的 ACL（设计稿 §3.2）。
/// 只有 SYSTEM（与本进程用户，控制台调试模式下不是 SYSTEM）拿 FullControl；给客户端的那个 SID 只拿
/// ReadWrite，**不含 CreateNewInstance**——否则任意用户进程能建同名管道的额外实例，客户端连到哪个
/// 实例由系统分配，PASS:SET 里的明文密码就会落到攻击者手里。后续实例由 Service 自己创建，用的是
/// 创建者的权限，用户不需要那一位。
/// </summary>
public static class PipeAcl
{
    public static PipeSecurity Build(SecurityIdentifier? clientReadWrite)
    {
        var security = new PipeSecurity();
        if (clientReadWrite is not null)
        {
            security.AddAccessRule(new PipeAccessRule(clientReadWrite,
                PipeAccessRights.ReadWrite, AccessControlType.Allow));
        }

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));

        // 控制台调试模式（F5，以开发者身份跑）：创建者不是 SYSTEM，没有这一条后续实例会建不出来。
        var self = WindowsIdentity.GetCurrent().User;
        if (self is not null && !self.Equals(system))
            security.AddAccessRule(new PipeAccessRule(self, PipeAccessRights.FullControl, AccessControlType.Allow));

        return security;
    }
}
