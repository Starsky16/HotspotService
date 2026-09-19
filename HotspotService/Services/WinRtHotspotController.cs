using HotspotService.Models;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace HotspotService.Services;

public sealed class WinRtHotspotController : IHotspotController
{
    private static readonly TimeSpan RestartTransitionDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 「当前连接档案 + 热点能力」快照的缓存时长。两者都是开销较大的 WinRT 探测调用，
    /// 而守护巡检每 10 秒就要读一次状态，逐次重新探测会持续产生系统侧对象；
    /// 缓存后调用频次降到约 1/6，连接档案变化最多滞后该时长被识别，
    /// 期间任何 WinRT 调用失败都会立刻失效缓存（见 <see cref="InvalidateManager"/>）。
    /// </summary>
    private static readonly TimeSpan SnapshotCacheDuration = TimeSpan.FromSeconds(60);

    private static readonly object ManagerGate = new();
    private static NetworkOperatorTetheringManager? _cachedManager;
    private static string? _cachedManagerKey;
    private static ConnectionProfile? _cachedProfile;
    private static HotspotSupportState _cachedSupport = HotspotSupportState.Unknown;
    private static long _snapshotTakenAt;
    private static bool _hasSnapshot;

    public Task<HotspotActualState> GetStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = GetManager();
        try
        {
            return Task.FromResult(MapState(manager.TetheringOperationalState));
        }
        catch
        {
            InvalidateManager();
            throw;
        }
    }

    public async Task SetStateAsync(GuardTargetState target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = GetManager();
        try
        {
            var result = target == GuardTargetState.On
                ? await manager.StartTetheringAsync()
                : await manager.StopTetheringAsync();

            if (result.Status != TetheringOperationStatus.Success)
            {
                throw new InvalidOperationException($"移动热点操作失败：{result.Status}。");
            }
        }
        finally
        {
            // 启停后连接档案可能变化，下次调用重新探测并重建管理对象。
            InvalidateManager();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = GetManager();
        try
        {
            var stop = await manager.StopTetheringAsync();
            if (stop.Status != TetheringOperationStatus.Success)
            {
                throw new InvalidOperationException($"移动热点停止失败：{stop.Status}。");
            }

            // 等待热点完全停止，避免紧接的启动操作被系统判定为“操作进行中”而失败。
            await Task.Delay(RestartTransitionDelay, cancellationToken);

            var start = await manager.StartTetheringAsync();
            if (start.Status != TetheringOperationStatus.Success)
            {
                throw new InvalidOperationException($"移动热点启动失败：{start.Status}。");
            }
        }
        finally
        {
            // 启停后连接档案可能变化，下次调用重新探测并重建管理对象。
            InvalidateManager();
        }
    }

    public Task<int> GetConnectedClientCountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = GetManager();
        try
        {
            var reported = (int)manager.ClientCount;

            // WinRT 的 ClientCount 在部分驱动/会话下会滞后，甚至长期为 0
            // （网卡复位后由守护重启热点、客户端重新接入时尤其明显），
            // 用实际枚举到的客户端数量交叉校验并取较大值，避免界面一直显示 0。
            var enumerated = CountTetheringClients(manager);
            return Task.FromResult(Math.Max(reported, enumerated));
        }
        catch
        {
            InvalidateManager();
            throw;
        }
    }

    /// <summary>枚举当前连接的热点客户端数量，失败时返回 0（以 ClientCount 为准）。</summary>
    private static int CountTetheringClients(NetworkOperatorTetheringManager manager)
    {
        try
        {
            return manager.GetTetheringClients().Count;
        }
        catch
        {
            return 0;
        }
    }

    public Task<int> GetMaxClientCountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = GetManager();
        try
        {
            return Task.FromResult((int)manager.MaxClientCount);
        }
        catch
        {
            InvalidateManager();
            throw;
        }
    }

    public Task<IReadOnlyList<HotspotClientInfo>> GetConnectedClientsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = GetManager();
        try
        {
            var clients = manager.GetTetheringClients();

            var result = new List<HotspotClientInfo>(clients.Count);
            foreach (var client in clients)
            {
                var hostNames = client.HostNames?
                    .Select(x => x.RawName ?? string.Empty)
                    .Where(x => x.Length > 0)
                    .ToArray() ?? [];
                result.Add(new HotspotClientInfo(client.MacAddress ?? string.Empty, hostNames));
            }

            return Task.FromResult<IReadOnlyList<HotspotClientInfo>>(result);
        }
        catch
        {
            InvalidateManager();
            throw;
        }
    }

    public Task<HotspotSupportState> GetTetheringSupportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(GetSnapshot().Support);
    }

    /// <summary>
    /// 取「当前连接档案 + 热点能力」快照：命中缓存直接返回，否则重新探测一次。
    /// 同时校正管理对象缓存：连接档案未变则沿用，变了或不支持则丢弃。
    /// </summary>
    private static (ConnectionProfile? Profile, HotspotSupportState Support) GetSnapshot()
    {
        lock (ManagerGate)
        {
            if (_hasSnapshot
                && Environment.TickCount64 - _snapshotTakenAt < SnapshotCacheDuration.TotalMilliseconds)
            {
                return (_cachedProfile, _cachedSupport);
            }

            var (profile, support) = ProbeSnapshot();
            var key = profile is null ? null : BuildProfileKey(profile);
            if (key is null || support != HotspotSupportState.Supported || key != _cachedManagerKey)
            {
                // 连接档案变化、设备不支持或探测失败：丢弃旧管理对象，下次按需重建。
                _cachedManager = null;
            }

            _cachedProfile = profile;
            _cachedSupport = support;
            _cachedManagerKey = key;
            _snapshotTakenAt = Environment.TickCount64;
            _hasSnapshot = true;
            return (profile, support);
        }
    }

    /// <summary>
    /// 实际执行一次 WinRT 探测。无 Wi-Fi 网卡时判定为不支持；
    /// 其余探测失败一律降级为“无法判断”，不抛异常，避免误报不支持导致功能被跳过。
    /// </summary>
    private static (ConnectionProfile? Profile, HotspotSupportState Support) ProbeSnapshot()
    {
        // 无 Wi-Fi 网卡时 Windows 无法开启移动热点，直接判定为不支持。
        if (!HasWiFiAdapter())
        {
            return (null, HotspotSupportState.NotSupported);
        }

        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                // 有 Wi-Fi 网卡但暂无可用连接档案：按“无法判断”处理，避免误报。
                return (null, HotspotSupportState.Unknown);
            }

            var capability = NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(profile);
            return (profile, capability == TetheringCapability.Enabled
                ? HotspotSupportState.Supported
                : HotspotSupportState.NotSupported);
        }
        catch
        {
            return (null, HotspotSupportState.Unknown);
        }
    }

    private static bool HasWiFiAdapter()
    {
        try
        {
            foreach (var networkInterface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                {
                    return true;
                }
            }
        }
        catch
        {
            // 查询失败时不在此处下结论，交由后续能力探测决定。
        }

        return false;
    }

    private static string? BuildProfileKey(ConnectionProfile profile)
    {
        try
        {
            return $"{profile.ProfileName}|{profile.NetworkAdapter?.NetworkAdapterId}";
        }
        catch
        {
            // 读不出档案标识时退化为“每次重新创建管理对象”，不影响正确性。
            return null;
        }
    }

    /// <summary>
    /// 取 WinRT 热点管理对象：连接档案未变化时复用已创建实例。
    /// 该类型不实现 IDisposable、只能等终结器回收，巡检每轮都新建会持续产生系统侧对象。
    /// </summary>
    private static NetworkOperatorTetheringManager GetManager()
    {
        var (profile, support) = GetSnapshot();

        if (profile is null)
        {
            throw new InvalidOperationException(support == HotspotSupportState.NotSupported
                ? "设备不支持移动热点共享。"
                : "未找到可用于共享网络的当前连接。");
        }

        if (support != HotspotSupportState.Supported)
        {
            throw new InvalidOperationException($"当前网络环境不支持热点共享：{support}。");
        }

        lock (ManagerGate)
        {
            return _cachedManager ??= NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
        }
    }

    /// <summary>
    /// 丢弃缓存的快照与管理对象，使下次调用重新探测连接档案并重建（管理对象可能因驱动复位失效）。
    /// </summary>
    private static void InvalidateManager()
    {
        lock (ManagerGate)
        {
            _cachedManager = null;
            _cachedManagerKey = null;
            _cachedProfile = null;
            _cachedSupport = HotspotSupportState.Unknown;
            _hasSnapshot = false;
        }
    }

    private static HotspotActualState MapState(TetheringOperationalState state)
    {
        return state switch
        {
            TetheringOperationalState.On => HotspotActualState.On,
            TetheringOperationalState.Off => HotspotActualState.Off,
            TetheringOperationalState.InTransition => HotspotActualState.Transitioning,
            _ => HotspotActualState.Unknown
        };
    }
}
