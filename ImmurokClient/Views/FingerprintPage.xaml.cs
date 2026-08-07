using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ImmurokClient.Localization;
using ImmurokClient.Services;
using ImmurokCommon.Protocol;

namespace ImmurokClient.Views;

public partial class FingerprintPage : Page
{
    private static string T(string k, params object[] a) => Loc.Instance.T(k, a);

    private const int AuthSlotCount = 5; // 认证槽 0–4
    private const byte SwitchSlot = 5;   // 切换指纹槽
    private bool _busy;
    private bool _switchEnrolled;
    private int _enrolledCount; // 已登记的认证指纹数（>0 时录入前需先用旧指纹验证）

    public FingerprintPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
            Loc.Instance.LanguageChanged += OnLanguageChanged;
            await RefreshAsync();
        };
        Unloaded += (_, _) => Loc.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private async void OnLanguageChanged() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        string? r = await AppServices.Pipe.FpSlotsAsync();
        if (r is null)
        {
            CountText.Text = T("common.no_service_resp");
            SlotPanel.ItemsSource = null;
            SwitchState.Text = "—";
            return;
        }
        if (!r.StartsWith(IpcProtocol.Ok))
        {
            CountText.Text = r.Contains("NOT_CONNECTED") ? T("common.dev_disconnected") : T("common.read_fail", r);
            SlotPanel.ItemsSource = null;
            SwitchState.Text = "—";
            return;
        }

        int bitmap = 0;
        string[] p = r.Split(IpcProtocol.Sep);
        if (p.Length >= 2) int.TryParse(p[1], out bitmap);

        int count = 0;
        var rows = new System.Collections.Generic.List<UIElement>();
        for (int i = 0; i < AuthSlotCount; i++)
        {
            bool enrolled = (bitmap & (1 << i)) != 0;
            if (enrolled) count++;
            rows.Add(BuildRow((byte)i, enrolled));
        }
        _enrolledCount = count;
        CountText.Text = T("msg.fp.count", count, AuthSlotCount);
        SlotPanel.ItemsSource = rows;

        // 切换指纹（bit 5）
        _switchEnrolled = (bitmap & (1 << SwitchSlot)) != 0;
        SwitchState.Text = _switchEnrolled ? T("msg.fp.switch_enrolled") : T("msg.fp.switch_none");
        SwitchBtn.Content = _switchEnrolled ? T("msg.fp.switch_delete") : T("fp.switch.enroll");
    }

    private UIElement BuildRow(byte slot, bool enrolled)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x14, 0x80, 0x80, 0x80)),
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = T("msg.fp.slot", slot), Width = 70, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = enrolled ? T("msg.fp.enrolled") : T("msg.slot.empty"),
            Width = 90,
            Opacity = enrolled ? 1.0 : 0.55,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var btn = new Wpf.Ui.Controls.Button { Content = enrolled ? T("common.delete") : T("msg.fp.enroll_btn"), MinWidth = 84 };
        if (enrolled)
        {
            btn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            btn.Click += async (_, _) => await DeleteAsync(slot);
        }
        else
        {
            btn.Click += async (_, _) => await EnrollAsync(slot);
        }
        panel.Children.Add(btn);
        border.Child = panel;
        return border;
    }

    // ---- 认证指纹 ----

    private async Task EnrollAsync(byte slot)
    {
        if (_busy) return;

        // 设备上已有指纹时，固件会先要求用「已登记的手指」验证一次，通过后才开始采集新指纹。
        // 不先说明的话，用户会在该换手指的时候继续按旧手指（对齐 macOS enroll.confirm.newfinger）。
        if (_enrolledCount > 0)
        {
            var go = MessageBox.Show(
                T("msg.enroll.newfinger_message"),
                T("msg.enroll.newfinger_title"), MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (go != MessageBoxResult.OK) return;
        }

        _busy = true;
        try
        {
            string title = slot == SwitchSlot ? T("fp.switch") : T("msg.fp.slot", slot);
            string terminal = EnrollDialog.Show(Window.GetWindow(this), slot, T("enroll.title") + " · " + title);
            StatusText.Text = terminal switch
            {
                var s when s.Contains("COMPLETE") => T("msg.fp.enroll_ok", slot),
                var s when s.Contains("TIMEOUT") => T("msg.fp.enroll_timeout"),
                var s when s.Contains("NOT_CONNECTED") => T("common.dev_disconnected"),
                "CANCELLED" => T("msg.enroll.cancelled"),
                _ => T("msg.fp.enroll_fail", terminal),
            };
            await RefreshAsync();
        }
        finally { _busy = false; }
    }

    private async Task DeleteAsync(byte slot)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            // 删除指纹要在设备上过指纹门，弹认证窗（含 30s 倒计时）。
            string? r = FpAuthDialog.Run(Window.GetWindow(this),
                T("msg.fpauth.fp_delete", slot),
                () => AppServices.Pipe.FpDeleteAsync(slot));
            StatusText.Text = r?.Contains("DELETED") == true ? T("msg.fp.delete_ok", slot) : T("common.delete_fail", r ?? "");
            await RefreshAsync();
        }
        finally { _busy = false; }
    }

    // ---- 切换指纹 ----

    private async void OnSwitchClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_switchEnrolled)
        {
            _busy = true;
            try
            {
                string? r = FpAuthDialog.Run(Window.GetWindow(this),
                    T("msg.fpauth.fp_delete_switch"),
                    () => AppServices.Pipe.FpDeleteAsync(SwitchSlot));
                StatusText.Text = r?.Contains("DELETED") == true ? T("msg.fp.switch_del_ok") : T("common.delete_fail", r ?? "");
                await RefreshAsync();
            }
            finally { _busy = false; }
        }
        else
        {
            await EnrollAsync(SwitchSlot);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
}
