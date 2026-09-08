using HotspotService.Models;

namespace HotspotService.Services;

public sealed class HotspotGuardCoordinator
{
    private readonly IHotspotController _hotspotController;
    private readonly HotspotPluginSettingsStore _settingsStore;
    private readonly HotspotGuardRuntimeState _runtimeState;
    private readonly IGuardStatusNotifier _guardStatusNotifier;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private static readonly int MaxConsecutiveAutoRestarts = 3;
    private static readonly TimeSpan AutoRestartPauseDuration = TimeSpan.FromMinutes(30);
    private int _initialized;
    private int _consecutiveFailureCount;
    private int _consecutiveAutoRestarts;
    private DateTimeOffset? _transitioningSince;
    private DateTimeOffset? _lastRestartAt;
    private DateTimeOffset? _autoRestartPausedUntil;
    private DateTimeOffset? _lastClientInfoRefreshAt;

    public HotspotGuardCoordinator(
        IHotspotController hotspotController,
        HotspotPluginSettingsStore settingsStore,
        HotspotGuardRuntimeState runtimeState,
        IGuardStatusNotifier guardStatusNotifier,
        TimeProvider timeProvider)
    {
        _hotspotController = hotspotController;
        _settingsStore = settingsStore;
        _runtimeState = runtimeState;
        _guardStatusNotifier = guardStatusNotifier;
        _timeProvider = timeProvider;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        var targetChanged = _runtimeState.SetGuardTarget(_settingsStore.StartupTarget);
        var guardChanged = _runtimeState.SetGuardEnabled(_settingsStore.AutoStartGuard);
        var syncChanged = await RequestSyncCoreAsync(_runtimeState.GuardEnabled, cancellationToken);

        if (guardChanged || (_runtimeState.GuardEnabled && (targetChanged || syncChanged)))
        {
            _guardStatusNotifier.NotifyGuardStatusChanged();
        }
    }

    public async Task SetGuardEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var changed = _runtimeState.SetGuardEnabled(enabled);
        var syncChanged = false;

        if (enabled)
        {
            syncChanged = await RequestSyncCoreAsync(true, cancellationToken);
        }

