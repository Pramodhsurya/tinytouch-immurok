using System;
using System.Collections.Generic;

namespace ImmurokClient.Services;

/// <summary>
/// 一个内置注入目标：只描述**应用身份**，不含密码，也不含界面模板。
///
/// 为什么不带密码框模板：注入时找密码框的顺序是「当前焦点 → AutomationId → 窗口内唯一密码框」
/// （见 <see cref="InjectionEngine"/>）。1Password 这类 Electron/Web UI 应用根本没有 AutomationId，
/// 实际总是靠第三条命中；而抓到的 ClassName 是 CSS Modules 生成的哈希（如
/// <c>textField--TextField_Sjq8x</c>），换个版本就变，写进内置预设只会过期。
/// 所以预设只锁身份，界面靠运行时判定。
/// </summary>
public sealed record InjectionPreset(
    string Key,
    string DisplayName,
    string PackageFamilyName,  // MSIX/Store 版身份
    string Aumid,
    string Publisher,          // 独立安装版身份（签名主体 CN）
    string ExeName)
{
    /// <summary>用预设生成一条新注入项。密码留空，由用户在编辑框里填。</summary>
    public PasswordInjectionItem ToItem() => new()
    {
        Name = DisplayName,
        PackageFamilyName = PackageFamilyName,
        Aumid = Aumid,
        Publisher = Publisher,
        ExeName = ExeName,
        // AppId（exe 路径）刻意留空：预设装在别人机器上，路径必然不同；
        // 而且路径是最弱的一档，有包族名/签名就轮不到它。
        AppId = "",
        Signature = "",        // 指纹随证书续期会变，不写死
        FieldWasPassword = true,
    };
}

/// <summary>
/// 随客户端一起发布的内置注入目标表。用户点一下就能加一条，只需再填自己的密码。
///
/// 每个条目同时带包族名与签名身份：1Password 在 Windows 上有 Microsoft Store（MSIX）
/// 和官网独立安装两种分发，前者有包族名，后者没有——两种身份都写上，
/// <see cref="AppIdentity.Matches"/> 会按强度自动挑能用的那个，一条预设覆盖两种装法。
/// </summary>
public static class InjectionPresets
{
    public static IReadOnlyList<InjectionPreset> All { get; } = new[]
    {
        new InjectionPreset(
            Key: "1password",
            DisplayName: "1Password",
            PackageFamilyName: "Agilebits.1Password_amwd9z03whsfe",
            Aumid: "Agilebits.1Password_amwd9z03whsfe!Agilebits.OnePassword",
            Publisher: "Agilebits",
            ExeName: "1Password.exe"),
    };

    /// <summary>本机是否已经有这个预设对应的注入项（按身份判，不看名字）。</summary>
    public static bool AlreadyAdded(InjectionPreset p)
    {
        foreach (var it in PasswordInjectionStore.Items)
        {
            if (p.PackageFamilyName.Length > 0 &&
                string.Equals(it.PackageFamilyName, p.PackageFamilyName, StringComparison.OrdinalIgnoreCase))
                return true;
            if (p.Publisher.Length > 0 &&
                string.Equals(it.Publisher, p.Publisher, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(it.ExeName, p.ExeName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
