namespace ImmurokClient.Services;

/// <summary>
/// 极简服务定位器（骨架期用）。后续可替换为 DI 容器（Host.CreateApplicationBuilder）。
/// </summary>
public static class AppServices
{
    public static PipeClient Pipe { get; } = new();

    public static FirmwareUpdateService Firmware { get; } = new();

    /// <summary>指纹触发注入的后台监听（轮询服务端 INJECT:POLL）。</summary>
    public static InjectionListener Injection { get; } = new();
}