        if (changed || syncChanged)
        {
            _guardStatusNotifier.NotifyGuardStatusChanged();
        }
    }

    public async Task SetGuardTargetAsync(
        GuardTargetState target,
        bool applyImmediately = false,
        CancellationToken cancellationToken = default)
    {
        var changed = _runtimeState.SetGuardTarget(target);
        var syncChanged = false;

        if (applyImmediately)
        {
            syncChanged = await RequestSyncCoreAsync(true, cancellationToken);
        }
        else if (changed && _runtimeState.GuardEnabled)
        {
            syncChanged = await RequestSyncCoreAsync(false, cancellationToken);
        }

        if (changed || syncChanged)
        {
            _guardStatusNotifier.NotifyGuardStatusChanged();
        }
    }

    public Task SetGuardTargetAsync(GuardTargetState target, CancellationToken cancellationToken)
    {
        return SetGuardTargetAsync(target, applyImmediately: false, cancellationToken: cancellationToken);
    }

    public async Task RunPeriodicCheckAsync(CancellationToken cancellationToken)
    {
        if (await RequestSyncCoreAsync(false, cancellationToken))
        {
            _guardStatusNotifier.NotifyGuardStatusChanged();
        }
    }

    public Task RequestSyncAsync(CancellationToken cancellationToken)
    {
        return RequestSyncAsync(forceApply: false, cancellationToken: cancellationToken);
    }

    public async Task RequestSyncAsync(bool forceApply = false, CancellationToken cancellationToken = default)
    {
        if (await RequestSyncCoreAsync(forceApply, cancellationToken))
        {
            _guardStatusNotifier.NotifyGuardStatusChanged();
        }
    }

    public async Task RestartHotspotAsync(CancellationToken cancellationToken = default)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var changed = await RestartHotspotCoreAsync(cancellationToken);
            if (changed)
            {
                _guardStatusNotifier.NotifyGuardStatusChanged();
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<bool> RequestSyncCoreAsync(bool forceApply, CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            return await PerformSyncAsync(forceApply, cancellationToken);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<bool> RefreshClientStatsAsync(CancellationToken cancellationToken)
    {
        var connected = await _hotspotController.GetConnectedClientCountAsync(cancellationToken);
        var max = await _hotspotController.GetMaxClientCountAsync(cancellationToken);
        var changed = _runtimeState.SetConnectedClientCount(connected);
        changed |= _runtimeState.SetMaxClientCount(max);
        return changed;
    }

    private async Task<bool> TryRefreshClientStatsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RefreshClientStatsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 客户端计数读取失败不判定主同步失败，仅降级保留上次计数。
            return false;
        }
    }

    private async Task<bool> TryRefreshConnectedClientsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var clients = await _hotspotController.GetConnectedClientsAsync(cancellationToken);
            return _runtimeState.SetConnectedClients(clients);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 设备列表读取失败不判定主同步失败，仅降级保留上次列表。
            return false;
        }
    }

    private async Task<bool> TryRefreshClientInfoAsync(CancellationToken cancellationToken)
    {
        var changed = await TryRefreshClientStatsAsync(cancellationToken);
        changed |= await TryRefreshConnectedClientsAsync(cancellationToken);
        _lastClientInfoRefreshAt = _timeProvider.GetUtcNow();
        return changed;
    }

    private async Task<bool> TryRefreshClientInfoIfDueAsync(
        bool forceApply,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settingsStore.ClientCountRefreshSeconds));
        var due = forceApply
            || _lastClientInfoRefreshAt is null
            || now - _lastClientInfoRefreshAt.Value >= interval;
        if (!due)
        {
            return false;
        }

        return await TryRefreshClientInfoAsync(cancellationToken);
    }

    /// <summary>
    /// 按热点实际状态刷新客户端信息：关闭时确定性清零（避免旧值误判/误导）；
    /// 切换中不读取（避免瞬时 0 覆盖真实数据）；运行中按配置间隔刷新，
    /// 刚启动或由非运行态切到运行态时可强制立即刷新一次。
    /// </summary>
    private async Task<bool> SyncClientInfoForStateAsync(
        HotspotActualState state,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        switch (state)
        {
            case HotspotActualState.Off:
                var offChanged = _runtimeState.SetConnectedClientCount(0);
                offChanged |= _runtimeState.SetConnectedClients(Array.Empty<HotspotClientInfo>());
                _lastClientInfoRefreshAt = _timeProvider.GetUtcNow();
                return offChanged;

            case HotspotActualState.Transitioning:
                return false;

            default:
                return await TryRefreshClientInfoIfDueAsync(forceRefresh, _timeProvider.GetUtcNow(), cancellationToken);
        }
    }

    private async Task<bool> PerformSyncAsync(bool forceApply, CancellationToken cancellationToken)
    {
        var changed = false;
        var now = _timeProvider.GetUtcNow();

        try
        {
            var support = await _hotspotController.GetTetheringSupportAsync(cancellationToken);
            changed |= _runtimeState.SetTetheringSupport(support);

            if (support == HotspotSupportState.NotSupported)
            {
                // 设备无法开启热点（如没有可用的 Wi-Fi 网卡）：不尝试任何启停或客户端读取，
                // 客户端信息清零，界面据此显示 “None”。
                _consecutiveFailureCount = 0;
                _transitioningSince = null;
                _runtimeState.SetLastCheckAt(_timeProvider.GetUtcNow());
                _runtimeState.SetLastError(null);
                changed |= _runtimeState.SetConnectedClientCount(0);
                changed |= _runtimeState.SetConnectedClients(Array.Empty<HotspotClientInfo>());
                return changed;
            }

            var previousState = _runtimeState.LastKnownHotspotState;
            var actualState = await _hotspotController.GetStateAsync(cancellationToken);
            changed |= _runtimeState.SetLastKnownHotspotState(actualState);
            _runtimeState.SetLastCheckAt(now);

            if (!forceApply && !_runtimeState.GuardEnabled)
            {
                // 守护关闭：仅做展示性刷新，不干预系统热点。
                changed |= await SyncClientInfoForStateAsync(actualState, forceRefresh: false, cancellationToken);
                _runtimeState.SetLastError(null);
                return changed;
            }

            var justStarted = false;
            if (actualState == HotspotActualState.Transitioning)
            {
                _transitioningSince ??= _timeProvider.GetUtcNow();
                _runtimeState.SetLastError(null);
            }
            else
            {
                _transitioningSince = null;

                var targetState = _runtimeState.GuardTarget.ToActualState();
                if (actualState != targetState)
                {
                    await _hotspotController.SetStateAsync(_runtimeState.GuardTarget, cancellationToken);
                    justStarted = targetState == HotspotActualState.On;
                    actualState = await _hotspotController.GetStateAsync(cancellationToken);
                    changed |= _runtimeState.SetLastKnownHotspotState(actualState);
                    _runtimeState.SetLastCheckAt(_timeProvider.GetUtcNow());
                }

                _runtimeState.SetLastError(null);
                _consecutiveFailureCount = 0;
            }

            // 热点刚被守护拉起，或刚从关闭/切换中切到运行态时强制刷新一次，
            // 让连接数尽快反映真实状态（不会被“间隔门控”拖延）。
            var transitionedToRunning = previousState != HotspotActualState.On && actualState == HotspotActualState.On;
            changed |= await SyncClientInfoForStateAsync(
                actualState,
                forceRefresh: forceApply || justStarted || transitionedToRunning,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _consecutiveFailureCount++;
            _runtimeState.SetLastCheckAt(_timeProvider.GetUtcNow());
            _runtimeState.SetLastError(ex.Message);
        }

        changed |= await TryRestartIfNeededAsync(cancellationToken);

        return changed;
    }

    private async Task<bool> TryRestartIfNeededAsync(CancellationToken cancellationToken)
    {
        var policy = _settingsStore.RestartPolicy;
        if (!_runtimeState.GuardEnabled || !policy.EnableAutoRestart)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        if (_autoRestartPausedUntil is not null && now < _autoRestartPausedUntil)
        {
            return false;
        }

        if (!ShouldRestart(policy))
        {
            _consecutiveAutoRestarts = 0;
            return false;
        }

        if (_lastRestartAt is not null &&
            now - _lastRestartAt.Value < TimeSpan.FromSeconds(Math.Max(0, policy.RestartCooldownSeconds)))
        {
            return false;
        }

        try
        {
            var changed = await RestartHotspotCoreAsync(cancellationToken);

            // 连续多次重启仍未解决问题（条件仍成立）时暂停自动重启，避免无限循环。
            _consecutiveAutoRestarts++;
            if (_consecutiveAutoRestarts >= MaxConsecutiveAutoRestarts)
            {
                _autoRestartPausedUntil = _timeProvider.GetUtcNow() + AutoRestartPauseDuration;
                _consecutiveAutoRestarts = 0;
            }

            return changed;
        }
        catch (Exception ex)
        {
            _lastRestartAt = _timeProvider.GetUtcNow();
            _runtimeState.SetLastCheckAt(_timeProvider.GetUtcNow());
            _runtimeState.SetLastError(ex.Message);
            return false;
        }
    }

    private bool ShouldRestart(HotspotRestartPolicySettings policy)
    {
        if (policy.ConsecutiveFailureThreshold > 0 && _consecutiveFailureCount >= policy.ConsecutiveFailureThreshold)
        {
            return true;
        }

        // 热点未运行（关闭/切换中）时连接数无意义且可能是残留旧值，
        // 不能作为重启依据，否则会出现在刚被守护拉起后又被“顶掉”的反复重启。
        if (policy.ClientCountThreshold > 0
            && _runtimeState.LastKnownHotspotState == HotspotActualState.On
            && _runtimeState.ConnectedClientCount >= policy.ClientCountThreshold)
        {
            return true;
        }

        if (policy.StuckTransitioningSeconds > 0 && _transitioningSince is not null &&
            _timeProvider.GetUtcNow() - _transitioningSince.Value >= TimeSpan.FromSeconds(policy.StuckTransitioningSeconds))
        {
            return true;
        }

        return false;
    }

    private async Task<bool> RestartHotspotCoreAsync(CancellationToken cancellationToken)
    {
        await _hotspotController.RestartAsync(cancellationToken);

        _lastRestartAt = _timeProvider.GetUtcNow();
        _consecutiveFailureCount = 0;
        _transitioningSince = null;

        var actualState = await _hotspotController.GetStateAsync(cancellationToken);
        var changed = _runtimeState.SetLastKnownHotspotState(actualState);
        _runtimeState.SetLastCheckAt(_timeProvider.GetUtcNow());
        _runtimeState.SetLastError(null);

        // 重启会断开全部设备再重新接入，立即强制刷新一次，
        // 避免间隔门控让界面继续显示重启前的旧连接数。
        if (actualState == HotspotActualState.On)
        {
            changed |= await TryRefreshClientInfoAsync(cancellationToken);
        }

        return changed;
    }
}
