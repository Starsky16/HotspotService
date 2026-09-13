namespace HotspotService.Models;

/// <summary>
/// 一次网卡流量读取的结果：可能成功（拿到累计流量），也可能因为找不到网卡、网卡被禁用等原因失败。
/// 失败时 <see cref="Error"/> 里是面向用户的中文原因，直接展示在设置页。
/// </summary>
public readonly record struct NetworkTrafficReadResult(
    NetworkTrafficTarget Target,
    bool Succeeded,
    string? InterfaceName,
    NetworkTrafficCounters Counters,
    DateTimeOffset Timestamp,
    string? Error)
{
    public static NetworkTrafficReadResult Success(
        NetworkTrafficTarget target,
        string interfaceName,
        NetworkTrafficCounters counters,
        DateTimeOffset timestamp) =>
        new(target, true, interfaceName, counters, timestamp, null);

    public static NetworkTrafficReadResult Failure(NetworkTrafficTarget target, string error) =>
        new(target, false, null, default, default, error);
}
