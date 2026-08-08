using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ImmurokClient.Localization;
using ImmurokClient.Services;
using ImmurokCommon.Protocol;

namespace ImmurokClient.Views;

public partial class DevicePage : Page
{
    private static string T(string k, params object[] a) => Loc.Instance.T(k, a);

    private byte _activeSlot;
    private bool _connected;

    public DevicePage()
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
        // 连接状态
        string? status = await AppServices.Pipe.StatusAsync();
        bool connected = false;
        if (status is null)
        {
            ConnText.Text = T("msg.dev.no_service");
        }
        else
        {
            string[] p = status.Split(IpcProtocol.Sep);
            connected = p.Length >= 2 && p[1] == "1";
            string name = p.Length >= 3 ? p[2] : "";
            ConnText.Text = connected ? T("msg.dev.connected", name) : T("msg.dev.not_connected");
        }

        // 设备信息
        InfoText.Text = "";
        if (connected)
        {
            string? info = await AppServices.Pipe.InfoAsync();
            if (info is not null && info.StartsWith(IpcProtocol.Ok))
            {
                string[] ip = info.Split(IpcProtocol.Sep);
                if (ip.Length >= 5)
                {
                    string batt = ip[2] == "-1" ? T("common.unknown") : $"{ip[2]}%";
                    InfoText.Text = T("msg.dev.info", batt, ip[3], ip[4]);
                }
            }
        }

        // 配对状态
        string? pair = await AppServices.Pipe.PairStatusAsync();
        bool paired = pair?.Contains("PAIRED") == true && pair.Contains("UNPAIRED") != true;
        PairText.Text = paired ? T("msg.dev.paired") : T("msg.dev.unpaired");
        PairBtn.IsEnabled = connected && !paired;

