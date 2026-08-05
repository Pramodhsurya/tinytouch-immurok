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
    private readonly CredentialStore _creds;
    private readonly SshAgentServer _sshAgent;
    private readonly CliServer _cli;
    private readonly AppSettings _settings;

    // 预授权：指纹先到、CP 还没就绪时置位，短时间内 CP 一上线即可解锁。
    private DateTime _preAuthUntil = DateTime.MinValue;
    private static readonly TimeSpan PreAuthWindow = TimeSpan.FromSeconds(10);

    public Worker(
        ILogger<Worker> log,
        BleManager ble,
        PipeServer pipe,
        SessionMonitor session,
        ScreenUnlocker unlocker,
        CredentialStore creds,
        SshAgentServer sshAgent,
        CliServer cli,
        AppSettings settings)
    {
        _log = log;
        _ble = ble;
        _pipe = pipe;
        _session = session;
        _unlocker = unlocker;
        _creds = creds;
        _sshAgent = sshAgent;
        _cli = cli;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("ImmurokService 启动");

        _ble.SignedFingerprintMatched += OnSignedFingerprintMatched;
        _ble.ConnectionChanged += OnConnectionChanged;
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
                _log.LogDebug("非锁屏态收到指纹匹配，置预授权窗口");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "处理指纹匹配失败");
        }
    }

    private void TryUnlock()
    {
        if (!_creds.TryRead(out string username, out string password) || string.IsNullOrEmpty(password))
        {
            _log.LogWarning("未配置登录密码，无法解锁");
            return;
        }
        // 若凭据里没存用户名，回退到当前活动会话用户。
        if (string.IsNullOrEmpty(username))
            username = _session.GetActiveConsoleUser() ?? Environment.UserName;

        bool ok = _unlocker.Unlock(username, password);
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
