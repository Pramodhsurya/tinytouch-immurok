using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;

namespace ImmurokClient;

public partial class App : Application
{
    private static Mutex? _mutex;
    private TaskbarIcon? _tray;
    private MainWindow? _window;
    private bool _forceExit;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "immurok-client-singleton", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);

        // 多语言：按已保存的语言初始化，未设置则默认英语（与 macOS 支持语言一致）。
        ImmurokClient.Localization.Loc.Instance.SetLanguage(
            ImmurokClient.Services.ClientSettings.Language ?? ImmurokClient.Localization.Loc.DefaultLanguage);

        // 托盘常驻：关闭主窗口只是隐藏到托盘，退出走托盘菜单。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _window = new MainWindow();
        _window.Closing += (_, args) =>
        {
            if (!_forceExit)
            {
                args.Cancel = true;
                _window.Hide();
            }
        };

        _tray = new TaskbarIcon
        {
            ToolTipText = "immurok",
            // 必须指向 .ico（H.NotifyIcon 要把流转成 System.Drawing.Icon，PNG 会抛 ArgumentException）
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/immurok.ico")),
        };
        var menu = new ContextMenu();
        var openItem = new MenuItem { Header = Localization.Loc.Instance.T("tray.open") };
        openItem.Click += (_, _) => ShowWindow();
        var exitItem = new MenuItem { Header = Localization.Loc.Instance.T("tray.exit") };
        exitItem.Click += (_, _) =>
        {
            _forceExit = true;
            _tray?.Dispose();
            Shutdown();
        };
        menu.Items.Add(openItem);
        menu.Items.Add(exitItem);
        _tray.ContextMenu = menu;
        _tray.TrayMouseDoubleClick += (_, _) => ShowWindow();
        _tray.ForceCreate();

        _window.Show();
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
