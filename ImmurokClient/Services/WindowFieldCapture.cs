using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace ImmurokClient.Services;

/// <summary>
/// 一次捕获的目标：目标密码框的模板 + 其宿主应用身份（exe / 发行人 / 签名指纹）。
/// </summary>
public sealed record CapturedTarget(
    AppIdentity App,         // 应用身份（包族名 / 签名 / 路径，见 AppIdentity）
    string FieldAutomationId,
    string FieldName,
    string FieldControlType,
    string FieldClassName,
    string WindowTitle,
    bool IsPassword);

/// <summary>
/// 从屏幕坐标用 UI Automation 抽取目标密码框及其宿主应用信息。
///
/// 只做「定位 + 取模板 + 取签名信息」。真正的注入（写值/敲键、指纹门）在下一步的注入引擎里；
/// 那时会用 WinVerifyTrust 对目标进程做真正的签名信任校验，这里只抽取展示/存储用的证书信息。
/// </summary>
public static class WindowFieldCapture
{
    /// <summary>从物理屏幕坐标定位元素并抽取目标信息；失败返回 null。</summary>
    public static CapturedTarget? FromScreenPoint(double x, double y)
    {
        AutomationElement? el;
        try { el = AutomationElement.FromPoint(new System.Windows.Point(x, y)); }
        catch { return null; }
        if (el is null) return null;

        int pid;
        string autoId, name, ctrl, cls;
        bool isPass;
        try
        {
            var info = el.Current;
            pid = info.ProcessId;
            autoId = info.AutomationId ?? "";
            name = info.Name ?? "";
            ctrl = info.ControlType?.ProgrammaticName ?? "";
            cls = info.ClassName ?? "";
            isPass = info.IsPassword;
        }
        catch { return null; }

        // 落在了本应用自己（例如准星窗口）上，视为未命中，让用户重试。
        if (pid == Environment.ProcessId) return null;

        string windowTitle = TryGetWindowTitle(el);
        // UIA 给的 pid 已经是元素真正的宿主进程（传统 UWP 也不会是 ApplicationFrameHost），
        // 所以这里直接用它取身份，不必再做窗口下钻。
        AppIdentity app = AppIdentity.FromProcess(pid);

        return new CapturedTarget(app, autoId, name, ctrl, cls, windowTitle, isPass);
    }

    /// <summary>轻量采样光标下的元素（不算签名），供定位时的高亮框 + 信息卡实时显示。rect 为元素物理边界矩形。</summary>
    public static (string exe, string ctrl, bool isPassword, string window, System.Windows.Rect rect) SampleLight(double x, double y)
    {
        try
        {
            var el = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (el is null) return ("", "", false, "", System.Windows.Rect.Empty);
            var c = el.Current;
            string ctrl = c.ControlType?.ProgrammaticName ?? "";
            if (ctrl.StartsWith("ControlType.", StringComparison.Ordinal))
                ctrl = ctrl.Substring("ControlType.".Length);
            int pid = c.ProcessId;
            string exe = pid == Environment.ProcessId
                ? "(本工具)"
                : System.IO.Path.GetFileName(TryGetProcessPath(pid));
            System.Windows.Rect rect;
            try { rect = c.BoundingRectangle; } catch { rect = System.Windows.Rect.Empty; }
            return (exe, ctrl, c.IsPassword, TryGetWindowTitle(el), rect);
        }
        catch { return ("", "", false, "", System.Windows.Rect.Empty); }
    }

    /// <summary>沿 UIA 控件树向上找到宿主窗口，取其标题。</summary>
    private static string TryGetWindowTitle(AutomationElement el)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            AutomationElement? cur = el;
            while (cur is not null)
            {
                if (cur.Current.ControlType == ControlType.Window)
                    return cur.Current.Name ?? "";
                cur = walker.GetParent(cur);
            }
        }
        catch { /* 树遍历失败就留空 */ }
        return "";
    }

    // ---- 取目标进程 exe 路径（跨进程，同一用户即可） ----
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, StringBuilder buf, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr h);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private static string TryGetProcessPath(int pid)
    {
        if (pid <= 0) return "";
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            uint sz = (uint)sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref sz) ? sb.ToString() : "";
        }
        finally { CloseHandle(h); }
    }

}
