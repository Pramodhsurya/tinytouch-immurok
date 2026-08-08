using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImmurokService.Platform;

/// <summary>
/// 「指纹注入信号」：非锁屏态收到一次签名指纹匹配时置位，供客户端（用户会话）轮询取用（一次性消费）。
///
/// 服务端在 Session 0，不能直接对用户会话的前台应用做 UI Automation / 模拟输入，
/// 所以只负责「记录发生了一次可用于注入的指纹」，真正的注入由客户端 INJECT:POLL 拉取后自行完成。
/// </summary>
public sealed class FpInjectionSignal
{
    private readonly object _lock = new();
    private DateTime _at = DateTime.MinValue;
    private ushort _pageId;
    private bool _pending;

    // 长轮询唤醒：Signal 时完成当前 TCS 并换上新的，让挂起的 WaitForSignalAsync 立即返回。
    private volatile TaskCompletionSource<bool> _wake =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // 信号有效期：超过即视为过期丢弃，避免久放的旧信号被误取用。
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(3);

    /// <summary>置一个待取用的注入信号（非锁屏态指纹匹配时调用）。</summary>
    public void Signal(ushort pageId)
    {
        lock (_lock)
        {
            _at = DateTime.UtcNow;
            _pageId = pageId;
            _pending = true;
        }
        // 唤醒当前挂起的等待者，并换上新 TCS 供下一次等待。
        Interlocked.Exchange(ref _wake,
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously))
            .TrySetResult(true);
    }

    /// <summary>
    /// 长轮询：等到有新鲜信号或超时才返回，返回后由调用方 Consume 取用。
    /// true=期间被信号唤醒；false=超时。取消（停服）时抛 OperationCanceledException。
    /// </summary>
    public async Task<bool> WaitForSignalAsync(TimeSpan timeout, CancellationToken ct)
    {
        // 先抓住当前唤醒任务，再检查是否已有新鲜信号——此顺序保证不漏掉这之间到来的 Signal：
        // 若 Signal 在检查前发生，_pending 已置位会被下面命中；若在检查后发生，会完成我们已捕获的 waiter。
        Task waiter = _wake.Task;
        lock (_lock)
        {
            if (_pending && (DateTime.UtcNow - _at) <= Ttl) return true;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task delay = Task.Delay(timeout, linked.Token);
        Task done = await Task.WhenAny(waiter, delay).ConfigureAwait(false);
        linked.Cancel(); // 取消尚未完成的那个（命中信号时即取消 delay 定时器）
        if (done == delay)
        {
            ct.ThrowIfCancellationRequested(); // 区分真超时 vs 停服取消
            return false;
        }
        return true;
    }

    /// <summary>取用并清除一个「新鲜」信号；无或已过期返回 false。</summary>
    public bool Consume(out ushort pageId)
    {
        lock (_lock)
        {
            bool fresh = _pending && (DateTime.UtcNow - _at) <= Ttl;
            _pending = false; // 无论新鲜与否都清掉（过期信号一并作废）
            pageId = fresh ? _pageId : (ushort)0;
            return fresh;
        }
    }
}
