namespace HotspotService.Models;

/// <summary>
/// 一次成功的网卡流量读数：网卡名 + 累计流量 + 采样时间。
/// </summary>
public readonly record struct NetworkInterfaceTraffic(
    string InterfaceName,
    NetworkTrafficCounters Counters,
    DateTimeOffset Timestamp);
