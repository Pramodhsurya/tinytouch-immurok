using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ImmurokClient.Localization;
using ImmurokClient.Services;
using Wpf.Ui.Controls;

namespace ImmurokClient.Views;

/// <summary>
/// 指纹认证等待窗：凡是设备侧要过指纹门的操作，都用它把「请在设备上触摸指纹」显式化，
/// 并给出与设备端一致的 30s 倒计时（设备门超时也是 30s）。
///
/// <para>用法：<c>var r = FpAuthDialog.Run(owner, T("..."), () =&gt; AppServices.Pipe.XxxAsync());</c>
/// —— 弹窗期间执行该操作，操作返回即自动关窗并把结果交回调用方。</para>
///
/// <para>点「取消」会向 Service 发 CANCELGATE，设备立刻停止闪灯等待，
/// 而不是让用户对着一个还在闪的设备干等到超时。</para>
/// </summary>
public partial class FpAuthDialog : FluentWindow
{
    private static string T(string k, params object[] a) => Loc.Instance.T(k, a);

    /// <summary>与设备端指纹门一致的超时（秒）。</summary>
    private const int TimeoutSec = 30;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTime _deadline;
    private bool _done;      // 操作已返回
    private bool _cancelled; // 用户主动取消

    private FpAuthDialog(string action)
    {
        InitializeComponent();
        ActionText.Text = action;
        _deadline = DateTime.UtcNow.AddSeconds(TimeoutSec);
        SecondsText.Text = T("fpauth.remaining", TimeoutSec);
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// 弹出认证窗并执行 <paramref name="op"/>；操作返回后自动关窗。
    /// 返回操作结果；用户取消或超时返回 default。
    /// </summary>
    public static T? Run<T>(System.Windows.Window? owner, string action, Func<Task<T>> op)
        => RunSteps<T>(owner, action, _ => op());

    /// <summary>
    /// 多步版本：批量操作里每一项都要单独过一次指纹门，回调 <c>step(说明)</c>
    /// 换文案并把 30s 倒计时重新计起，避免第二项开始就显示成已超时。
    /// </summary>
    public static T? RunSteps<T>(System.Windows.Window? owner, string action, Func<Action<string>, Task<T>> op)
    {
        var dlg = new FpAuthDialog(action);
        if (owner is not null) dlg.Owner = owner;
        else dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

        T? result = default;
        dlg.Loaded += async (_, _) =>
        {
            dlg.StartPulse();
            dlg._timer.Start();
            try
            {
                result = await op(msg => dlg.Dispatcher.Invoke(() => dlg.NextStep(msg)));
            }
            catch (Exception) { result = default; }
            dlg._done = true;
            dlg.Close();
        };
        dlg.ShowDialog();
        return dlg._cancelled ? default : result;
    }

    /// <summary>进入下一个待认证的步骤：换说明文案 + 倒计时重新计起。</summary>
    private void NextStep(string action)
    {
        if (_cancelled) return;
        ActionText.Text = action;
        HeadText.Text = T("fpauth.head");
        FpGlyph.Opacity = 1.0;
        _deadline = DateTime.UtcNow.AddSeconds(TimeoutSec);
        Countdown.Value = 100;
        SecondsText.Text = T("fpauth.remaining", TimeoutSec);
        StartPulse();
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double left = (_deadline - DateTime.UtcNow).TotalSeconds;
        if (left <= 0)
        {
            // 设备端的门也在此时超时，操作会自行以失败收尾；这里只把界面切到超时态。
            _timer.Stop();
            Countdown.Value = 0;
            SecondsText.Text = T("fpauth.timeout");
            HeadText.Text = T("fpauth.timeout_head");
            StopPulse();
            FpGlyph.Opacity = 0.3;
            return;
        }
        Countdown.Value = left / TimeoutSec * 100;
        SecondsText.Text = T("fpauth.remaining", (int)Math.Ceiling(left));
    }

    private async void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (_done) { Close(); return; }
        // 先让设备停止等待，再关窗——否则设备会一直闪到 30s 超时。
        _cancelled = true;
        _timer.Stop();
        CloseBtn.IsEnabled = false;
        SecondsText.Text = T("fpauth.cancelling");
        await AppServices.Pipe.CancelGateAsync();
        Close();
    }

    // ---- 呼吸动画：提示「现在请触摸设备」----

    private void StartPulse()
    {
        var anim = new DoubleAnimation(0.25, 1.0, new Duration(TimeSpan.FromSeconds(0.9)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Pulse.BeginAnimation(OpacityProperty, anim);
    }

    private void StopPulse()
    {
        Pulse.BeginAnimation(OpacityProperty, null);
        Pulse.Opacity = 0;
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        base.OnClosed(e);
    }
}
