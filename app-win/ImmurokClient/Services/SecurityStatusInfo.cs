using System.Collections.Generic;
using System.Linq;

namespace ImmurokClient.Services;

/// <summary>
/// 服务端 <c>SECURITY:STATUS</c>（<c>OK:cp_pipe=…;service_pipe=…;caller_check=…;data_acl=…;pairing_scope=…;owner=…</c>）
/// 的解析与判定。横幅和状态页共用：每一项给出 ok / 标签 / 附注 / 横幅文案的 key。
/// 这只是给人看的健康提示；放行永远来自设备上的触摸，不来自这里的绿色。
/// </summary>
public sealed class SecurityStatusInfo
{
    public sealed record Item(string Key, bool Ok, string LabelKey, string? HintKey, string? BannerKey);

    public IReadOnlyDictionary<string, string> Raw { get; }
    public IReadOnlyList<Item> Items { get; }
    public bool AllOk => Items.All(i => i.Ok);
    public Item? FirstProblem => Items.FirstOrDefault(i => !i.Ok);

    private SecurityStatusInfo(Dictionary<string, string> raw)
    {
        Raw = raw;
        string V(string k) => raw.TryGetValue(k, out var v) ? v : "";

        string cp = V("cp_pipe");
        string pipe = V("service_pipe");
        string caller = V("caller_check");
        string acl = V("data_acl");
        string scope = V("pairing_scope");
        string owner = V("owner");

        Items = new List<Item>
        {
            new("cp_pipe", cp != "squatted", "security.status.cp",
                cp == "unknown" ? "security.status.cp_unknown" : null,
                cp == "squatted" ? "security.banner.cp_squatted" : null),
            new("service_pipe", pipe != "contended", "security.status.pipe", null,
                pipe == "contended" ? "security.banner.pipe_contended" : null),
            new("caller_check", caller != "off", "security.status.caller",
                caller == "path-only" ? "security.status.caller_pathonly" : null,
                caller == "off" ? "security.banner.caller_off" : null),
            new("data_acl", acl != "loose", "security.status.acl", null,
                acl == "loose" ? "security.banner.acl_loose" : null),
            new("pairing_scope", scope is "user" or "none", "security.status.pairing", null,
                scope == "machine" ? "security.banner.pairing_machine"
                : scope == "unreadable" ? "security.banner.pairing_unreadable" : null),
            new("owner", owner == "set", "security.status.owner", null,
                owner == "unset" ? "security.banner.owner_unset" : null),
        };
    }

    /// <summary>解析应答；服务未运行 / 老版本服务（UNKNOWN_COMMAND）返回 null。</summary>
    public static SecurityStatusInfo? Parse(string? response)
    {
        if (response is null || !response.StartsWith("OK:", System.StringComparison.Ordinal)) return null;
        var raw = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (string kv in response[3..].Split(';', System.StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq > 0) raw[kv[..eq]] = kv[(eq + 1)..];
        }
        return raw.Count == 0 ? null : new SecurityStatusInfo(raw);
    }
}
