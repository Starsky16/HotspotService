namespace HotspotService.Models;

public sealed class HotspotRestartPolicySettings
{
    /// <summary>
    /// 是否启用自动重启策略。默认关闭，不影响既有守护行为。
    /// </summary>
    public bool EnableAutoRestart { get; set; } = false;

    /// <summary>
    /// 连接设备数达到该值（含）时自动重启热点；0 表示不因连接数量重启。
    /// </summary>
    public int ClientCountThreshold { get; set; } = 0;

    /// <summary>
    /// 连续同步失败达到该次数（含）时自动重启热点；0 表示不因失败重启。
    /// </summary>
    public int ConsecutiveFailureThreshold { get; set; } = 0;

    /// <summary>
    /// 热点卡在“切换中”状态超过该秒数时自动重启；0 表示不因卡死重启。
    /// </summary>
    public int StuckTransitioningSeconds { get; set; } = 0;

    /// <summary>
    /// 两次自动重启之间的冷却秒数，避免频繁重启导致抖动。
    /// </summary>
    public int RestartCooldownSeconds { get; set; } = 60;
}