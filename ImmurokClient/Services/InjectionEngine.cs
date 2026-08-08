using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows.Automation;

namespace ImmurokClient.Services;

/// <summary>
/// 指纹触发时的注入引擎：识别前台应用 → 按注入项匹配 → 校验签名 → 定位密码框 → SetFocus + 模拟键入。
/// 运行在客户端（用户会话），可对前台应用做 UI Automation 与模拟输入（服务端在 Session 0 做不到）。
/// </summary>
public static class InjectionEngine
{
    /// <summary>对当前前台窗口尝试一次注入。返回一句状态用于日志/诊断。</summary>
    public static string TryInjectForeground()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return "no-foreground";

        GetWindowThreadProcessId(hwnd, out int pid);
        if (pid <= 0) return "no-pid";
        if (pid == Environment.ProcessId) return "self"; // 不注入到自己

        string exe = TryGetProcessPath(pid);
        if (string.IsNullOrEmpty(exe)) return "no-exe";

        // 按 exe 路径匹配注入项
        var item = PasswordInjectionStore.Items.FirstOrDefault(
            x => !string.IsNullOrEmpty(x.AppId) &&
                 string.Equals(x.AppId, exe, StringComparison.OrdinalIgnoreCase));
        if (item is null) return "no-match";

        // 安全闸：注入项存了签名指纹时，前台 exe 的指纹必须一致，防止仿冒进程冒领密码。
        if (!string.IsNullOrEmpty(item.Signature))
        {
            string thumb = TryGetThumbprint(exe);
            if (!string.Equals(thumb, item.Signature, StringComparison.OrdinalIgnoreCase))
                return "sig-mismatch";
        }

        string? pw = PasswordInjectionStore.GetPassword(item);
        if (string.IsNullOrEmpty(pw)) return "no-password";

        var (field, _) = FindPasswordField(hwnd, pid, item);
        if (field is null) return "no-field";

        // 取字段位置/可见性，供回退方案（点击密码框中心再键入）使用。
        bool foff = true;
        System.Windows.Rect rect = System.Windows.Rect.Empty;
        try
        {
            var c = field.Current;
            foff = c.IsOffscreen;
            rect = c.BoundingRectangle;
        }
        catch { }

        // 方式一：UIA ValuePattern 直接设值（等价 macOS 的 AXValue 写入）。
        // 1Password 等安全应用会拦截合成按键（SendInput），直接设值可绕过。
        bool valueSet = false;
        try
        {
            if (field.TryGetCurrentPattern(ValuePattern.Pattern, out object vpObj) && vpObj is ValuePattern vp
                && !vp.Current.IsReadOnly)
            {
                try { field.SetFocus(); } catch { }
                vp.SetValue(pw);
                valueSet = true;
            }
        }
        catch { /* 设值失败则走下面的回退方案 */ }

        // 方式二（回退）：真实鼠标点击密码框中心把焦点/光标压进去，再逐字键入。
        if (!valueSet)
        {
            if (!foff && rect.Width > 1 && rect.Height > 1)
            {
                int cx = (int)(rect.Left + rect.Width / 2);
                int cy = (int)(rect.Top + rect.Height / 2);
                SetCursorPos(cx, cy);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
            }
            else
            {
                try { field.SetFocus(); } catch { }
            }

            System.Threading.Thread.Sleep(100); // 给焦点/光标切换留点时间
            TypeUnicode(pw);
        }

        // 填好后自动提交：找到「解锁/确定」这类主按钮并触发；找不到明确按钮就退回敲一次 Enter。
        System.Threading.Thread.Sleep(150);
        TrySubmit(hwnd);

