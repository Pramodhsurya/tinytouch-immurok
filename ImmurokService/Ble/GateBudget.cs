namespace ImmurokService.Ble;

/// <summary>
/// 设备指纹门的主机侧预算（设计稿 §3.6 过渡方案）。
///
/// 固件的 cooldown 是 rolling 的：每次免门通过都把 10s 窗口重新计时（hidkbd.c:5371），所以一次
/// 真实触摸之后，只要保持 &lt;10s 的节奏就能无限次读 OTP / API secret / 做签名，直到断连。
/// 固件侧「授权时声明 ttl / 次数」是待办；在那之前由 Service 兜底：记住上一次真实触摸的时间
/// 与之后放行的读秘密次数，超过预算就先发 AUTH_REQUEST——固件对它永远要求新触摸
/// （hidkbd.c:5908）——逼一次真实触摸再转发。Service 是 SYSTEM、在攻击者模型之外，这个计数
/// 在 Windows 上站得住（macOS 的 App 在会话内、站不住，所以固件待办不能因此一直拖）。
/// </summary>
public sealed class GateBudget
{
    /// <summary>一次真实触摸之后，读秘密命令免触摸的最长时间。</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    /// <summary>一次真实触摸之后，读秘密命令免触摸的最多次数。</summary>
    public const int MaxUses = 20;

    private readonly object _lock = new();
    private DateTime _touchedAt = DateTime.MinValue;
    private int _uses;

    /// <summary>设备上过了一次真实指纹门：重新开一张票。</summary>
    public void Touched()
    {
        lock (_lock)
        {
            _touchedAt = DateTime.UtcNow;
            _uses = 0;
        }
    }

    /// <summary>断连：作废（固件那边的 cooldown 也在断连时清零）。</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _touchedAt = DateTime.MinValue;
            _uses = 0;
        }
    }

    /// <summary>
    /// 消耗一次预算。票内返回 true 并计数；没有票 / 过期 / 用完返回 false，调用方要先逼一次真实触摸。
    /// </summary>
    public bool TryUse()
    {
        lock (_lock)
        {
            if (_touchedAt == DateTime.MinValue) return false;
            if (DateTime.UtcNow - _touchedAt > Ttl) return false;
            if (_uses >= MaxUses) return false;
            _uses++;
            return true;
        }
    }

    /// <summary>剩余次数与剩余时间（供状态展示）。</summary>
    public (int UsesLeft, TimeSpan TimeLeft) Remaining
    {
        get
        {
            lock (_lock)
            {
                if (_touchedAt == DateTime.MinValue) return (0, TimeSpan.Zero);
                TimeSpan left = Ttl - (DateTime.UtcNow - _touchedAt);
                if (left < TimeSpan.Zero) left = TimeSpan.Zero;
                return (Math.Max(0, MaxUses - _uses), left);
            }
        }
    }
}
