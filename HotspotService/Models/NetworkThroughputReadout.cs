namespace HotspotService.Models;

/// <summary>
/// 运行状态中对外暴露的单个目标的网速读数，有三种形态：
/// 未启用采样（<see cref="IsSampling"/> 为 false）、采样失败（<see cref="Error"/> 不为空）、
/// 采样成功但还没有速率（<see cref="Throughput"/> 为空，例如首个采样点或网卡刚切换）。
/// </summary>
public sealed record NetworkThroughputReadout(
    NetworkTrafficTarget Target,
    bool IsSampling,
    string? InterfaceName,
    NetworkThroughput? Throughput,
    DateTimeOffset? SampledAt,
    string? Error)
{
    /// <summary>采样被关闭（或尚未开始）时的读数。</summary>
    public static NetworkThroughputReadout NotSampling(NetworkTrafficTarget target) =>
        new(target, false, null, null, null, null);

    /// <summary>采样成功时的读数；首个采样点或网卡切换后 <paramref name="throughput"/> 为空。</summary>
    public static NetworkThroughputReadout Sampling(
        NetworkTrafficTarget target,
        string interfaceName,
        NetworkThroughput? throughput,
        DateTimeOffset sampledAt) =>
        new(target, true, interfaceName, throughput, sampledAt, null);

    /// <summary>采样失败时的读数，<paramref name="error"/> 为面向用户的中文原因。</summary>
    public static NetworkThroughputReadout Unavailable(NetworkTrafficTarget target, string error, DateTimeOffset sampledAt) =>
        new(target, true, null, null, sampledAt, error);
}
