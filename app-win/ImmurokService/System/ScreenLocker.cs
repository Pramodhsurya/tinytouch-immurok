using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Platform;

/// <summary>
/// 锁定交互会话的屏幕（响应设备的 0x23 长按锁屏请求）。
///
/// <para>⚠️ Service 以 LocalSystem 跑在 Session 0，<c>LockWorkStation()</c> 只作用于**调用方所在会话**，
/// 直接调用锁不了用户的桌面。标准做法是拿到活动控制台会话的用户令牌
/// （<c>WTSQueryUserToken</c>），用 <c>CreateProcessAsUser</c> 在那个会话里起一个
/// <c>rundll32.exe user32.dll,LockWorkStation</c> 来完成锁屏。</para>
///
/// <para>这与解锁走 CP 管道是对称的：解锁必须由 LogonUI 里的 Credential Provider 提交凭据，
/// 锁屏必须由用户会话里的进程发起，两个方向都跨不过会话隔离。</para>
/// </summary>
public sealed class ScreenLocker
{
    private readonly ILogger<ScreenLocker> _log;
    public ScreenLocker(ILogger<ScreenLocker> log) => _log = log;

    public bool Lock()
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            _log.LogWarning("锁屏失败：无活动控制台会话");
            return false;
        }

        // 开发进程模式（tools\restart-all.ps1 默认路径）下，Service 是以普通进程跑在
        // 用户自己的交互会话里，不是 Session 0 的 LocalSystem 服务。这种情况直接调
        // LockWorkStation 就能锁；反倒是 WTSQueryUserToken 会因为普通用户没有
        // SeTcbPrivilege 而失败（错误 1314 ERROR_PRIVILEGE_NOT_HELD）。
        // 所以先按会话号判断走哪条路，而不是无条件走跨会话。
        uint ourSession = (uint)Process.GetCurrentProcess().SessionId;
        if (ourSession == sessionId)
        {
            if (LockWorkStation())
            {
                _log.LogInformation("已锁屏（本进程即在交互会话 {Session}，直接 LockWorkStation）", sessionId);
                return true;
            }
            _log.LogWarning("LockWorkStation 失败（错误 {Err}），改走跨会话路径", Marshal.GetLastWin32Error());
        }

        IntPtr userToken = IntPtr.Zero, dupToken = IntPtr.Zero, env = IntPtr.Zero;
        try
        {
            if (!WTSQueryUserToken(sessionId, out userToken))
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_PRIVILEGE_NOT_HELD)
                {
                    _log.LogWarning(
                        "锁屏失败：WTSQueryUserToken 需要 SeTcbPrivilege（错误 1314）。" +
                        "本进程会话 {Our}、目标会话 {Target}：以普通用户身份跨会话锁屏是不允许的，" +
                        "需以 LocalSystem 注册为服务运行（install.ps1）。", ourSession, sessionId);
                }
                else
                {
                    _log.LogWarning("锁屏失败：WTSQueryUserToken 失败（错误 {Err}）", err);
                }
                return false;
            }

            // CreateProcessAsUser 需要 primary token，WTSQueryUserToken 给的可直接复制为 primary。
            if (!DuplicateTokenEx(userToken, MAXIMUM_ALLOWED, IntPtr.Zero,
                    SecurityImpersonation, TokenPrimary, out dupToken))
            {
                _log.LogWarning("锁屏失败：DuplicateTokenEx 失败（错误 {Err}）", Marshal.GetLastWin32Error());
                return false;
            }

            // 环境块拿不到也能继续，只是进程环境不完整。
            if (!CreateEnvironmentBlock(out env, dupToken, false))
                env = IntPtr.Zero;

            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"winsta0\default", // 必须指定交互桌面，否则进程没有可见桌面
            };

            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string cmd = $"\"{Path.Combine(sysDir, "rundll32.exe")}\" user32.dll,LockWorkStation";

            bool ok = CreateProcessAsUser(
                dupToken, null, cmd,
                IntPtr.Zero, IntPtr.Zero, false,
                CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
                env, null, ref si, out PROCESS_INFORMATION pi);

            if (!ok)
            {
                _log.LogWarning("锁屏失败：CreateProcessAsUser 失败（错误 {Err}）", Marshal.GetLastWin32Error());
                return false;
            }

            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            _log.LogInformation("已在会话 {Session} 触发锁屏", sessionId);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "锁屏异常");
            return false;
        }
        finally
        {
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            if (dupToken != IntPtr.Zero) CloseHandle(dupToken);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
        }
    }

    // ---- P/Invoke ----
    private const int ERROR_PRIVILEGE_NOT_HELD = 1314;
    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    /// <summary>只对**调用方所在会话**生效——从 Session 0 调用锁不了用户桌面。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existingToken, uint desiredAccess,
        IntPtr tokenAttributes, int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr env);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr token, string? appName, string? cmdLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles,
        uint creationFlags, IntPtr environment, string? currentDirectory,
        ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
