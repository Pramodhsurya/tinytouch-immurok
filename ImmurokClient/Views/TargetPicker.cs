using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ImmurokClient.Localization;
using ImmurokClient.Services;

namespace ImmurokClient.Views;

/// <summary>
/// 「准星」定位器（取词模式）：点「定位目标」→ 整个 client 隐藏、系统光标全局变十字。
/// 参考截图 / 元素审查工具（Chrome DevTools inspect、Edge Inspect）的做法：
///   1) 用一圈高亮矩形描出光标下元素的真实边界（绿框 + 淡绿填充）；
///   2) 元素旁贴一张信息卡（类型徽章 / 应用名 / 窗口标题 + 尺寸 / 操作提示），
///      贴着元素下缘，靠近屏幕底则翻到上缘，并夹在虚拟屏内。
/// 左键确认取该元素、右键或 Esc 取消。
///
/// 不铺遮罩（否则 FromPoint 采到的是遮罩自己）——用全局低级鼠标钩子(WH_MOUSE_LL)接管移动/点击，
/// 用 SetSystemCursor 让光标全局变十字（结束时 SystemParametersInfo 恢复）。同一时刻只有一个定位在进行。
/// 覆盖层均为点击穿透(WS_EX_TRANSPARENT)，UIA FromPoint 会跳过；确认取值前再显式藏起，双保险。
/// </summary>
internal static class TargetPicker
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ---- 交互状态 ----
    private static DispatcherFrame? _frame;
    private static bool _confirm;
    private static POINT _clickPt;
    private static POINT _lastPt;
    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelMouseProc? _proc; // 保活委托，防被 GC

    public static CapturedTarget? Pick(Window? owner)
    {
        _frame = new DispatcherFrame();
        _confirm = false;
        _clickPt = default;
        GetCursorPos(out _lastPt);

        // 虚拟屏（多显示器）范围，用于把信息卡夹在屏内。
        int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        // 1) 隐藏整个 client：模态对话框(owner)移出屏幕（不能 Hide），其它窗口直接 Hide；结束再还原。
        var moved = new List<(Window w, double l, double t)>();
        var hidden = new List<Window>();
        foreach (Window w in Application.Current.Windows)
        {
            if (!w.IsVisible) continue;
            if (ReferenceEquals(w, owner))
            {
                moved.Add((w, w.Left, w.Top));
                try { w.Left = -32000; w.Top = -32000; } catch { }
            }
            else
            {
                try { w.Hide(); hidden.Add(w); } catch { }
            }
        }

        // 2) 元素高亮矩形（绿框 + 淡绿填充，点击穿透、不激活、置顶）
        var highlight = MakeOverlay(new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0xF2, 0x6B)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(0x1E, 0x36, 0xF2, 0x6B)),
        });
        highlight.Show();

        // 3) 信息卡（贴着元素）：类型徽章 + 应用名 / 窗口标题·尺寸 / 操作提示
        var passBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43));
        var normChipBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        var chipText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 2), VerticalAlignment = VerticalAlignment.Center, Child = chipText };
        var exeText = new TextBlock { Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        var headRow = new StackPanel { Orientation = Orientation.Horizontal };
        headRow.Children.Add(chip);
        headRow.Children.Add(exeText);
        var subText = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xAA)), FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        var hintText = new TextBlock { Text = Loc.Instance.T("inject.pick.hint"), Foreground = new SolidColorBrush(Color.FromRgb(0x72, 0x72, 0x78)), FontSize = 11, Margin = new Thickness(0, 5, 0, 0) };
        var stack = new StackPanel { MaxWidth = 440 };
        stack.Children.Add(headRow);
        stack.Children.Add(subText);
        stack.Children.Add(hintText);
        var card = MakeOverlay(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1E, 0x1E, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Child = stack,
        });
        card.SizeToContent = SizeToContent.WidthAndHeight;
        card.Show();

        string locPassword = Loc.Instance.T("inject.pick.password");

        // 4) 系统光标全局变十字
        SetCrossCursor();

        // 5) 安装全局鼠标钩子
        _proc = HookProc;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);

        // 6) 采样 + 刷新高亮框/信息卡 + 检 Esc
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) =>
        {
            if (_frame is null || !_frame.Continue) return;
            if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0) // Esc 取消
            {
                _confirm = false;
                _frame.Continue = false;
                return;
            }

            POINT p = _lastPt;
            var (exe, ctrl, isPass, win, rect) = WindowFieldCapture.SampleLight(p.X, p.Y);
            bool hasRect = rect.Width > 1 && rect.Height > 1 && exe != "(本工具)";

            // 高亮矩形贴合元素物理边界；无有效矩形时移出屏外。
            IntPtr hh = new WindowInteropHelper(highlight).Handle;
            if (hh != IntPtr.Zero)
            {
                if (hasRect)
                    SetWindowPos(hh, HWND_TOPMOST, (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
                else
                    SetWindowPos(hh, HWND_TOPMOST, -32000, -32000, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
            }

            // 信息卡内容
            bool empty = string.IsNullOrEmpty(exe) && string.IsNullOrEmpty(ctrl);
            chip.Background = isPass ? passBrush : normChipBrush;
            chipText.Foreground = isPass ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE4));
            chipText.Text = isPass ? locPassword : (string.IsNullOrEmpty(ctrl) ? "—" : ctrl);
            exeText.Text = empty ? "—" : (string.IsNullOrEmpty(exe) ? "—" : exe);
            string dim = hasRect ? $"{(int)rect.Width} × {(int)rect.Height}" : "";
            string sub = string.IsNullOrEmpty(win) ? dim
                       : (string.IsNullOrEmpty(dim) ? win : win + "    " + dim);
            subText.Text = sub;
            subText.Visibility = string.IsNullOrEmpty(sub) ? Visibility.Collapsed : Visibility.Visible;

            // 定位信息卡：贴元素下缘，靠底翻到上缘，夹在虚拟屏内；无矩形时跟随光标。
            card.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(card);
            double cw = card.ActualWidth * dpi.DpiScaleX;
            double ch = card.ActualHeight * dpi.DpiScaleY;
            const int gap = 10;
            double cx, cy;
            if (hasRect)
            {
                cx = rect.Left;
                cy = rect.Bottom + gap;
                if (cy + ch > vTop + vHeight) cy = rect.Top - gap - ch;
            }
            else
            {
                cx = p.X + 18;
                cy = p.Y + 18;
            }
            if (cx + cw > vLeft + vWidth) cx = vLeft + vWidth - cw;
            if (cx < vLeft) cx = vLeft;
            if (cy < vTop) cy = vTop;

            IntPtr hc = new WindowInteropHelper(card).Handle;
            if (hc != IntPtr.Zero)
                SetWindowPos(hc, HWND_TOPMOST, (int)cx, (int)cy, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        };
        timer.Start();

        CapturedTarget? result = null;
        try
        {
            Dispatcher.PushFrame(_frame);
            if (_confirm)
            {
                // 关键：取值前先藏起自己的高亮/信息卡（它们正压在光标点上），再在还原 client 之前取元素——
                // 此刻只有目标应用在光标下，FromScreenPoint 命中的才是目标；否则会命中本工具覆盖层或弹回的本窗口 → 判空。
                try { highlight.Hide(); } catch { }
                try { card.Hide(); } catch { }
                result = WindowFieldCapture.FromScreenPoint(_clickPt.X, _clickPt.Y);
            }
        }
        finally
        {
            timer.Stop();
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            _proc = null;
            RestoreCursors();
            try { highlight.Close(); } catch { }
            try { card.Close(); } catch { }
            foreach (var (w, l, t) in moved) { try { w.Left = l; w.Top = t; } catch { } }
            foreach (var w in hidden) { try { w.Show(); } catch { } }
        }

        return result;
    }

    /// <summary>造一个点击穿透、不激活、置顶、无边框透明的覆盖层窗口（初始在屏外）。</summary>
    private static Window MakeOverlay(UIElement content)
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            IsHitTestVisible = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            Width = 1,
            Height = 1,
            Content = content,
        };
        w.SourceInitialized += (_, _) =>
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            int ex = GetWindowLong(h, GWL_EXSTYLE);
            SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
        return w;
    }

    private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            var d = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            _lastPt = d.pt;
            if (msg == WM_LBUTTONDOWN)
            {
                _clickPt = d.pt;
                _confirm = true;
                if (_frame is not null) _frame.Continue = false;
                return (IntPtr)1; // 吞掉，避免点到目标应用
            }
            if (msg == WM_RBUTTONDOWN)
            {
                _confirm = false;
                if (_frame is not null) _frame.Continue = false;
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    // ---- 系统光标：整套常用光标临时换成十字，结束用 SPI_SETCURSORS 还原 ----
    private static void SetCrossCursor()
    {
        IntPtr cross = LoadCursor(IntPtr.Zero, IDC_CROSS);
        if (cross == IntPtr.Zero) return;
        foreach (int id in new[] { OCR_NORMAL, OCR_IBEAM, OCR_HAND })
        {
            IntPtr copy = CopyIcon(cross);
            if (copy != IntPtr.Zero) SetSystemCursor(copy, (uint)id);
        }
    }

    private static void RestoreCursors()
    {
        SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE);
    }

    // ---- 常量 ----
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204;
    private const int VK_ESCAPE = 0x1B;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    private const int IDC_CROSS = 32515;
    private const int OCR_NORMAL = 32512, OCR_IBEAM = 32513, OCR_HAND = 32649;
    private const uint SPI_SETCURSORS = 0x0057, SPIF_SENDCHANGE = 0x02;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    // ---- P/Invoke ----
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemCursor(IntPtr hcur, uint id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
}
