using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ImmurokService.Platform;

/// <summary>服务设置持久化（%ProgramData%\immurok\settings.json）。</summary>
public sealed class AppSettings
{
    private sealed class Data { public bool SshAgentEnabled { get; set; } }

    private readonly ILogger<AppSettings> _log;
    private readonly string _path;
    private Data _data = new();

    public AppSettings(ILogger<AppSettings> log)
    {
        _log = log;
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "immurok");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Load();
    }

    public bool SshAgentEnabled
    {
        get => _data.SshAgentEnabled;
        set { _data.SshAgentEnabled = value; Save(); }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new();
        }
        catch (Exception ex) { _log.LogWarning(ex, "读取设置失败"); }
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_data)); }
        catch (Exception ex) { _log.LogError(ex, "写入设置失败"); }
    }
}
