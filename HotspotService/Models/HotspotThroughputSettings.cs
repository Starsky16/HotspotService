namespace HotspotService.Models;

/// <summary>
/// 网速采样设置，持久化到插件配置文件。
/// 采样只读取网卡计数器，不会修改任何系统设置。
/// </summary>
public sealed class HotspotThroughputSettings
{
    /// <summary>允许的最小采样间隔（秒）。</summary>
    public const int MinimumIntervalSeconds = 1;

    /// <summary>允许的最大采样间隔（秒）。</summary>
    public const int MaximumIntervalSeconds = 10;

    /// <summary>是否启用网速采样。默认开启。</summary>
    public bool EnableSampling { get; set; } = true;

    /// <summary>采样间隔（秒），取值范围 1–10，默认 2 秒。</summary>
    public int SamplingIntervalSeconds { get; set; } = 2;

    /// <summary>把任意整数采样间隔收进合法区间。</summary>
    public static int ClampInterval(int value) =>
        Math.Clamp(value, MinimumIntervalSeconds, MaximumIntervalSeconds);
}
