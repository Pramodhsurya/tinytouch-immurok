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
    private byte _otherOccupiedSlot; // 0 = 无

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
        _otherOccupiedSlot = 0;
        if (!connected) return;

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

        // 另一台 = 已占用且非活跃的那个槽
        if (slot1 && active != 1) _otherOccupiedSlot = 1;
        else if (slot2 && active != 2) _otherOccupiedSlot = 2;

        UnbindOtherBtn.Visibility = _otherOccupiedSlot != 0 ? Visibility.Visible : Visibility.Collapsed;
        UnbindSelfBtn.IsEnabled = paired;
        DualHostCard.Visibility = Visibility.Visible;
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

    private async void OnUnbindSelfClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            T("msg.unbind.self_confirm"),
            T("device.unbind.self"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        DualHint.Text = T("msg.unbind.self_progress");
        string? r = await AppServices.Pipe.SlotClearOwnAsync();
        DualHint.Text = r == IpcProtocol.Ok ? T("msg.unbind.self_ok") : T("msg.unbind.fail", r ?? "");
        await RefreshAsync();
    }

    private async void OnUnbindOtherClick(object sender, RoutedEventArgs e)
    {
        if (_otherOccupiedSlot == 0) return;
        DualHint.Text = T("msg.unbind.other_progress", _otherOccupiedSlot);
        string? r = await AppServices.Pipe.SlotClearAsync(_otherOccupiedSlot);
        DualHint.Text = r == IpcProtocol.Ok ? T("msg.unbind.other_ok") : T("msg.unbind.other_fail", r ?? "");
        await RefreshAsync();
    }
}
