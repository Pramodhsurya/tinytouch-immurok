using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Ssh;

/// <summary>
/// 终端指纹提示：拉起 ImmurokConsolePrompt.exe 助手进程，让它附到 ssh 客户端的控制台显示
/// "请按指纹"动画（对齐 macOS/Linux 体验）。用独立进程避免动到 Service 自己的控制台。
/// 找不到助手 / 无法附控制台（Session 0 等）时静默降级。
///
/// 收尾用 <see cref="Finish"/>：关闭助手 stdin → 助手读到 EOF 后擦除本行并退出 → 本方法等它退出，
/// 从而保证"擦除"发生在调用方向 ssh 回包之前，不会残留提示文字。
/// </summary>
public sealed class ConsolePrompt : IDisposable
{
    private readonly Process _proc;
    private bool _finished;
    private ConsolePrompt(Process p) => _proc = p;

    public static ConsolePrompt? TryStart(uint clientPid, ILogger log)
    {
        if (clientPid == 0) return null;
        string exe = Path.Combine(AppContext.BaseDirectory, "ImmurokConsolePrompt.exe");
        if (!File.Exists(exe)) { log.LogDebug("终端提示助手缺失: {Exe}", exe); return null; }
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = clientPid.ToString(),
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            var p = Process.Start(psi);
            return p is null ? null : new ConsolePrompt(p);
        }
        catch (Exception ex) { log.LogDebug(ex, "启动终端提示失败"); return null; }
    }

    public void Fail(int remaining) => Send($"FAIL {remaining}");
    public void Signing() => Send("SIGNING");

    private void Send(string s)
    {
        if (_finished) return;
        try { _proc.StandardInput.WriteLine(s); _proc.StandardInput.Flush(); }
        catch { /* 助手已退出 */ }
    }

    /// <summary>关闭 stdin 让助手擦除并退出，并等待其退出（擦除完成）。</summary>
    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        try { _proc.StandardInput.Close(); } catch { /* ignore */ }
        try { if (!_proc.WaitForExit(700)) _proc.Kill(); } catch { /* ignore */ }
        try { _proc.Dispose(); } catch { /* ignore */ }
    }

    public void Dispose() => Finish();
}
