using System.Windows;
using System.Windows.Controls;
using ImmurokClient.Localization;
using ImmurokClient.Services;
using ImmurokCommon.Protocol;

namespace ImmurokClient.Views;

public partial class StatusPage : Page
{
    private static string T(string k, params object[] a) => Loc.Instance.T(k, a);

    public StatusPage()
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
        ChecklistPanel.Children.Clear();

        string? status = await AppServices.Pipe.StatusAsync();
        bool serviceUp = status is not null;
        bool connected = status?.Split(IpcProtocol.Sep) is { Length: >= 2 } p && p[1] == "1";

        string? pair = await AppServices.Pipe.PairStatusAsync();
        bool paired = pair?.Contains("PAIRED") == true && pair.Contains("UNPAIRED") != true;

        string? pass = await AppServices.Pipe.PassStatusAsync();
        bool passConfigured = pass?.Contains("CONFIGURED") == true;

        bool cpInstalled = IsCredentialProviderInstalled();

        AddItem(T("msg.status.service"), serviceUp);
        AddItem(T("msg.status.connected"), connected);
        AddItem(T("msg.status.paired"), paired);
        AddItem(T("msg.status.pass"), passConfigured);
        AddItem(T("msg.status.cp"), cpInstalled, cpInstalled ? null : T("msg.status.cp_hint"));
    }

    /// <summary>读注册表判断 CP 是否已注册（只读 HKLM，无需管理员）。</summary>
    private static bool IsCredentialProviderInstalled()
    {
        const string clsid = "{C433E3A5-FA34-42B1-A94C-5D793D2EEA83}";
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\" + clsid);
            return key is not null;
        }
        catch { return false; }
    }

    private void AddItem(string label, bool ok, string? hint = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        sp.Children.Add(new TextBlock
        {
            Text = ok ? "✓  " : "✕  ",
            Foreground = ok ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.IndianRed,
            FontWeight = FontWeights.Bold,
        });
        sp.Children.Add(new TextBlock { Text = label + (hint is null ? "" : T("common.hintwrap", hint)) });
        ChecklistPanel.Children.Add(sp);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
}
