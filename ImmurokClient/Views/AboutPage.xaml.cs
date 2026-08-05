using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ImmurokClient.Localization;
using ImmurokClient.Services;

namespace ImmurokClient.Views;

public partial class AboutPage : Page
{
    private static string T(string k, params object[] a) => Loc.Instance.T(k, a);

    private bool _busy;

    public AboutPage()
    {
        InitializeComponent();
    }

    private async void OnOtaClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = T("msg.ota.pick_title"),
            Filter = T("msg.ota.filter"),
        };
        if (dlg.ShowDialog() != true) return;

        byte[] imfw;
        try { imfw = File.ReadAllBytes(dlg.FileName); }
        catch (Exception ex) { OtaStatus.Text = T("msg.ota.read_fail", ex.Message); return; }

        _busy = true;
        PickBtn.IsEnabled = false;
        OtaProgress.Visibility = Visibility.Visible;
        OtaProgress.Value = 0;
        OtaStatus.Text = T("msg.ota.progress");
        try
        {
            string terminal = await AppServices.Pipe.OtaPushStreamAsync(imfw,
                pct => Dispatcher.Invoke(() => { OtaProgress.Value = pct; OtaStatus.Text = T("msg.ota.progress_pct", pct); }));

            OtaStatus.Text = terminal switch
            {
                var s when s.Contains("DONE") => T("msg.ota.done"),
                var s when s.Contains("OTA_NOT_AVAILABLE") => T("msg.ota.no_channel"),
                var s when s.Contains("TIMEOUT") => T("msg.ota.timeout"),
                _ => T("msg.ota.fail", terminal),
            };
        }
        finally
        {
            _busy = false;
            PickBtn.IsEnabled = true;
        }
    }
}
