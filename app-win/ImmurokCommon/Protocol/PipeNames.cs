namespace ImmurokCommon.Protocol;

/// <summary>命名管道名称。</summary>
public static class PipeNames
{
    /// <summary>Client ↔ Service：文本协议（复用 macOS PAMSocketServer 协议）。</summary>
    public const string ClientService = "immurok";
    public const string ClientServiceFull = @"\\.\pipe\immurok";

    /// <summary>Service → Credential Provider：原始 UTF-16 用户名/密码。</summary>
    public const string CredentialProvider = "ImmurokCredentialProvider";
    public const string CredentialProviderFull = @"\\.\pipe\ImmurokCredentialProvider";
}
