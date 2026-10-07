using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ImmurokClient.Helpers;

/// <summary>
/// 修复 WPF-UI NavigationView 内页面滚轮失效的问题。
///
/// 根因：NavigationView 的内容宿主自带一个 ScrollViewer，而页面里又套了一个。
/// 内层页面 ScrollViewer 被外层给了无限高度 → 自身 ScrollableHeight=0（没内容可滚），
/// 却仍会消费冒泡的 MouseWheel，把事件"吃掉"；真正能滚的是外层那个 ScrollViewer
/// （所以只有拖动滚动条有效、滚轮无效）。
///
/// 方案：在页面 ScrollViewer 上以隧道事件 PreviewMouseWheel（handledEventsToo）接管，
/// 找到"自身或最近的、真正可滚动的" ScrollViewer 来滚动，并标记 Handled 阻断内层默认吞没。
///
/// 用法（XAML）：
///   xmlns:h="clr-namespace:ImmurokClient.Helpers"
///   &lt;ScrollViewer h:WheelScroll.Enabled="True" .../&gt;
/// </summary>
public static class WheelScroll
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(WheelScroll),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool v) => o.SetValue(EnabledProperty, v);

    private static readonly MouseWheelEventHandler Handler = OnPreviewMouseWheel;

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        if ((bool)e.NewValue)
            // handledEventsToo:true —— 即使外层已把隧道事件标记 handled，仍然触发。
            sv.AddHandler(UIElement.PreviewMouseWheelEvent, Handler, handledEventsToo: true);
        else
            sv.RemoveHandler(UIElement.PreviewMouseWheelEvent, Handler);
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        ScrollViewer? target = FindScrollable(sv);
        if (target is null) return;
        // 滚轮向上 Delta 为正，应减小垂直偏移；÷2 让步进接近系统默认手感。
        target.ScrollToVerticalOffset(target.VerticalOffset - e.Delta / 2.0);
        e.Handled = true; // 阻断内层 ScrollViewer 对冒泡 MouseWheel 的默认吞没
    }

    /// <summary>返回自身或最近的、真正可滚动（ScrollableHeight&gt;0）的 ScrollViewer。</summary>
    private static ScrollViewer? FindScrollable(ScrollViewer start)
    {
        if (start.ScrollableHeight > 0) return start;
        DependencyObject? o = VisualTreeHelper.GetParent(start);
        while (o is not null)
        {
            if (o is ScrollViewer p && p.ScrollableHeight > 0) return p;
            o = VisualTreeHelper.GetParent(o);
        }
        return null;
    }
}
