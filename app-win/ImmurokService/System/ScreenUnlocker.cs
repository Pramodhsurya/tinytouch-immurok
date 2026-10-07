using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using ImmurokCommon.Protocol;
using ImmurokService.Ipc;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace ImmurokService.Platform;

/// <summary>
/// 向 Credential Provider 管道写入用户名/密码以触发解锁。
/// 连接 <c>\\.\pipe\ImmurokCredentialProvider</c>，一次 WriteFile 写 UTF-16 的
/// <c>username\0password\0</c>；CP 端一次 ReadFile 读走整块后提交凭据。
///
/// 写之前必须校验管道服务端是谁（设计稿 §3.1）：管道名是全局命名空间、先到先得，CP 只在
/// 锁屏时才创建实例，用户登录后到锁屏前这段时间任何普通用户进程都能抢先建同名管道，
/// 等锁屏触摸时收下登录密码。校验依据是服务端进程的 token 必须是 LocalSystem——普通用户
/// 进程做不到这一点；映像路径 == LogonUI.exe 与会话 id == 当前控制台会话作为第二、三道。
/// 校验失败：不写、记日志、置 <see cref="LastPeerCheck"/> 供状态页展示，本次解锁放弃
/// （不重试其他实例——连到哪个实例由系统分配，重试只会拖长锁屏等待）。
/// </summary>
public sealed class ScreenUnlocker
{
    public enum PeerCheck { Unknown, Ok, Squatted }

    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);

    private readonly ILogger<ScreenUnlocker> _log;
    public ScreenUnlocker(ILogger<ScreenUnlocker> log) => _log = log;

    /// <summary>最近一次对端校验结果（Unknown = 还没触发过解锁）。</summary>
    public PeerCheck LastPeerCheck { get; private set; } = PeerCheck.Unknown;

    /// <summary>最近一次校验失败的原因（pid / 映像路径 / 会话），不含凭据与用户名。</summary>
    public string? LastPeerCheckDetail { get; private set; }

    public bool Unlock(string username, string password)
    {
        IntPtr pipe = IntPtr.Zero;
        SafeProcessHandle? peer = null;
        try
        {
            if (!WaitNamedPipe(PipeNames.CredentialProviderFull, 5000))
            {
                _log.LogWarning("CP 管道不可用（可能未处于锁屏/CP 未加载）");
                return false;
            }

            pipe = CreateFile(PipeNames.CredentialProviderFull, GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (pipe == IntPtr.Zero || pipe == new IntPtr(-1))
            {
                _log.LogError("打开 CP 管道失败: {Err}", Marshal.GetLastWin32Error());
                return false;
            }

            // 对端校验。peer 句柄持有到写完为止，防 pid 复用。
            if (!VerifyPeer(pipe, out peer, out string detail))
            {
                LastPeerCheck = PeerCheck.Squatted;
                LastPeerCheckDetail = detail;
                _log.LogWarning("CP 管道对端校验失败，不推送凭据（{Detail}）", detail);
                return false;
            }
            LastPeerCheck = PeerCheck.Ok;
            LastPeerCheckDetail = null;

            // 必须一次写完。CP 端是字节流管道（PIPE_TYPE_BYTE），只做一次 ReadFile 就
            // DisconnectNamedPipe；而它的 _StoreCredential 是按「username\0password\0」
            // 一整块来解析的。分两次写会撞上竞态：CP 那次读常常只拿到用户名，密码字段
            // 落在缓冲区已清零的部分 → 变成空密码，认证失败（而服务这边两次写都成功，
            // 日志照报推送成功，于是表现为「服务说成了、屏幕没解开」）；写第二帧时若 CP
            // 已经断开，还会得到 233 ERROR_PIPE_NOT_CONNECTED。
            byte[] payload = Encoding.Unicode.GetBytes(username + '\0' + password + '\0');

            if (!WriteFile(pipe, payload, (uint)payload.Length, out _, IntPtr.Zero))
            {
                _log.LogError("写解锁凭据失败: {Err}", Marshal.GetLastWin32Error());
                return false;
            }
            _log.LogInformation("已向 CP 推送解锁凭据");
            return true;
        }
        finally
        {
            if (pipe != IntPtr.Zero && pipe != new IntPtr(-1))
                CloseHandle(pipe);
            peer?.Dispose();
        }
    }

    /// <summary>
    /// 校验管道服务端进程：token 用户必须是 LocalSystem（唯一决定性的一条），映像路径必须是
    /// <c>%SystemRoot%\System32\LogonUI.exe</c>，会话必须是当前活动控制台会话。
    /// 任何一步取不到信息都按失败处理——身份只能用来拒绝。
    /// </summary>
    private bool VerifyPeer(IntPtr pipe, out SafeProcessHandle? peer, out string detail)
    {
        peer = null;
        if (!GetNamedPipeServerProcessId(pipe, out uint pid) || pid == 0)
        {
            detail = $"取不到服务端 pid: err={Marshal.GetLastWin32Error()}";
            return false;
        }

        CallerIdentity? id = PipeCaller.IdentifyProcess(pid, out peer);
        if (id is null)
        {
            detail = $"pid={pid} 取不到进程身份";
            return false;
        }

        string image = id.ImagePath ?? "?";
        if (!id.Sid.Equals(LocalSystem))
        {
            detail = $"pid={pid} image={image} session={id.SessionId} 服务端不是 SYSTEM";
            return false;
        }

        string expected = Path.Combine(Environment.SystemDirectory, "LogonUI.exe");
        if (id.ImagePath is null || !string.Equals(id.ImagePath, expected, StringComparison.OrdinalIgnoreCase))
        {
            detail = $"pid={pid} image={image} session={id.SessionId} 服务端不是 LogonUI";
            return false;
        }

        uint console = WTSGetActiveConsoleSessionId();
        uint serverSession = id.SessionId;
        if (GetNamedPipeServerSessionId(pipe, out uint pipeSession)) serverSession = pipeSession;
        if (console == 0xFFFFFFFF || serverSession != console)
        {
            detail = $"pid={pid} image={image} session={serverSession} 与控制台会话 {console} 不符";
            return false;
        }

        detail = "";
        return true;
    }

    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WaitNamedPipe(string name, uint timeout);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr file, byte[] buffer, uint toWrite, out uint written, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerSessionId(IntPtr pipe, out uint serverSessionId);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