        // 双主机
        await RefreshDualHostAsync(connected, paired);
    }

    private async Task RefreshDualHostAsync(bool connected, bool paired)
    {
        DualHostCard.Visibility = Visibility.Collapsed;
        _connected = connected;

        // 设备不在身边、但本机仍有绑定：仍要给出「解绑本机」入口（只清本地绑定数据），
        // 否则用户无法改配新设备。槽位状态要查设备，此时未知，隐藏槽位行。
        if (!connected)
        {
            if (!paired) return;
            DualDesc.Text = T("device.dual.desc_offline");
            Slot1Row.Visibility = Visibility.Collapsed;
            Slot2Row.Visibility = Visibility.Collapsed;
            // 槽位行看不见，行内的悬停按钮也就无从触达：离线时用独立的「解绑本机」按钮兜底。
            UnbindSelfBtn.Visibility = Visibility.Visible;
            UnbindSelfBtn.IsEnabled = true;
            SetDualHint(T("msg.unbind.local_only_hint"));
            DualHostCard.Visibility = Visibility.Visible;
            return;
        }

        DualDesc.Text = T("device.dual.desc");
        Slot1Row.Visibility = Visibility.Visible;
        Slot2Row.Visibility = Visibility.Visible;
        UnbindSelfBtn.Visibility = Visibility.Collapsed; // 在线时解绑入口都在槽位行里

        string? s = await AppServices.Pipe.SlotStatusAsync();
        // OK:<supported>:<bitmap>:<active>
        if (s is null || !s.StartsWith(IpcProtocol.Ok)) return;
        string[] parts = s.Split(IpcProtocol.Sep);
        if (parts.Length < 4) return;
        bool supported = parts[1] == "1";
        if (!supported) return; // 旧固件不显示双主机卡片

        int.TryParse(parts[2], out int bitmap);
        int.TryParse(parts[3], out int active);
        _activeSlot = (byte)active;

        bool slot1 = (bitmap & 0b01) != 0;
        bool slot2 = (bitmap & 0b10) != 0;
        Slot1State.Text = SlotLabel(slot1, active == 1);
        Slot2State.Text = SlotLabel(slot2, active == 2);

        // 每个槽自己知道该给哪种解绑按钮：活跃槽=本机，另一个已占用的槽=另一台。
        ConfigureSlotButton(Slot1UnbindBtn, 1, slot1, active == 1, paired);
        ConfigureSlotButton(Slot2UnbindBtn, 2, slot2, active == 2, paired);
        UpdateSlotReveal();

        DualHostCard.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 按槽位状态决定这一行的解绑按钮：空槽没有按钮；活跃槽（本机）给「解绑本机」；
    /// 另一台占用的槽给「解绑另一台」。用 Tag 记住槽号，点击时据此分派。
    /// </summary>
    private static void ConfigureSlotButton(
        Wpf.Ui.Controls.Button btn, byte slot, bool occupied, bool isSelf, bool paired)
    {
        if (!occupied)
        {
            btn.Visibility = Visibility.Collapsed;
            return;
        }

        btn.Visibility = Visibility.Visible;
        btn.Content = T(isSelf ? "device.unbind.self" : "device.unbind.other");
        btn.Tag = slot.ToString();
        btn.IsEnabled = !isSelf || paired; // 「解绑本机」要本机确实有绑定
    }

    /// <summary>
    /// 设置提示文字，没有内容时整体收起。空 TextBlock 仍会占一整行加上外边距，
    /// 平时没提示可说的，那段空白就挂在卡片底部。
    /// </summary>
    private void SetDualHint(string text)
    {
        DualHint.Text = text;
        DualHint.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSlotRowHover(object sender, System.Windows.Input.MouseEventArgs e) => UpdateSlotReveal();

    /// <summary>悬停哪一行就淡入哪一行的按钮；同步 IsHitTestVisible，避免点到看不见的按钮。</summary>
    private void UpdateSlotReveal()
    {
        Reveal(Slot1UnbindBtn, Slot1Row.IsMouseOver);
        Reveal(Slot2UnbindBtn, Slot2Row.IsMouseOver);

        static void Reveal(Wpf.Ui.Controls.Button btn, bool hovering)
        {
            bool on = hovering && btn.Visibility == Visibility.Visible;
            btn.Opacity = on ? 1 : 0;
            btn.IsHitTestVisible = on;
        }
    }

    /// <summary>槽位行内的解绑按钮：按 Tag 里的槽号分派到「解绑本机」或「解绑另一台」。</summary>
    private async void OnSlotUnbindClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button btn) return;
        if (!byte.TryParse(btn.Tag as string, out byte slot)) return;

        if (slot == _activeSlot) await UnbindSelfAsync();
        else await UnbindOtherAsync(slot);
    }

    private static string SlotLabel(bool occupied, bool active)
    {
        if (!occupied) return T("msg.slot.empty");
        return active ? T("msg.slot.self_active") : T("msg.slot.other");
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnPairClick(object sender, RoutedEventArgs e)
    {
        PairBtn.IsEnabled = false;
        PairText.Text = T("msg.pair.progress");
        string? r = await AppServices.Pipe.PairStartAsync();
        PairText.Text = r switch
        {
            var x when x?.Contains("PAIRED") == true => T("msg.pair.ok"),
            var x when x?.Contains("NEEDSRESET") == true => T("msg.pair.needsreset"),
            var x when x?.Contains("LINKPARAMS") == true => T("msg.pair.linkparams"),
            var x when x?.Contains("WAITBUTTON") == true => T("msg.pair.waitbutton"),
            null => T("msg.pair.noservice"),
            _ => T("msg.pair.fail"),
        };
        await RefreshAsync();
    }

    private async void OnUnbindSelfClick(object sender, RoutedEventArgs e) => await UnbindSelfAsync();

    private async Task UnbindSelfAsync()
    {
        // 两种情况的后果不同，确认文案要分开：
        // 已连接 → 通知设备清槽（设备会重启）；未连接 → 只清本地绑定，设备侧槽位仍占用。
        var confirm = MessageBox.Show(
            _connected ? T("msg.unbind.self_confirm") : T("msg.unbind.local_only_confirm"),
            T("device.unbind.self"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        SetDualHint(_connected ? T("msg.unbind.self_progress") : T("msg.unbind.local_only_progress"));
        string? r = await AppServices.Pipe.SlotClearOwnAsync();

        if (r?.StartsWith($"{IpcProtocol.Ok}:LOCAL") == true)
            SetDualHint(T("msg.unbind.local_only_ok"));
        else if (r?.StartsWith(IpcProtocol.Ok) == true)
            SetDualHint(T("msg.unbind.self_ok"));
        else if (r == IpcProtocol.Reject)
            // 设备拒绝（本机不是活跃槽）——多半是本机绑的还是旧设备、现在连上的却是新设备。
            // 此时本地那把 shared_key 对当前设备无效，但只有用户能确认要不要丢弃它，故显式询问。
            await OfferLocalUnbindAsync();
        else
            SetDualHint(T("msg.unbind.fail", r ?? ""));

        await RefreshAsync();
    }

    /// <summary>设备拒绝解绑后，询问是否仅清除本地绑定数据（以便改配新设备）。</summary>
    private async Task OfferLocalUnbindAsync()
    {
        SetDualHint(T("msg.unbind.rejected"));
        var choice = MessageBox.Show(
            T("msg.unbind.rejected_offer"),
            T("device.unbind.self"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.OK) return;

        string? r = await AppServices.Pipe.PairResetAsync();
        SetDualHint(r?.StartsWith(IpcProtocol.Ok) == true
            ? T("msg.unbind.local_only_ok")
            : T("msg.unbind.fail", r ?? ""));
    }

    private async Task UnbindOtherAsync(byte slot)
    {
        if (slot == 0) return;
        // 解绑另一台要在设备上过指纹门，弹认证窗（含 30s 倒计时）。
        string? r = FpAuthDialog.Run(Window.GetWindow(this),
            T("msg.fpauth.unbind_other", slot),
            () => AppServices.Pipe.SlotClearAsync(slot));
        SetDualHint(r == IpcProtocol.Ok ? T("msg.unbind.other_ok") : T("msg.unbind.other_fail", r ?? ""));
        await RefreshAsync();
    }
}
