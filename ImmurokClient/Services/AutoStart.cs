using System;
using Microsoft.Win32;

namespace ImmurokClient.Services;

/// <summary>
/// 客户端开机自启：写 HKCU 的 Run 键。
///
/// 为什么用 HKCU\Run 而不是计划任务或 HKLM：
/// 密码注入必须跑在「用户会话 + 普通完整性级别」下——服务在 Session 0 够不着桌面，
/// 而 HKLM\Run 或以最高权限运行的计划任务会把客户端拉成管理员，反而破坏注入的身份前提。
/// Run 键天生就是这个身份，且不需要管理员权限即可写。
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "immurok";

    /// <summary>当前可执行文件路径（带引号，防止路径含空格被拆成参数）。</summary>
    private static string Command
    {
        get
        {
            string exe = Environment.ProcessPath ?? "";
            return string.IsNullOrEmpty(exe) ? "" : $"\"{exe}\"";
        }
    }

    /// <summary>是否已登记开机自启。</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
            }
            catch { return false; }
        }
    }

    /// <summary>开启/关闭开机自启。返回是否成功（写注册表失败时为 false，由调用方提示）。</summary>
    public static bool SetEnabled(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (on)
            {
                string cmd = Command;
                if (string.IsNullOrEmpty(cmd)) return false;
                key.SetValue(ValueName, cmd, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 启动时对齐：首次运行默认开启；已开启但登记的路径变了（换了安装目录/改名）则改写成当前路径。
    /// 用户手动关掉后不会被重新打开——首次默认只做一次，由 ClientSettings 记住。
    /// </summary>
    public static void SyncOnStartup()
    {
        try
        {
            if (!ClientSettings.AutoStartInitialized)
            {
                SetEnabled(true);
                ClientSettings.AutoStartInitialized = true;
                return;
            }

            if (!IsEnabled) return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(ValueName) is string cur &&
                !string.Equals(cur, Command, StringComparison.OrdinalIgnoreCase))
            {
                SetEnabled(true); // 路径已过期，改写成当前 exe
            }
        }
        catch { /* 自启只是便利功能，失败不影响主流程 */ }
    }
}
