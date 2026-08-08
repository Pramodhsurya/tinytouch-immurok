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

        // 全局异常兜底：记录到 %AppData%\immurok\client-error.log 并弹窗，避免静默崩溃。
        DispatcherUnhandledException += OnDispatcherUnhandledException;

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

        // 开机自启：首次运行默认开启；之后只在安装路径变化时校正，不覆盖用户的选择。
        // 注入必须由本进程（用户会话）完成，客户端不常驻就等于功能静默失效。
        Services.AutoStart.SyncOnStartup();

        // 指纹触发注入：后台轮询服务端信号，命中前台注入项时自动填入密码。
        Services.AppServices.Injection.Start();
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
        Services.AppServices.Injection.Stop();
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// 全局未处理异常兜底：写日志 + 弹窗给出可见错误，并标记已处理避免直接崩溃。
    /// 之前缺这个处理器，UI 里任何异常都会静默终止进程（如「点确定后没反应/没保存」）。
    /// </summary>
    private void OnDispatcherUnhandledException(
        object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
    {
        try
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "immurok");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "client-error.log"),
                $"[{DateTime.Now:O}] {args.Exception}\n\n");
        }
        catch { /* 记录失败就算了 */ }

        try
        {
            MessageBox.Show(args.Exception.ToString(), "immurok",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* 弹窗失败也不再抛 */ }

        args.Handled = true; // 不让未处理异常直接终止进程
    }
}
