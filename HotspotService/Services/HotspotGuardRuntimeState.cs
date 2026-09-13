using HotspotService.Infrastructure;
using HotspotService.Models;

namespace HotspotService.Services;

public sealed class HotspotGuardRuntimeState : ObservableObject
{
    private bool _guardEnabled;
    private GuardTargetState _guardTarget = GuardTargetState.On;
    private HotspotActualState _lastKnownHotspotState = HotspotActualState.Unknown;
    private HotspotSupportState _tetheringSupport;
    private int _connectedClientCount;
    private int _maxClientCount;
    private IReadOnlyList<HotspotClientInfo> _connectedClients = [];
    private DateTimeOffset? _lastCheckAt;
    private string? _lastError;
    private NetworkThroughputReadout _hotspotThroughput = NetworkThroughputReadout.NotSampling(NetworkTrafficTarget.Hotspot);
    private NetworkThroughputReadout _internetThroughput = NetworkThroughputReadout.NotSampling(NetworkTrafficTarget.Internet);
    private DateTimeOffset? _lastThroughputSampleAt;
    private bool _shortcutSourceAvailable;
    private string? _shortcutSourceMessage;
    private DateTimeOffset? _lastShortcutTriggeredAt;
    private string? _lastShortcutError;

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

    /// <summary>
    /// 当前设备是否支持开启移动热点。
    /// </summary>
    public HotspotSupportState TetheringSupport
    {
        get => _tetheringSupport;
        private set => SetProperty(ref _tetheringSupport, value);
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

    /// <summary>
    /// 热点网卡的最新网速读数。采样关闭时为未采样形态，找不到热点网卡时为失败形态。
    /// </summary>
    public NetworkThroughputReadout HotspotThroughput
    {
        get => _hotspotThroughput;
        private set => SetProperty(ref _hotspotThroughput, value);
    }

    /// <summary>
    /// 外网网卡的最新网速读数。
    /// </summary>
    public NetworkThroughputReadout InternetThroughput
    {
        get => _internetThroughput;
        private set => SetProperty(ref _internetThroughput, value);
    }

    /// <summary>
    /// 最近一次网速采样时间（只要有一个目标完成采样就会更新）。
    /// </summary>
    public DateTimeOffset? LastThroughputSampleAt
    {
        get => _lastThroughputSampleAt;
        private set => SetProperty(ref _lastThroughputSampleAt, value);
    }

    /// <summary>
    /// 是否已连接到 KeyboardCapture 插件（快捷键重启热点可用）。
    /// </summary>
    public bool ShortcutSourceAvailable
    {
        get => _shortcutSourceAvailable;
        private set => SetProperty(ref _shortcutSourceAvailable, value);
    }

    /// <summary>
    /// 快捷键来源的提示信息：未检测到 KeyboardCapture 或连接失败时的原因，连接正常时为 null。
    /// </summary>
    public string? ShortcutSourceMessage
    {
        get => _shortcutSourceMessage;
        private set => SetProperty(ref _shortcutSourceMessage, value);
    }

    /// <summary>
    /// 最近一次快捷键触发热点重启的时间。
    /// </summary>
    public DateTimeOffset? LastShortcutTriggeredAt
    {
        get => _lastShortcutTriggeredAt;
        private set => SetProperty(ref _lastShortcutTriggeredAt, value);
    }

    /// <summary>
    /// 最近一次快捷键重启失败的原因，成功时为 null。
    /// </summary>
    public string? LastShortcutError
    {
        get => _lastShortcutError;
        private set => SetProperty(ref _lastShortcutError, value);
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

    public bool SetTetheringSupport(HotspotSupportState value)
    {
        var changed = TetheringSupport != value;
        TetheringSupport = value;
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

    /// <summary>
    /// 按读数里的目标写入对应网速字段，并同步记录采样时间；返回读数是否发生变化。
    /// </summary>
    public bool SetThroughput(NetworkThroughputReadout value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var changed = value.Target switch
        {
            NetworkTrafficTarget.Hotspot => SetHotspotThroughput(value),
            NetworkTrafficTarget.Internet => SetInternetThroughput(value),
            _ => false
        };

        if (value.SampledAt is { } sampledAt)
        {
            SetLastThroughputSampleAt(sampledAt);
        }

        return changed;
    }

    public bool SetHotspotThroughput(NetworkThroughputReadout value)
    {
        var changed = HotspotThroughput != value;
        HotspotThroughput = value;
        return changed;
    }

    public bool SetInternetThroughput(NetworkThroughputReadout value)
    {
        var changed = InternetThroughput != value;
        InternetThroughput = value;
        return changed;
    }

    public void SetLastThroughputSampleAt(DateTimeOffset? value)
    {
        LastThroughputSampleAt = value;
    }

    public void SetShortcutSourceAvailable(bool value)
    {
        ShortcutSourceAvailable = value;
    }

    public void SetShortcutSourceMessage(string? value)
    {
        ShortcutSourceMessage = value;
    }

    public void SetLastShortcutTriggeredAt(DateTimeOffset? value)
    {
        LastShortcutTriggeredAt = value;
    }

    public void SetLastShortcutError(string? value)
    {
        LastShortcutError = value;
    }
}
