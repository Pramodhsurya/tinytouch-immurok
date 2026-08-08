using ImmurokService.Ble;
using ImmurokService.Ipc;
using ImmurokService.Security;
using ImmurokService.Platform;
using ImmurokService.Ssh;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ImmurokService;

/// <summary>
/// 后台工作宿主：编排 BLE、管道服务端、锁屏解锁触发（对应 macOS AppDelegate 的初始化与业务分派）。
///
/// 核心闭环：收到并验签通过的签名指纹匹配（0x21）→ 若当前锁屏 → 取用户名+密码 →
/// 经 CP 管道推送触发解锁。
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _log;
    private readonly BleManager _ble;
    private readonly PipeServer _pipe;
    private readonly SessionMonitor _session;
    private readonly ScreenUnlocker _unlocker;
    private readonly ScreenLocker _locker;
    private readonly CredentialStore _creds;
    private readonly SshAgentServer _sshAgent;
    private readonly CliServer _cli;
    private readonly AppSettings _settings;
    private readonly FpInjectionSignal _injectionSignal;

    // 预授权：指纹先到、CP 还没就绪时置位，短时间内 CP 一上线即可解锁。
    private DateTime _preAuthUntil = DateTime.MinValue;
    private static readonly TimeSpan PreAuthWindow = TimeSpan.FromSeconds(10);

    // 最近一次指纹驱动的真实认证流（解锁成功等）。用于抑制 0x23 长按锁屏：
    // 固件在每次触摸上升沿之后固定 1.6s 就发 0x23，与指纹是否匹配无关，
    // 所以一次成功解锁后用户手指多停一会儿，会立刻收到锁屏请求把刚解开的屏幕又锁上。
    private DateTime _lastAuthFlow = DateTime.MinValue;
    private static readonly TimeSpan LockSuppressWindow = TimeSpan.FromSeconds(3);

    public Worker(
        ILogger<Worker> log,
        BleManager ble,
        PipeServer pipe,
        SessionMonitor session,
        ScreenUnlocker unlocker,
        ScreenLocker locker,
        CredentialStore creds,
        SshAgentServer sshAgent,
        CliServer cli,
        AppSettings settings,
        FpInjectionSignal injectionSignal)
    {
        _log = log;
        _ble = ble;
        _pipe = pipe;
        _session = session;
        _unlocker = unlocker;
        _locker = locker;
        _creds = creds;
        _sshAgent = sshAgent;
        _cli = cli;
        _settings = settings;
        _injectionSignal = injectionSignal;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("ImmurokService 启动");

        _ble.SignedFingerprintMatched += OnSignedFingerprintMatched;
        _ble.ConnectionChanged += OnConnectionChanged;
        _ble.LockRequested += OnLockRequested;
        _pipe.Start();
        _ble.StartScan();

        // SSH agent：若已启用则随服务启动。
        if (_settings.SshAgentEnabled)
            _sshAgent.Start();

        // imk CLI 服务：随服务常驻启动。
        _cli.Start();

        try
        {
            // 每 2 秒轮询：锁屏解锁主要由 0x21 事件驱动，但这里兜底处理
            //「先触摸指纹、随后才锁屏」的预授权窗口。
            int tick = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                bool locked = _session.IsLocked();

                if (locked && DateTime.UtcNow < _preAuthUntil)
                {
                    _log.LogInformation("预授权窗口内检测到锁屏，触发解锁");
                    _preAuthUntil = DateTime.MinValue;
                    TryUnlock();
                }

                if (++tick % 15 == 0) // ~30s 打一次心跳
                    _log.LogDebug("心跳: connected={Connected} locked={Locked}", _ble.IsConnected, locked);
            }
        }
        catch (OperationCanceledException) { /* 正常停止 */ }
        finally
        {
            _ble.SignedFingerprintMatched -= OnSignedFingerprintMatched;
            _ble.ConnectionChanged -= OnConnectionChanged;
            _ble.LockRequested -= OnLockRequested;
        }
    }

    private void OnConnectionChanged(bool connected)
    {
        // 连接后刷新 SSH 身份缓存（若 agent 在跑）。
        if (connected && _sshAgent.IsRunning)
            _ = _sshAgent.RefreshIdentitiesAsync();
    }

    private void OnSignedFingerprintMatched(ushort pageId)
    {
        try
        {
            if (_session.IsLocked())
            {
                TryUnlock();
            }
            else
            {
                // 未锁屏：置预授权窗口，供未来的 AUTH/权限场景或刚进入锁屏时使用。
                _preAuthUntil = DateTime.UtcNow + PreAuthWindow;
                // 同时置注入信号：客户端（用户会话）轮询 INJECT:POLL 拿到后，
                // 对当前前台应用做密码注入。是否真的注入由客户端按前台应用是否命中注入项决定。
                _injectionSignal.Signal(pageId);
                _log.LogDebug("非锁屏态收到指纹匹配，置预授权窗口 + 注入信号");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "处理指纹匹配失败");
        }
    }

    /// <summary>
    /// 处理设备的 0x23 长按锁屏请求。以下情况跳过（对齐 macOS/Linux 的判定）：
    ///   · 功能未开启（默认关）
    ///   · 屏幕已经是锁的
    ///   · 距上一次真实认证流不足 3s —— 固件每次触摸按住 1.6s 都会发 0x23，
    ///     不加这道闸，成功解锁后手指多停一会儿就会把屏幕立刻锁回去
    /// </summary>
    private void OnLockRequested()
    {
        try
        {
            if (!_settings.LockEnabled)
            {
                _log.LogInformation("锁屏请求忽略：功能未开启（功能页里打开「屏幕锁定」）");
                return;
            }
            if (_session.IsLocked())
            {
                _log.LogInformation("锁屏请求忽略：SessionMonitor 判定屏幕已锁");
                return;
            }
            if (DateTime.UtcNow - _lastAuthFlow < LockSuppressWindow)
            {
                _log.LogInformation("锁屏请求忽略：距上次认证仅 {Ms}ms（抑制窗口 {Win}ms）",
                    (int)(DateTime.UtcNow - _lastAuthFlow).TotalMilliseconds, (int)LockSuppressWindow.TotalMilliseconds);
                return;
            }

            // 锁屏会让此前那次 0x21 建立的预授权失效——不清掉的话，
            // 轮询循环下一拍就会拿它把刚锁上的屏幕解开。
            _preAuthUntil = DateTime.MinValue;
            _log.LogInformation("锁屏请求通过判定，开始锁屏");
            _locker.Lock();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "处理锁屏请求失败");
        }
    }

    private void TryUnlock()
    {
        // 功能开关：在这里统一拦，事件驱动与预授权窗口两条路径都覆盖到。
        if (!_settings.UnlockEnabled)
        {
            _log.LogDebug("指纹解锁已关闭，跳过");
            return;
        }

        if (!_creds.TryRead(out string username, out string password) || string.IsNullOrEmpty(password))
        {
            _log.LogWarning("未配置登录密码，无法解锁");
            return;
        }
        // 若凭据里没存用户名，回退到当前活动会话用户。
        if (string.IsNullOrEmpty(username))
            username = _session.GetActiveConsoleUser() ?? Environment.UserName;

        bool ok = _unlocker.Unlock(username, password);
        if (ok) _lastAuthFlow = DateTime.UtcNow; // 抑制紧随其后的 0x23 长按锁屏
        _log.LogInformation("解锁触发结果: {Ok}", ok);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("ImmurokService 停止中");
        await _cli.StopAsync();
        await _sshAgent.StopAsync();
        await _pipe.DisposeAsync();
        await _ble.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
