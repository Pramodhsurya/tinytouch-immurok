using System.Runtime.InteropServices;
using System.Text;
using ImmurokCommon.Protocol;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Platform;

/// <summary>
/// 向 Credential Provider 管道写入用户名/密码以触发解锁。
/// 连接 <c>\\.\pipe\ImmurokCredentialProvider</c>，依次 WriteFile 写 UTF-16
/// <c>username\0</c>、<c>password\0</c>；CP 端 ReadFile 读到后提交凭据。
/// </summary>
public sealed class ScreenUnlocker
{
    private readonly ILogger<ScreenUnlocker> _log;
    public ScreenUnlocker(ILogger<ScreenUnlocker> log) => _log = log;

    public bool Unlock(string username, string password)
    {
        IntPtr pipe = IntPtr.Zero;
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

            byte[] u = Encoding.Unicode.GetBytes(username + '\0');
            byte[] p = Encoding.Unicode.GetBytes(password + '\0');

            if (!WriteFile(pipe, u, (uint)u.Length, out _, IntPtr.Zero))
            {
                _log.LogError("写用户名失败: {Err}", Marshal.GetLastWin32Error());
                return false;
            }
            if (!WriteFile(pipe, p, (uint)p.Length, out _, IntPtr.Zero))
            {
                _log.LogError("写密码失败: {Err}", Marshal.GetLastWin32Error());
                return false;
            }
            _log.LogInformation("已向 CP 推送解锁凭据");
            return true;
        }
        finally
        {
            if (pipe != IntPtr.Zero && pipe != new IntPtr(-1))
                CloseHandle(pipe);
        }
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
    private static extern bool CloseHandle(IntPtr obj);
}
