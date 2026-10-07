using System.Windows;
using System.Windows.Controls;
using ImmurokClient.Localization;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;
using WpfTextBox = Wpf.Ui.Controls.TextBox;

namespace ImmurokClient.Views;

/// <summary>
/// 代码构建的简易模态输入框（1~2 个文本字段 + 确定/取消）。用于重命名等。
/// 纯代码，避免额外 XAML；随应用主题渲染。
/// </summary>
internal static class InputDialog
{
    /// <summary>返回 (是否确定, 字段1, 字段2)。label2 为 null 时只显示一个字段。</summary>
    public static (bool ok, string v1, string v2) Show(
        Window? owner, string title, string label1, string init1, string? label2 = null, string init2 = "")
    {
        var win = new FluentWindow
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ExtendsContentIntoTitleBar = true,
            WindowBackdropType = WindowBackdropType.Mica,
            ShowInTaskbar = false,
        };
        if (owner is not null) win.Owner = owner;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new TitleBar { Title = title };
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var panel = new StackPanel { Margin = new Thickness(20, 8, 20, 20) };
        Grid.SetRow(panel, 1);
        root.Children.Add(panel);

        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label1, Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        var tb1 = new WpfTextBox { Text = init1 };
        panel.Children.Add(tb1);

        WpfTextBox? tb2 = null;
        if (label2 is not null)
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = label2, Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 12, 0, 4) });
            tb2 = new WpfTextBox { Text = init2 };
            panel.Children.Add(tb2);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var okBtn = new WpfButton { Content = Loc.Instance.T("common.ok"), Appearance = ControlAppearance.Primary, MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancelBtn = new WpfButton { Content = Loc.Instance.T("common.cancel"), Appearance = ControlAppearance.Secondary, MinWidth = 88, IsCancel = true };
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);
        panel.Children.Add(buttons);

        bool ok = false;
        okBtn.Click += (_, _) => { ok = true; win.Close(); };
        cancelBtn.Click += (_, _) => { ok = false; win.Close(); };

        win.Loaded += (_, _) => { tb1.Focus(); tb1.SelectAll(); };
        win.ShowDialog();

        return (ok, tb1.Text, tb2?.Text ?? "");
    }
}
