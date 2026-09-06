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

        var changed = await TryRefreshClientStatsAsync(cancellationToken);
        changed |= await TryRefreshConnectedClientsAsync(cancellationToken);
        _lastClientInfoRefreshAt = now;
        return changed;
    }

    private async Task<bool> PerformSyncAsync(bool forceApply, CancellationToken cancellationToken)
    {
        var changed = false;
        var now = _timeProvider.GetUtcNow();

        try
        {
            var actualState = await _hotspotController.GetStateAsync(cancellationToken);
            changed |= _runtimeState.SetLastKnownHotspotState(actualState);
            _runtimeState.SetLastCheckAt(now);

            changed |= await TryRefreshClientInfoIfDueAsync(forceApply, now, cancellationToken);

            if (!forceApply && !_runtimeState.GuardEnabled)
            {
                _runtimeState.SetLastError(null);
                return changed;
            }

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
                    actualState = await _hotspotController.GetStateAsync(cancellationToken);
                    changed |= _runtimeState.SetLastKnownHotspotState(actualState);
                    _runtimeState.SetLastCheckAt(_timeProvider.GetUtcNow());
                }

                _runtimeState.SetLastError(null);
                _consecutiveFailureCount = 0;
            }
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

        if (policy.ClientCountThreshold > 0 && _runtimeState.ConnectedClientCount >= policy.ClientCountThreshold)
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

        return changed;
    }
}
