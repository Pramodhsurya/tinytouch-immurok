using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ImmurokService.Ipc;

/// <summary>管道对端进程的身份：pid、token 用户 SID、账户名（DOMAIN\user）、会话 id、映像路径。</summary>
public sealed record CallerIdentity(uint Pid, SecurityIdentifier Sid, string Account, uint SessionId, string? ImagePath)
{
    // 日志里只放 pid 与会话：账户名属于个人信息，事件日志普通用户可读。
    public override string ToString() => $"pid={Pid} session={SessionId}";
}

/// <summary>
/// 取命名管道对端进程的身份。
/// 这是纵深防御不是边界：同用户、同完整性级别的进程可以注入合法客户端后再连，
/// 从 pid 看到的就是一个签名齐全的进程（设计稿 §3.3 / §9.1）。因此身份只用来**拒绝**，
/// 放行必须来自设备上的触摸。
/// </summary>
public static class PipeCaller
{
    /// <summary>
    /// 从已连接的服务端管道句柄取对端（客户端）进程身份。取不到（对端已退出、权限不足）返回 null，
    /// 调用方按「身份未知」处理。
    /// </summary>
    public static CallerIdentity? Identify(NamedPipeServerStream pipe)
    {
        try
        {
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid) || pid == 0) return null;
            CallerIdentity? id = IdentifyProcess(pid, out SafeProcessHandle? process);
            process?.Dispose();
            return id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 按 pid 取进程身份。<paramref name="process"/> 是以 <c>PROCESS_QUERY_LIMITED_INFORMATION</c>
    /// 打开的句柄，调用方需要防 pid 复用时把它持有到用完为止，否则直接 Dispose。
    /// </summary>
    public static CallerIdentity? IdentifyProcess(uint pid, out SafeProcessHandle? process)
    {
        process = null;
        try
        {
            SafeProcessHandle handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle.IsInvalid) return null;
            process = handle;

            if (!OpenProcessToken(handle, TOKEN_QUERY, out SafeAccessTokenHandle token)) return null;

            SecurityIdentifier? sid;
            string account;
            using (token)
            {
                // WindowsIdentity 会复制一份 token，这里的句柄用完即关。
                using var identity = new WindowsIdentity(token.DangerousGetHandle());
                sid = identity.User;
                if (sid is null) return null;
                try { account = identity.Name; }
                catch { account = sid.Value; } // 孤儿 SID 解析不出账户名
            }

            if (!ProcessIdToSessionId(pid, out uint session)) session = uint.MaxValue;

            string? image = null;
            var sb = new StringBuilder(1024);
            uint len = (uint)sb.Capacity;
            if (QueryFullProcessImageName(handle, 0, sb, ref len)) image = sb.ToString(0, (int)len);

            return new CallerIdentity(pid, sid, account, session, image);
        }
        catch
        {
            return null;
        }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint desiredAccess, out SafeAccessTokenHandle token);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder exeName, ref uint size);
}
