using HotspotService.Models;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace HotspotService.Services;

public sealed class WinRtHotspotController : IHotspotController
{
    private static readonly TimeSpan RestartTransitionDelay = TimeSpan.FromSeconds(1);

    public Task<HotspotActualState> GetStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = CreateManager();
        return Task.FromResult(MapState(manager.TetheringOperationalState));
    }

    public async Task SetStateAsync(GuardTargetState target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = CreateManager();
        var result = target == GuardTargetState.On
            ? await manager.StartTetheringAsync()
            : await manager.StopTetheringAsync();

        if (result.Status != TetheringOperationStatus.Success)
        {
            throw new InvalidOperationException($"移动热点操作失败：{result.Status}。");
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = CreateManager();
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

    public Task<int> GetConnectedClientCountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = CreateManager();
        return Task.FromResult((int)manager.ClientCount);
    }

    public Task<int> GetMaxClientCountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = CreateManager();
        return Task.FromResult((int)manager.MaxClientCount);
    }

    public Task<IReadOnlyList<HotspotClientInfo>> GetConnectedClientsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = CreateManager();
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

    public Task<HotspotSupportState> GetTetheringSupportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                // 没有可供共享的当前连接，无法判断是否具备热点能力。
                return Task.FromResult(HotspotSupportState.Unknown);
            }

            var capability = NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(profile);
            return Task.FromResult(capability == TetheringCapability.Enabled
                ? HotspotSupportState.Supported
                : HotspotSupportState.NotSupported);
        }
        catch
        {
            // 探测失败按“无法判断”处理，避免误报不支持导致功能被跳过。
            return Task.FromResult(HotspotSupportState.Unknown);
        }
    }

    private static NetworkOperatorTetheringManager CreateManager()
    {
        var profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile is null)
        {
            throw new InvalidOperationException("未找到可用于共享网络的当前连接。");
        }

        var capability = NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(profile);
        if (capability != TetheringCapability.Enabled)
        {
            throw new InvalidOperationException($"当前网络环境不支持热点共享：{capability}。");
        }

        return NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
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
