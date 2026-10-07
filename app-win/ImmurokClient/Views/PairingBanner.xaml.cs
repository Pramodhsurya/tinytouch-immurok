using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ImmurokClient.Services;

namespace ImmurokClient.Views;

/// <summary>
/// 各功能页顶部的「本机未配对」横幅：只在未配对时出现，带一个直达配对入口的按钮。
/// 页面在自己的刷新流程里调 <see cref="RefreshAsync"/> 即可，无需关心配对状态怎么查。
/// </summary>
public partial class PairingBanner : UserControl
{
    public PairingBanner()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
        => Visibility = await PairingGuard.IsPairedAsync() ? Visibility.Collapsed : Visibility.Visible;

    private void OnGoClick(object sender, RoutedEventArgs e) => PairingGuard.NavigateToPairing();
}
