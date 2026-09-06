using System.Collections.Concurrent;

namespace ImmurokService.Ipc;

/// <summary>
/// <c>SECURITY:STATUS</c> 的汇总点（设计稿 §4）。各管道服务端在构造时把自己的 <c>Contended</c> 注册进来，
/// 避免 CommandHandlers ↔ PipeServer 的构造循环依赖。其余状态源（CP 对端校验、调用方策略、目录 ACL、
/// DPAPI 作用域、owner）由 CommandHandlers 直接读各单例。
/// 这些状态是给人看的健康提示，不是安全判定的输入（§9.2）。
/// </summary>
public sealed class SecurityStatus
{
    private readonly ConcurrentDictionary<string, Func<bool>> _pipes = new(StringComparer.Ordinal);

    public void RegisterPipe(string name, Func<bool> contended) => _pipes[name] = contended;

    public bool AnyPipeContended
    {
        get
        {
            foreach (var f in _pipes.Values)
            {
                try { if (f()) return true; } catch { /* ignore */ }
            }
            return false;
        }
    }
}
