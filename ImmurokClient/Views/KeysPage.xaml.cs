using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ImmurokClient.Localization;
using ImmurokClient.Services;
using ImmurokCommon.Protocol;

namespace ImmurokClient.Views;

public partial class KeysPage : Page
{
    private static string T(string k, params object[] a) => Loc.Instance.T(k, a);

    private const byte CatSsh = 0, CatOtp = 1, CatApi = 2;
    private bool _busy;

    // 批量删除选择集（跨类别，按 类+索引 标识）
    private readonly HashSet<(byte cat, byte idx)> _selected = new();

    private record KeyEntry(byte Index, string Name, string Extra);

    public KeysPage()
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
        _selected.Clear();
        UpdateDelSelectedBtn();
        await LoadCategoryAsync(CatOtp, OtpPanel, OtpEmpty);
        await LoadCategoryAsync(CatSsh, SshPanel, SshEmpty);
        await LoadCategoryAsync(CatApi, ApiPanel, ApiEmpty);
    }

    private async Task LoadCategoryAsync(byte cat, ItemsControl panel, TextBlock empty)
    {
        string? r = await AppServices.Pipe.KeyListAsync(cat);
        var entries = ParseList(r);
        var rows = new List<UIElement>();
        foreach (var e in entries) rows.Add(BuildRow(cat, e));
        panel.ItemsSource = rows;
        empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static List<KeyEntry> ParseList(string? r)
    {
        var list = new List<KeyEntry>();
        if (r is null || !r.StartsWith(IpcProtocol.Ok)) return list;
        // OK:<idx,b64name,b64extra>;...
        int c = r.IndexOf(IpcProtocol.Sep);
        string payload = c >= 0 ? r[(c + 1)..] : "";
        if (payload.Length == 0) return list;
        foreach (string ent in payload.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = ent.Split(',');
            if (f.Length < 2 || !byte.TryParse(f[0], out byte idx)) continue;
            string name = FromB64(f[1]);
            string extra = f.Length > 2 ? FromB64(f[2]) : "";
            list.Add(new KeyEntry(idx, name, extra));
        }
        return list;
    }

    private UIElement BuildRow(byte cat, KeyEntry e)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x14, 0x80, 0x80, 0x80)),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });               // 复选框
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 名称
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });               // 操作

        // 选择复选框（批量删除）
        var cb = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            IsChecked = _selected.Contains((cat, e.Index)),
        };
        cb.Checked += (_, _) => { _selected.Add((cat, e.Index)); UpdateDelSelectedBtn(); };
        cb.Unchecked += (_, _) => { _selected.Remove((cat, e.Index)); UpdateDelSelectedBtn(); };
        Grid.SetColumn(cb, 0);
        grid.Children.Add(cb);

        string title = string.IsNullOrEmpty(e.Extra) ? e.Name : $"{e.Name}  ·  {e.Extra}";
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(actions, 2);

        if (cat == CatOtp)
        {
            var codeText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            var codeBtn = new Wpf.Ui.Controls.Button { Content = T("msg.keys.getcode"), MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
            // 认证窗内部同步阻塞（ShowDialog），这里不再需要 async。
            codeBtn.Click += (_, _) =>
            {
                string? cr = FpAuthDialog.Run(Window.GetWindow(this),
                    T("msg.fpauth.otp_code", e.Name),
                    () => AppServices.Pipe.KeyOtpAsync(e.Index));
                if (cr is not null && cr.StartsWith(IpcProtocol.Ok))
                {
                    string code = cr[(cr.IndexOf(IpcProtocol.Sep) + 1)..];
                    codeText.Text = code;
                    StatusText.Text = T("msg.keys.otp_code", e.Name, code);
                    try { Clipboard.SetText(code); StatusText.Text += T("common.copied_suffix"); } catch { }
                }
                else StatusText.Text = T("msg.keys.getcode_fail");
            };
            actions.Children.Add(codeText);
            actions.Children.Add(codeBtn);
        }
        else if (cat == CatSsh)
        {
            var pubBtn = new Wpf.Ui.Controls.Button { Content = T("msg.keys.pubkey"), MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
            pubBtn.Click += async (_, _) =>
            {
                string? pr = await AppServices.Pipe.KeySshPubAsync(e.Index);
                // OK:<b64authkey>:<b64fp>
                if (pr is not null && pr.StartsWith(IpcProtocol.Ok))
                {
                    string[] pp = pr.Split(IpcProtocol.Sep);
                    if (pp.Length >= 3)
                    {
                        string authkey = FromB64(pp[1]);
                        string fp = FromB64(pp[2]);
                        try { Clipboard.SetText(authkey); } catch { }
                        StatusText.Text = T("msg.keys.ssh_pub", e.Name, fp);
                    }
                }
                else StatusText.Text = T("msg.keys.pub_fail");
            };
            actions.Children.Add(pubBtn);
        }

        // 重命名
        var editBtn = new Wpf.Ui.Controls.Button
        {
            Content = T("keys.rename"),
            MinWidth = 72,
            Margin = new Thickness(0, 0, 6, 0),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
        };
        editBtn.Click += async (_, _) => await RenameAsync(cat, e);
        actions.Children.Add(editBtn);

        var delBtn = new Wpf.Ui.Controls.Button
        {
            Content = T("common.delete"),
            MinWidth = 72,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
        };
        delBtn.Click += async (_, _) => await DeleteAsync(cat, e);
        actions.Children.Add(delBtn);

        grid.Children.Add(actions);
        border.Child = grid;
        return border;
    }

    private void UpdateDelSelectedBtn() => DelSelectedBtn.IsEnabled = _selected.Count > 0 && !_busy;

    // ---- 重命名 ----

    private async Task RenameAsync(byte cat, KeyEntry e)
    {
        if (_busy) return;
        bool isOtp = cat == CatOtp;
        var (ok, name, svc) = InputDialog.Show(Window.GetWindow(this), T("keys.rename_title"),
            T("ph.name"), e.Name, isOtp ? T("ph.service") : null, e.Extra);
        if (!ok) return;
        name = name.Trim();
        if (name.Length == 0) { StatusText.Text = T("msg.keys.need_name"); return; }
        _busy = true;
        UpdateDelSelectedBtn();
        try
        {
            string svc2 = isOtp ? svc.Trim() : "";
            string nm = name;
            string? r = FpAuthDialog.Run(Window.GetWindow(this),
                T("msg.fpauth.rename", e.Name),
                () => AppServices.Pipe.KeyUpdateAsync(cat, e.Index, nm, svc2));
            StatusText.Text = r == IpcProtocol.Ok ? T("keys.rename_ok", name) : T("keys.rename_fail");
            await RefreshAsync();
        }
        finally { _busy = false; UpdateDelSelectedBtn(); }
    }

    // ---- 删除 ----

    private async Task DeleteAsync(byte cat, KeyEntry e)
    {
        if (_busy) return;
        var confirm = MessageBox.Show(T("msg.keys.del_confirm", e.Name),
            T("msg.keys.del_title"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        _busy = true;
        UpdateDelSelectedBtn();
        try
        {
            string? r = FpAuthDialog.Run(Window.GetWindow(this),
                T("msg.fpauth.key_delete", e.Name),
                () => AppServices.Pipe.KeyDeleteAsync(cat, e.Index));
            StatusText.Text = r == IpcProtocol.Ok ? T("msg.keys.del_ok", e.Name) : T("msg.keys.del_fail");
            await RefreshAsync();
        }
        finally { _busy = false; UpdateDelSelectedBtn(); }
    }

    private async void OnDeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var sel = _selected.ToList();
        if (sel.Count == 0) { StatusText.Text = T("keys.none_selected"); return; }
        var confirm = MessageBox.Show(T("keys.del_sel_confirm", sel.Count),
            T("msg.keys.del_title"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        _busy = true;
        UpdateDelSelectedBtn();
        try
        {
            int total = sel.Count;
            // 每删一项设备都要单独过一次指纹门，因此用一个认证窗贯穿全程，
            // 每项开始时重置倒计时（step 回调），而不是弹 N 个窗。
            int done = FpAuthDialog.RunSteps(Window.GetWindow(this),
                T("msg.fpauth.key_delete_batch", 1, total),
                async step =>
                {
                    int n = 0, i = 0;
                    // 同类内按索引降序删（固件 swap-delete：删高索引不影响低索引）。
                    foreach (var grp in sel.GroupBy(s => s.cat))
                    {
                        foreach (var (cat, idx) in grp.OrderByDescending(s => s.idx))
                        {
                            step(T("msg.fpauth.key_delete_batch", ++i, total));
                            string? r = await AppServices.Pipe.KeyDeleteAsync(cat, idx);
                            if (r == IpcProtocol.Ok) n++;
                        }
                    }
                    return n;
                });
            _selected.Clear();
            StatusText.Text = T("keys.del_sel_done", done);
            await RefreshAsync();
        }
        finally { _busy = false; UpdateDelSelectedBtn(); }
    }

    private static string FromB64(string s)
    {
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        catch { return s; }
    }

    // ---- 添加 ----

    private async void OnAddOtpClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string name = OtpName.Text.Trim();
        string secret = OtpSecret.Text.Trim();
        if (name.Length == 0 || secret.Length == 0) { StatusText.Text = T("msg.keys.need_name_secret"); return; }
        _busy = true;
        UpdateDelSelectedBtn();
        try
        {
            string svcName = OtpService.Text.Trim();
            string sec = secret;
            string? r = FpAuthDialog.Run(Window.GetWindow(this),
                T("msg.fpauth.key_add", name),
                () => AppServices.Pipe.KeyAddOtpAsync(name, svcName, sec));
            if (r == IpcProtocol.Ok)
            {
                StatusText.Text = T("msg.keys.add_totp_ok", name);
                OtpName.Text = OtpService.Text = OtpSecret.Text = "";
                await RefreshAsync();
            }
            else StatusText.Text = T("msg.keys.add_totp_fail");
        }
        finally { _busy = false; UpdateDelSelectedBtn(); }
    }

    private async void OnAddApiClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string name = ApiName.Text.Trim();
        string value = ApiValue.Text;
        if (name.Length == 0 || value.Length == 0) { StatusText.Text = T("msg.keys.need_name_value"); return; }
        _busy = true;
        UpdateDelSelectedBtn();
        try
        {
            string val = value;
            string? r = FpAuthDialog.Run(Window.GetWindow(this),
                T("msg.fpauth.key_add", name),
                () => AppServices.Pipe.KeyAddApiAsync(name, val));
            if (r == IpcProtocol.Ok)
            {
                StatusText.Text = T("msg.keys.add_api_ok", name);
                ApiName.Text = ApiValue.Text = "";
                await RefreshAsync();
            }
            else StatusText.Text = T("msg.keys.add_fail");
        }
        finally { _busy = false; UpdateDelSelectedBtn(); }
    }

    private async void OnGenSshClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string name = SshName.Text.Trim();
        if (name.Length == 0) { StatusText.Text = T("msg.keys.need_name"); return; }
        _busy = true;
        UpdateDelSelectedBtn();
        try
        {
            string kn = name;
            string? r = FpAuthDialog.Run(Window.GetWindow(this),
                T("msg.fpauth.ssh_gen", name),
                () => AppServices.Pipe.KeySshGenAsync(kn));
            if (r is not null && r.StartsWith(IpcProtocol.Ok))
            {
                string[] pp = r.Split(IpcProtocol.Sep);
                string fp = pp.Length >= 3 ? FromB64(pp[2]) : "";
                if (pp.Length >= 2)
                {
                    try { Clipboard.SetText(FromB64(pp[1])); } catch { }
                }
                StatusText.Text = T("msg.keys.gen_ok", name, fp);
                SshName.Text = "";
                await RefreshAsync();
            }
            else StatusText.Text = T("msg.keys.gen_fail");
        }
        finally { _busy = false; UpdateDelSelectedBtn(); }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
}
