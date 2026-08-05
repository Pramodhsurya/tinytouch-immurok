namespace ImmurokClient.Services;

/// <summary>
/// 极简服务定位器（骨架期用）。后续可替换为 DI 容器（Host.CreateApplicationBuilder）。
/// </summary>
public static class AppServices
{
    public static PipeClient Pipe { get; } = new();
}
