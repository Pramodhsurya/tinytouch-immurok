using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ImmurokClient.Localization;
using ImmurokClient.Services;
using ImmurokCommon.Protocol;

namespace ImmurokClient.Views;

public partial class AboutPage : Page
{
    private static string T(string k, params object[] a) => Loc.Instance.T(k, a);

    private bool _busy;
    private string? _deviceVersion;   // 归一化后的设备固件版本
    private bool _updateReady;        // 已检测到可用更新

    public AboutPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            Loc.Instance.LanguageChanged -= OnLocLanguageChanged;
            Loc.Instance.LanguageChanged += OnLocLanguageChanged;
            await LoadCurrentVersionAsync();
        };
        Unloaded += (_, _) => Loc.Instance.LanguageChanged -= OnLocLanguageChanged;
    }

    private async void OnLocLanguageChanged() => await LoadCurrentVersionAsync();

    /// <summary>
    /// 应用版本号取自程序集（源头是 Directory.Build.props 的 &lt;Version&gt;）。
    /// 原来这串是写死在多语言文案里的，升版本时必然漏掉——关于页一直显示 0.1.0。
    /// </summary>
    private static string AppVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]          // 去掉 SourceLink 追加的 +<commit>
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "";

    /// <summary>读取当前设备固件版本填入卡片（未连接则置未知）。</summary>
    private async Task LoadCurrentVersionAsync()
    {
        AppVersionText.Text = T("about.version", AppVersion);
        _deviceVersion = null;
        string? info = await AppServices.Pipe.InfoAsync();
        if (info is not null && info.StartsWith(IpcProtocol.Ok))
        {
            string[] p = info.Split(IpcProtocol.Sep);
            if (p.Length >= 4 && p[3] != "-" && p[3].Length > 0)
                _deviceVersion = FwVersion.Normalize(p[3]);
        }
        CurrentVerText.Text = _deviceVersion ?? T("common.unknown");
    }

    private async void OnCheckClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await LoadCurrentVersionAsync();
        if (_deviceVersion is null)
        {
            OtaStatus.Text = T("msg.ota.check_no_device");
            return;
        }

        _busy = true;
        CheckBtn.IsEnabled = false;
        UpdateBtn.Visibility = Visibility.Collapsed;
        NotesBar.IsOpen = false;
        _updateReady = false;
        OtaStatus.Text = T("msg.ota.checking");
        try
        {
            FwCheckResult r = await AppServices.Firmware.CheckAsync(_deviceVersion);
            LatestVerText.Text = r.LatestVersion ?? T("common.unknown");

            if (r.Available)
            {
                _updateReady = true;
                UpdateBtn.Visibility = Visibility.Visible;
                OtaStatus.Text = T("msg.ota.update_available", r.LatestVersion ?? "");
                if (!string.IsNullOrWhiteSpace(r.Notes))
                {
                    NotesBar.Message = r.Notes;
                    NotesBar.IsOpen = true;
                }
            }
            else if (r.Plan == FwPlan.Unknown)
            {
                OtaStatus.Text = T("msg.ota.check_unknown");
            }
            else
            {
                OtaStatus.Text = T("msg.ota.up_to_date");
            }
        }
        catch (Exception)
        {
            OtaStatus.Text = T("msg.ota.check_fail");
        }
        finally
        {
            _busy = false;
            CheckBtn.IsEnabled = true;
        }
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_busy || !_updateReady || _deviceVersion is null) return;

        var confirm = MessageBox.Show(
            T("msg.ota.confirm"),
            T("about.ota"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        _busy = true;
        CheckBtn.IsEnabled = false;
        UpdateBtn.IsEnabled = false;
        OtaProgress.Visibility = Visibility.Visible;
        OtaProgress.Value = 0;
        OtaStatus.Text = T("msg.ota.progress");
        try
        {
            FwUpdateResult res = await AppServices.Firmware.RunUpdateAsync(
                _deviceVersion,
                stage => Dispatcher.Invoke(() => OtaStatus.Text = StageText(stage)),
                pct => Dispatcher.Invoke(() => { OtaProgress.Value = pct; }));

            if (res.Ok)
            {
                OtaStatus.Text = T("msg.ota.done");
                UpdateBtn.Visibility = Visibility.Collapsed;
                _updateReady = false;
                await LoadCurrentVersionAsync();
            }
            else
            {
                OtaStatus.Text = res.ErrorCode switch
                {
                    "battery" => T("msg.ota.battery_low", FirmwareUpdateService.MinBatteryPct),
                    "no_channel" => T("msg.ota.no_channel"),
                    "timeout" => T("msg.ota.timeout"),
                    "download" => T("msg.ota.download_fail"),
                    "bridge" => T("msg.ota.bridge_fail"),
                    "reconnect" => T("msg.ota.reconnect_fail"),
                    _ => T("msg.ota.fail", res.ErrorCode ?? ""),
                };
            }
        }
        finally
        {
            _busy = false;
            CheckBtn.IsEnabled = true;
            UpdateBtn.IsEnabled = true;
        }
    }

    private static string StageText(FwStage stage) => stage switch
    {
        FwStage.Downloading => T("msg.ota.stage_download"),
        FwStage.DownloadingBridge => T("msg.ota.stage_download_bridge"),
        FwStage.Pushing => T("msg.ota.stage_push"),
        FwStage.PushingBridge => T("msg.ota.stage_push_bridge"),
        FwStage.WaitingReconnect => T("msg.ota.stage_reconnect"),
        _ => T("msg.ota.progress"),
    };
}
