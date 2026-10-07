using System.Runtime.InteropServices;

// immurok 终端指纹提示助手
//
// 用法：ImmurokConsolePrompt.exe <clientPid>
// 附到 ssh 客户端进程的控制台，在终端显示"请按指纹"动画（对齐 macOS/Linux 体验）。
// 通过 stdin 接收状态：
//   FAIL <n>   按错指纹，剩余 n 次
//   SIGNING    指纹已过，正在签名
//   END        结束（擦除并退出）
// stdin 关闭或收到 END 即退出。父进程独立，不影响 Service 自己的控制台。

if (args.Length < 1 || !uint.TryParse(args[0], out uint pid))
    return 1;

// 必须先脱离自己的控制台，才能附到目标进程的控制台。
FreeConsole();
if (!AttachConsole(pid))
    return 2; // 目标无控制台 / 跨会话（Session 0）等，静默放弃

IntPtr conout = CreateFileW("CONOUT$", GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
if (conout == INVALID_HANDLE_VALUE)
{
    FreeConsole();
    return 3;
}
// 开启 VT，让 ANSI 转义生效。
if (GetConsoleMode(conout, out uint mode))
    SetConsoleMode(conout, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);

const string message = "Touch your immurok to authorize SSH signing... (Ctrl+C to cancel)";
string[] frames = { "|", "/", "-", "\\" }; // 纯 ASCII，避免老式控制台乱码

// 共享状态（stdin 线程更新，渲染线程读取）。
object gate = new();
bool done = false;
bool signing = false;
bool failedFinal = false;
int failRemaining = -1;
long failUntil = 0;

var render = new Thread(() =>
{
    int f = 0;
    while (true)
    {
        string line;
        lock (gate)
        {
            if (done) break;
            long now = Environment.TickCount64;
            if (failedFinal)
            {
                line = "\x1b[31m[X] Authentication failed\x1b[0m";
            }
            else if (failRemaining >= 0 && now < failUntil)
            {
                string s = failRemaining == 1 ? "" : "s";
                line = $"\x1b[31m[!] Fingerprint not matched, {failRemaining} attempt{s} left\x1b[0m";
            }
            else if (signing)
            {
                line = "\x1b[32m[*] Approved, signing...\x1b[0m";
            }
            else
            {
                line = $"\x1b[33m[{frames[f % frames.Length]}] {message}\x1b[0m";
            }
        }
        WriteConsole(conout, "\r\x1b[K" + line);
        f++;
        Thread.Sleep(90);
    }
});
render.IsBackground = true;
render.Start();

// 读 stdin 指令。
string? cmd;
while ((cmd = Console.In.ReadLine()) != null)
{
    cmd = cmd.Trim();
    if (cmd.StartsWith("FAIL", StringComparison.Ordinal))
    {
        string[] p = cmd.Split(' ');
        int rem = p.Length > 1 && int.TryParse(p[1], out int n) ? n : 0;
        lock (gate) { failRemaining = rem; failUntil = Environment.TickCount64 + 700; }
    }
    else if (cmd == "SIGNING")
    {
        lock (gate) { signing = true; }
    }
    else if (cmd == "FAILED")
    {
        lock (gate) { failedFinal = true; }
    }
    else if (cmd == "END")
    {
        break;
    }
}

lock (gate) { done = true; }
render.Join(300);
WriteConsole(conout, "\r\x1b[K"); // 擦除本行
FreeConsole();
return 0;

// ---- P/Invoke ----
static void WriteConsole(IntPtr h, string s)
{
    byte[] b = System.Text.Encoding.UTF8.GetBytes(s);
    WriteFile(h, b, (uint)b.Length, out _, IntPtr.Zero);
}

partial class Program
{
    const uint GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2;
    const uint OPEN_EXISTING = 3;
    const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint dwProcessId);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetConsoleMode(IntPtr h, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetConsoleMode(IntPtr h, uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool WriteFile(IntPtr h, byte[] buf, uint toWrite, out uint written, IntPtr overlapped);
}
