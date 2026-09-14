using Microsoft.Extensions.Hosting;

namespace HotspotService.Services;

public sealed class HotspotGuardBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
    private readonly HotspotGuardCoordinator _coordinator;

    public HotspotGuardBackgroundService(HotspotGuardCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 初始化失败也要继续轮询：后续巡检有机会自愈。
        await RunSafelyAsync(_coordinator.InitializeAsync, stoppingToken);

        using var timer = new PeriodicTimer(CheckInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunSafelyAsync(_coordinator.RunPeriodicCheckAsync, stoppingToken);
        }
    }

    /// <summary>
    /// 单次初始化或巡检失败不能让轮询循环整体退出：否则守护状态与连接数刷新会永久停止，
    /// 只能通过重启应用恢复。这里吞掉非取消异常，等下一次巡检继续尝试。
    /// </summary>
    private static async Task RunSafelyAsync(Func<CancellationToken, Task> action, CancellationToken stoppingToken)
    {
        try
        {
            await action(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 应用正在停止，属于正常路径。
        }
        catch
        {
            // 忽略单次失败，等待下一次巡检。
        }
    }
}