        return valueSet ? "ok-value" : "ok-typed";
    }

    private static readonly string[] CancelWords =
        { "cancel", "取消", "关闭", "close", "dismiss", "返回", "back" };
    private static readonly string[] ConfirmWords =
        { "unlock", "解锁", "sign in", "signin", "登录", "登入", "log in", "login",
          "continue", "继续", "confirm", "确定", "确认", "submit", "enter", "go" };

    /// <summary>填好密码后尝试提交：优先触发主按钮（InvokePattern/点击），否则退回 Enter。</summary>
    private static string TrySubmit(IntPtr hwnd)
    {
        AutomationElement? root;
        try { root = AutomationElement.FromHandle(hwnd); }
        catch { return "no-root"; }
        if (root is null) return "no-root";

        AutomationElement? btn = PickPrimaryButton(root);
        if (btn is null)
        {
            PressEnter(); // 找不到明确的主按钮，退回敲一次回车（被拦截则无效，但无副作用）
            return "enter";
        }

        string nm = SafeName(btn);
        // 优先 InvokePattern（等价 AX press；1Password 接受程序化设值，通常也接受 Invoke）
        try
        {
            if (btn.TryGetCurrentPattern(InvokePattern.Pattern, out object ipObj) && ipObj is InvokePattern inv)
            {
                inv.Invoke();
                return $"invoke:{nm}";
            }
        }
        catch { /* 落到点击 */ }

        // 回退：合成点击按钮中心
        try
        {
            var c = btn.Current;
            var r = c.BoundingRectangle;
            if (!c.IsOffscreen && r.Width > 1 && r.Height > 1)
            {
                SetCursorPos((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2));
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
                return $"click:{nm}";
            }
        }
        catch { }

        PressEnter();
        return "enter-fallback";
    }

    /// <summary>挑主按钮：优先含「解锁/确定」等确认词的；否则窗口内唯一可用按钮；再否则放弃（返回 null）。</summary>
    private static AutomationElement? PickPrimaryButton(AutomationElement root)
    {
        try
        {
            var btns = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            var cands = new List<AutomationElement>();
            AutomationElement? confirmHit = null;
            foreach (AutomationElement b in btns)
            {
                try
                {
                    var c = b.Current;
                    if (c.IsOffscreen || !c.IsEnabled) continue;
                    var r = c.BoundingRectangle;
                    if (r.Width < 1 || r.Height < 1) continue;
                    string nm = (c.Name ?? "").ToLowerInvariant();
                    if (CancelWords.Any(w => nm.Contains(w))) continue; // 排除取消/关闭类
                    cands.Add(b);
                    if (confirmHit is null && ConfirmWords.Any(w => nm.Contains(w))) confirmHit = b;
                }
                catch { }
            }
            if (confirmHit is not null) return confirmHit;
            if (cands.Count == 1) return cands[0]; // 只有一个可用按钮，无歧义
            return null; // 多个且无确认词：不乱点，退回 Enter
        }
        catch { return null; }
    }

    private static string SafeName(AutomationElement e)
    {
        try { return e.Current.Name ?? ""; }
        catch { return ""; }
    }

    private static void PressEnter()
    {
        var down = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = 0x0D, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero } } };
        var up = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = 0x0D, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero } } };
        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>在前台窗口内定位要注入的密码框：焦点密码框 → 模板 AutomationId → 唯一密码框。返回命中路径便于诊断。</summary>
    private static (AutomationElement? field, string how) FindPasswordField(IntPtr hwnd, int pid, PasswordInjectionItem item)
    {
        // 1) 系统焦点若正好是本进程的密码框，直接用
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is not null && focused.Current.ProcessId == pid && focused.Current.IsPassword)
                return (focused, "focused");
        }
        catch { /* 取焦点失败就往下走 */ }

        AutomationElement? root;
        try { root = AutomationElement.FromHandle(hwnd); }
        catch { return (null, "none"); }
        if (root is null) return (null, "none");

        // 2) 模板里存了 AutomationId 就按它找（且必须是密码框）
        if (!string.IsNullOrEmpty(item.FieldAutomationId))
        {
            try
            {
                var byId = root.FindFirst(TreeScope.Descendants,
                    new AndCondition(
                        new PropertyCondition(AutomationElement.AutomationIdProperty, item.FieldAutomationId),
                        new PropertyCondition(AutomationElement.IsPasswordProperty, true)));
                if (byId is not null) return (byId, "template");
            }
            catch { /* 忽略，继续兜底 */ }
        }

        // 3) 兜底：窗口子树里「唯一的密码框」。多于一个（如改密码界面）则放弃，避免注错框。
        try
        {
            var all = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.IsPasswordProperty, true));
            if (all is not null && all.Count == 1) return (all[0], "single");
        }
        catch { /* 忽略 */ }

        return (null, "none");
    }

    // ---- 模拟键入（KEYEVENTF_UNICODE，逐 UTF-16 码元发送） ----
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private static void TypeUnicode(string s)
    {
        foreach (char c in s)
        {
            var inputs = new[] { MakeKey(c, false), MakeKey(c, true) };
            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
            System.Threading.Thread.Sleep(6); // 部分应用会丢弃过快的合成输入
        }
    }

    // ---- 鼠标点击（把光标/键盘焦点压进目标字段） ----
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);

    private static INPUT MakeKey(char c, bool keyUp) => new INPUT
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            }
        }
    };

    // ---- Win32：前台窗口 / 进程路径 ----
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);

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

    private static string TryGetThumbprint(string exePath)
    {
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(exePath));
            return cert.Thumbprint ?? "";
        }
        catch { return ""; }
    }
}