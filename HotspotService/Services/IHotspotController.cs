using HotspotService.Models;

namespace HotspotService.Services;

public interface IHotspotController
{
    Task<HotspotActualState> GetStateAsync(CancellationToken cancellationToken);

    Task SetStateAsync(GuardTargetState target, CancellationToken cancellationToken);

    Task RestartAsync(CancellationToken cancellationToken);

    Task<int> GetConnectedClientCountAsync(CancellationToken cancellationToken);

    Task<int> GetMaxClientCountAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<HotspotClientInfo>> GetConnectedClientsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 探测当前设备是否支持开启移动热点（例如是否具备可用的 Wi-Fi 网卡）。
    /// </summary>
    Task<HotspotSupportState> GetTetheringSupportAsync(CancellationToken cancellationToken);
}
