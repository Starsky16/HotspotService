namespace HotspotService.Models;

/// <summary>
/// 网卡自启动以来的累计流量（字节），来自 IPv4 统计，只能用于求两次采样的差值。
/// </summary>
public readonly record struct NetworkTrafficCounters(long ReceivedBytes, long SentBytes);
