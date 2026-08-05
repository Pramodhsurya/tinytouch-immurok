using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImmurokClient.Views;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ImmurokClient;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 启动时按注册表权威判定系统深/浅色再应用。
        // （ApplicationThemeManager.ApplySystemTheme() 在启动早期偶发误判，导致首屏主题不对，
        //   切换一次才恢复——这里直接读 AppsUseLightTheme 规避。）
        ApplicationThemeManager.Apply(DetectSystemTheme(), WindowBackdropType.Mica, updateAccent: true);

        // 监听系统主题切换，实时更新应用主题、Mica 背景与标题栏图标。
        SystemThemeWatcher.Watch(this);
        ApplicationThemeManager.Changed += OnThemeChanged;

        UpdateAppIcon();
        RootNavigation.Navigate(typeof(DevicePage));
    }

    private void OnThemeChanged(ApplicationTheme currentTheme, Color accent) => UpdateAppIcon();

    /// <summary>logo 跟随主题：暗色用白色版，亮色用黑色版。同时更新标题栏与任务栏图标。</summary>
    private void UpdateAppIcon()
    {
        bool dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        string asset = dark ? "immurok-white.png" : "immurok.png";
        var src = new BitmapImage(new Uri($"pack://application:,,,/Assets/{asset}"));
        AppIcon.Source = src; // 标题栏左上角
        Icon = src;           // 任务栏 / Alt-Tab（Window.Icon，运行时可动态切换）
    }

    /// <summary>读注册表判定系统主题（AppsUseLightTheme：0=深色，1=浅色）。</summary>
    private static ApplicationTheme DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
                return v == 0 ? ApplicationTheme.Dark : ApplicationTheme.Light;
        }
        catch { /* 读不到就按暗色兜底 */ }
        return ApplicationTheme.Dark;
    }
}
