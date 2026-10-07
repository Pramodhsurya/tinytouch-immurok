using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImmurokClient.Localization;
using ImmurokClient.Services;
using Wpf.Ui.Controls;

namespace ImmurokClient.Views;

/// <summary>
/// 「功能」页顶部的 IPC 加固健康横幅（设计稿 §4）。数据源是服务端 <c>SECURITY:STATUS</c>：
///   · 全部 ok → 绿色「IPC 加固已生效」，可用 × 收起为右上角一枚绿盾牌（收起状态存客户端设置）；
///   · 任一异常 → 橙色、不可关闭，文案指向具体项，「查看详情」跳状态页；
///   · 服务未运行 / 老版本服务不认这条命令 → 不显示（状态页已有「服务未运行」）。
/// 它是给人看的健康提示，不是安全判定的输入——放行永远来自设备上的触摸。
/// </summary>
public partial class SecurityBanner : UserControl
{
    private static string T(string k, params object[] a) => Loc.Instance.T(k, a);

    private static readonly Brush GreenBg = (Brush)new BrushConverter().ConvertFromString("#2230B050")!;
    private static readonly Brush GreenBorder = (Brush)new BrushConverter().ConvertFromString("#5530B050")!;
    private static readonly Brush GreenFg = (Brush)new BrushConverter().ConvertFromString("#FF30B050")!;
    private static readonly Brush OrangeBg = (Brush)new BrushConverter().ConvertFromString("#22E0A030")!;
    private static readonly Brush OrangeBorder = (Brush)new BrushConverter().ConvertFromString("#55E0A030")!;
    private static readonly Brush OrangeFg = (Brush)new BrushConverter().ConvertFromString("#FFE0A030")!;

    private SecurityStatusInfo? _last;

    public SecurityBanner()
    {
        InitializeComponent();
        Loc.Instance.LanguageChanged += () => Render();
    }

    public async Task RefreshAsync()
    {
        _last = SecurityStatusInfo.Parse(await AppServices.Pipe.SecurityStatusAsync());
        Render();
    }

    private void Render()
    {
        if (_last is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;

        if (_last.AllOk)
        {
            bool collapsed = ClientSettings.SecurityBannerCollapsed;
            Bar.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            Shield.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            Bar.Background = GreenBg;
            Bar.BorderBrush = GreenBorder;
            Icon.Symbol = SymbolRegular.ShieldCheckmark24;
            Icon.Foreground = GreenFg;
            Text.Text = T("security.banner.ok");
            DetailsButton.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Visible;
            return;
        }

        // 有异常：不可收起。
        Bar.Visibility = Visibility.Visible;
        Shield.Visibility = Visibility.Collapsed;
        Bar.Background = OrangeBg;
        Bar.BorderBrush = OrangeBorder;
        Icon.Symbol = SymbolRegular.ShieldError24;
        Icon.Foreground = OrangeFg;
        var p = _last.FirstProblem;
        Text.Text = p?.BannerKey is { } k ? T(k) : T("security.banner.details");
        DetailsButton.Content = T("security.banner.details");
        DetailsButton.Visibility = Visibility.Visible;
        CloseButton.Visibility = Visibility.Collapsed;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        ClientSettings.SecurityBannerCollapsed = true;
        Render();
    }

    private void OnShieldClick(object sender, RoutedEventArgs e)
    {
        ClientSettings.SecurityBannerCollapsed = false;
        Render();
    }

    private void OnDetailsClick(object sender, RoutedEventArgs e)
        => (Application.Current?.MainWindow as MainWindow)?.GoToStatus();
}
