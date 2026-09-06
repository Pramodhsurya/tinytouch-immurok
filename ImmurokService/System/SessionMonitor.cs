using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Platform;

/// <summary>
/// 锁屏状态检测与活动会话用户名获取。
/// Service 运行在 Session 0，收不到交互会话的 WM_WTSSESSION_CHANGE（除非用隐藏窗 + WTSRegisterSessionNotification），
/// 首版采用轮询判断（<see cref="IsLocked"/>），由 Worker 定时调用。
/// </summary>
public sealed class SessionMonitor
{
    private readonly ILogger<SessionMonitor> _log;
    public SessionMonitor(ILogger<SessionMonitor> log) => _log = log;

    /// <summary>
    /// 判断活动控制台会话是否处于锁屏。
    /// 通过 WTSQuerySessionInformation 查 WTSSessionInfoEx 的 SessionFlags：
    /// 0 = Locked, 1 = Unlocked（注意 Win7 早期语义相反，Win8+ 已修正）。
    /// </summary>
    public bool IsLocked()
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF) return false; // 无活动控制台会话

        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, WTS_INFO_CLASS.WTSSessionInfoEx,
                out IntPtr buffer, out uint bytes) || buffer == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            // WTSINFOEX 布局：DWORD Level @0；union（8 字节对齐，因含 LARGE_INTEGER）@8。
            // union.Level1: SessionId @0, SessionState @4, SessionFlags @8 → 绝对偏移 16。
            // 用固定偏移读取，避免结构体 marshal 的对齐坑。
            if (bytes < 20) return false;
            int level = Marshal.ReadInt32(buffer, 0);
            if (level != 1) return false;
            int sessionFlags = Marshal.ReadInt32(buffer, 16);
            // SessionFlags: WTS_SESSIONSTATE_LOCK = 0, WTS_SESSIONSTATE_UNLOCK = 1
            return sessionFlags == WTS_SESSIONSTATE_LOCK;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    /// <summary>取活动控制台会话的登录用户名（DOMAIN\\user 或 user）。</summary>
    public string? GetActiveConsoleUser()
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF) return null;

        string? user = QueryString(sessionId, WTS_INFO_CLASS.WTSUserName);
        if (string.IsNullOrEmpty(user)) return null;
        string? domain = QueryString(sessionId, WTS_INFO_CLASS.WTSDomainName);
        return string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}";
    }

    /// <summary>
    /// 取活动控制台会话登录用户的 SID。优先 <c>WTSQueryUserToken</c>（需要 SYSTEM，拿到的是会话
    /// 真实 token 的 SID，不经名字解析）；控制台调试模式下没有该权限，退回按用户名解析。
    /// </summary>
    public SecurityIdentifier? GetActiveConsoleUserSid()
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF) return null;

        if (WTSQueryUserToken(sessionId, out IntPtr token) && token != IntPtr.Zero)
        {
            try
            {
                using var identity = new WindowsIdentity(token);
                return identity.User;
            }
            catch (Exception ex) { _log.LogDebug(ex, "解析会话 token 失败"); }
            finally { CloseHandle(token); }
        }

        string? account = GetActiveConsoleUser();
        if (string.IsNullOrEmpty(account)) return null;
        try { return (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier)); }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "账户名解析 SID 失败");
            return null;
        }
    }

    private string? QueryString(uint sessionId, WTS_INFO_CLASS cls)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, cls, out IntPtr buffer, out _) || buffer == IntPtr.Zero)
            return null;
        try { return Marshal.PtrToStringUni(buffer); }
        finally { WTSFreeMemory(buffer); }
    }

    // ---- P/Invoke ----
    private const int WTS_SESSIONSTATE_LOCK = 0;

    private enum WTS_INFO_CLASS
    {
        WTSUserName = 5,
        WTSDomainName = 7,
        WTSSessionInfoEx = 25,
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId,
        WTS_INFO_CLASS infoClass, out IntPtr buffer, out uint bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
