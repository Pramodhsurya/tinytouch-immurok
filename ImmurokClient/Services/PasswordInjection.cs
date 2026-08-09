using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImmurokClient.Services;

/// <summary>
/// 一条密码注入项：定位目标应用 + 模板锁定其密码框 + 绑定专属密码。
/// 属性对齐产品需求：应用 ID / 发行人 / 数字签名 / 密码框 ID / 专属密码。
///
/// 注意：专属密码不落明文——用 DPAPI（当前用户作用域）加密后存 base64。
/// 本迭代只负责「配置 + 捕获」；真正的注入引擎与指纹触发在下一步实现。
/// </summary>
public sealed class PasswordInjectionItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    // ---- 应用身份 ----
    // 匹配按强度择优：包族名 → 签名主体+文件名 → exe 路径（见 AppIdentity.Matches）。
    // 老配置只有 AppId/Publisher/Signature，反序列化后新字段为空，自动退回路径匹配；
    // 用户下次重新定位即升级到强身份。

    // exe 完整路径。最弱的一档：应用升级换目录（Electron/Squirrel、MSIX）就失配。
    public string AppId { get; set; } = "";
    // 发行人 = Authenticode 签名证书主体 CN（未签名为空）。签名匹配的主键。
    public string Publisher { get; set; } = "";
    // 数字签名 = 签名证书指纹 thumbprint（未签名为空）。仅作参考：证书续期会变，不当硬闸。
    public string Signature { get; set; } = "";
    // MSIX/Store 包族名，不含版本号。最稳的一档。
    public string PackageFamilyName { get; set; } = "";
    // Application User Model ID。目前只作展示/诊断，不参与匹配。
    public string Aumid { get; set; } = "";
    // PE VERSIONINFO 里的原始文件名，配合 Publisher 做签名匹配。
    public string OriginalFilename { get; set; } = "";
    // 磁盘上的 exe 文件名，OriginalFilename 缺失时的替补。
    public string ExeName { get; set; } = "";

    // 密码框模板：AutomationId 为主键，其余属性用于再定位时的兜底匹配
    public string FieldAutomationId { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string FieldControlType { get; set; } = "";
    public string FieldClassName { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public bool FieldWasPassword { get; set; }

    // 专属密码：DPAPI(CurrentUser) 加密后的 base64；磁盘上永不出现明文
    public string PasswordProtected { get; set; } = "";

    // 以下三个是算出来的，[JsonIgnore] 免得写进 injections.json ——
    // 它们没有 setter，反序列化本来就会忽略，留在文件里纯属噪音。
    [JsonIgnore]
    public bool HasPassword => !string.IsNullOrEmpty(PasswordProtected);

    [JsonIgnore]
    public bool HasTarget =>
        !string.IsNullOrEmpty(PackageFamilyName)
        || (!string.IsNullOrEmpty(Publisher) && !string.IsNullOrEmpty(ExeName))
        || !string.IsNullOrEmpty(AppId);

    /// <summary>这条项目实际能用上的最强身份——决定 UI 上怎么描述它的稳定性。</summary>
    [JsonIgnore]
    public AppMatchKind BestIdentity =>
        !string.IsNullOrEmpty(PackageFamilyName) ? AppMatchKind.Package
        : !string.IsNullOrEmpty(Publisher) ? AppMatchKind.Signature
        : !string.IsNullOrEmpty(AppId) ? AppMatchKind.Path
        : AppMatchKind.None;
}

/// <summary>
/// 注入项本地存储：%AppData%\immurok\injections.json。
/// 元数据明文，专属密码 DPAPI 加密。仅用户会话可读（对齐 macOS Keychain 的思路）。
/// </summary>
public static class PasswordInjectionStore
{
    private static readonly object _lock = new();

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "immurok");
    private static string FilePath => Path.Combine(Dir, "injections.json");

    // DPAPI 附加熵：绑定用途，降低跨用途解密风险
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("immurok.injection.v1");

    private static List<PasswordInjectionItem> _items = Load();

    /// <summary>当前所有注入项的快照（拷贝，调用方可安全遍历）。</summary>
    public static IReadOnlyList<PasswordInjectionItem> Items
    {
        get { lock (_lock) { return _items.ToList(); } }
    }

    private static List<PasswordInjectionItem> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<PasswordInjectionItem>>(File.ReadAllText(FilePath))
                       ?? new();
        }
        catch { /* 损坏文件忽略，按空处理 */ }
        return new();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 写失败忽略 */ }
    }

    public static void AddOrUpdate(PasswordInjectionItem item)
    {
        lock (_lock)
        {
            int i = _items.FindIndex(x => x.Id == item.Id);
            if (i >= 0) _items[i] = item; else _items.Add(item);
            Save();
        }
    }

    public static void Remove(string id)
    {
        lock (_lock)
        {
            _items.RemoveAll(x => x.Id == id);
            Save();
        }
    }

    /// <summary>设置专属密码（DPAPI 加密写入 item）。空串表示清除。</summary>
    public static void SetPassword(PasswordInjectionItem item, string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) { item.PasswordProtected = ""; return; }
        try
        {
            byte[] blob = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
            item.PasswordProtected = Convert.ToBase64String(blob);
        }
        catch
        {
            // DPAPI 失败也不阻断保存（否则整条注入项存不下来）；密码留空，之后可重设。
            item.PasswordProtected = "";
        }
    }

    /// <summary>解密专属密码（供后续注入引擎使用；也用于编辑时回填）。失败返回 null。</summary>
    public static string? GetPassword(PasswordInjectionItem item)
    {
        if (string.IsNullOrEmpty(item.PasswordProtected)) return null;
        try
        {
            byte[] blob = Convert.FromBase64String(item.PasswordProtected);
            byte[] raw = ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(raw);
        }
        catch { return null; }
    }
}
