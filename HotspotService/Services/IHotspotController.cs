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
}
