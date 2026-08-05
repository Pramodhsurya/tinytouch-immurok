using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ImmurokClient.Localization;
using ImmurokClient.Services;

namespace ImmurokClient.Views;

public partial class SetupPage : Page
{
    private static string T(string k, params object[] a) => Loc.Instance.T(k, a);

    private sealed record LangItem(string Code, string Name);

    private bool _langInit;

    public SetupPage()
    {
        InitializeComponent();
        UserText.Text = Environment.UserName;
        InitLanguages();
        Loaded += async (_, _) =>
        {
            Loc.Instance.LanguageChanged -= OnLocLanguageChanged;
            Loc.Instance.LanguageChanged += OnLocLanguageChanged;
            await RefreshAsync();
        };
        Unloaded += (_, _) => Loc.Instance.LanguageChanged -= OnLocLanguageChanged;
    }

    private async void OnLocLanguageChanged() => await RefreshAsync();

    private void InitLanguages()
    {
        _langInit = true;
        LangCombo.ItemsSource = Loc.SupportedLanguages.Select(l => new LangItem(l.Code, l.Name)).ToList();
        LangCombo.SelectedValue = Loc.Instance.CurrentLanguage;
        _langInit = false;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_langInit) return;
        if (LangCombo.SelectedValue is not string code) return;
        Loc.Instance.SetLanguage(code);   // 即时刷新所有 {loc:Tr} 绑定
        ClientSettings.Language = code;    // 持久化
    }

    private bool _loading;

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        string? r = await AppServices.Pipe.PassStatusAsync();
        PassStateText.Text = r switch
        {
            var s when s?.Contains("CONFIGURED") == true => T("msg.setup.pass_configured"),
            var s when s?.Contains("NOTSET") == true => T("msg.setup.pass_notset"),
            null => T("msg.setup.pass_noservice"),
            _ => T("msg.setup.pass_status", r),
        };

        _loading = true;
        string? sa = await AppServices.Pipe.SshAgentStatusAsync();
        SshAgentToggle.IsChecked = sa?.Contains("ON") == true;
        _loading = false;
    }

    private async void OnSshAgentToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = SshAgentToggle.IsChecked == true;
        string? r = await AppServices.Pipe.SshAgentSetAsync(on);
        SshAgentHint.Text = on ? T("msg.setup.ssh_on") : T("msg.setup.ssh_off");
        if (r != "OK") SshAgentHint.Text = T("common.op_fail", r ?? "");
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        string pwd = PwdBox.Password;
        if (string.IsNullOrEmpty(pwd))
        {
            PassHint.Text = T("msg.setup.need_pass");
            return;
        }
        string? r = await AppServices.Pipe.PassSetAsync(Environment.UserName, pwd);
        PwdBox.Clear();
        PassHint.Text = r == "OK" ? T("msg.setup.saved") : T("msg.setup.save_fail", r ?? "");
        await RefreshAsync();
    }

    private async void OnClearClick(object sender, RoutedEventArgs e)
    {
        await AppServices.Pipe.PassClearAsync();
        PassHint.Text = T("msg.setup.cleared");
        await RefreshAsync();
    }
}
