using HotspotService.Infrastructure;
using HotspotService.Models;

namespace HotspotService.Services;

public sealed class HotspotGuardRuntimeState : ObservableObject
{
    private bool _guardEnabled;
    private GuardTargetState _guardTarget = GuardTargetState.On;
    private HotspotActualState _lastKnownHotspotState = HotspotActualState.Unknown;
    private int _connectedClientCount;
    private int _maxClientCount;
    private IReadOnlyList<HotspotClientInfo> _connectedClients = [];
    private DateTimeOffset? _lastCheckAt;
    private string? _lastError;

    public bool GuardEnabled
    {
        get => _guardEnabled;
        private set => SetProperty(ref _guardEnabled, value);
    }

    public GuardTargetState GuardTarget
    {
        get => _guardTarget;
        private set => SetProperty(ref _guardTarget, value);
    }

    public HotspotActualState LastKnownHotspotState
    {
        get => _lastKnownHotspotState;
        private set => SetProperty(ref _lastKnownHotspotState, value);
    }

    public int ConnectedClientCount
    {
        get => _connectedClientCount;
        private set => SetProperty(ref _connectedClientCount, value);
    }

    public int MaxClientCount
    {
        get => _maxClientCount;
        private set => SetProperty(ref _maxClientCount, value);
    }

    public IReadOnlyList<HotspotClientInfo> ConnectedClients
    {
        get => _connectedClients;
        private set => SetProperty(ref _connectedClients, value);
    }

    public DateTimeOffset? LastCheckAt
    {
        get => _lastCheckAt;
        private set => SetProperty(ref _lastCheckAt, value);
    }

    public string? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    public bool SetGuardEnabled(bool value)
    {
        var changed = GuardEnabled != value;
        GuardEnabled = value;
        return changed;
    }

    public bool SetGuardTarget(GuardTargetState value)
    {
        var changed = GuardTarget != value;
        GuardTarget = value;
        return changed;
    }

    public bool SetLastKnownHotspotState(HotspotActualState value)
    {
        var changed = LastKnownHotspotState != value;
        LastKnownHotspotState = value;
        return changed;
    }

    public bool SetConnectedClientCount(int value)
    {
        var changed = ConnectedClientCount != value;
        ConnectedClientCount = value;
        return changed;
    }

    public bool SetMaxClientCount(int value)
    {
        var changed = MaxClientCount != value;
        MaxClientCount = value;
        return changed;
    }

    public bool SetConnectedClients(IReadOnlyList<HotspotClientInfo>? value)
    {
        var normalized = value ?? Array.Empty<HotspotClientInfo>();
        if (ConnectedClients.Count == normalized.Count
            && ConnectedClients.Select(x => x.MacAddress).SequenceEqual(normalized.Select(x => x.MacAddress)))
        {
            return false;
        }

        ConnectedClients = normalized;
        return true;
    }

    public void SetLastCheckAt(DateTimeOffset? value)
    {
        LastCheckAt = value;
    }

    public void SetLastError(string? value)
    {
        LastError = value;
    }
}
