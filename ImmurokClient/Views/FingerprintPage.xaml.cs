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
        _busy = true;
        try
        {
            StatusText.Text = T("msg.fp.enroll_prep", slot);
            string terminal = await AppServices.Pipe.EnrollStreamAsync(slot, frame =>
                Dispatcher.Invoke(() => StatusText.Text = MapProgress(frame)));
            StatusText.Text = terminal switch
            {
                var s when s.Contains("COMPLETE") => T("msg.fp.enroll_ok", slot),
                var s when s.Contains("TIMEOUT") => T("msg.fp.enroll_timeout"),
                var s when s.Contains("NOT_CONNECTED") => T("common.dev_disconnected"),
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
            StatusText.Text = T("msg.fp.delete_prog", slot);
            string? r = await AppServices.Pipe.FpDeleteAsync(slot);
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
                StatusText.Text = T("msg.fp.switch_del_prog");
                string? r = await AppServices.Pipe.FpDeleteAsync(SwitchSlot);
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

    private static string MapProgress(string frame)
    {
        string[] p = frame.Split(':');
        string ev = p.Length > 1 ? p[1] : "";
        string step = (p.Length > 3 && p[3] != "0") ? T("msg.fp.enr.step", p[2], p[3]) : "";
        return ev switch
        {
            "Waiting"    => T("msg.fp.enr.waiting", step),
            "Captured"   => T("msg.fp.enr.captured", step),
            "LiftFinger" => T("msg.fp.enr.lift", step),
            "Processing" => T("msg.fp.enr.processing", step),
            "Overlap"    => T("msg.fp.enr.overlap", step),
            _             => $"{ev} {step}",
        };
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
}
