using HotspotService.Models;

namespace HotspotService.Services;

/// <summary>
/// 由两次连续采样计算瞬时吞吐。非线程安全，只允许后台采样服务单线程调用。
/// 下列情况返回 null，表示本周期没有可信速率：首个采样点、网卡发生切换、
/// 计数器回绕（网卡重连或驱动重置）、采样时间未前进。
/// </summary>
public sealed class HotspotThroughputCalculator
{
    private readonly Dictionary<NetworkTrafficTarget, Sample> _samples = [];

    /// <summary>计算吞吐（字节/秒）。返回值只在两次可信采样之间才有意义。</summary>
    public NetworkThroughput? Calculate(NetworkTrafficTarget target, NetworkInterfaceTraffic traffic)
    {
        var current = new Sample(
            traffic.InterfaceName,
            traffic.Counters.ReceivedBytes,
            traffic.Counters.SentBytes,
            traffic.Timestamp);

        if (!_samples.TryGetValue(target, out var previous))
        {
            _samples[target] = current;
            return null;
        }

        _samples[target] = current;

        if (!string.Equals(previous.InterfaceName, current.InterfaceName, StringComparison.Ordinal))
        {
            // 网卡切换：上一笔差值属于另一张网卡，直接丢弃。
            return null;
        }

        var seconds = (current.Timestamp - previous.Timestamp).TotalSeconds;
        if (seconds <= 0)
        {
            return null;
        }

        var downloadDelta = current.ReceivedBytes - previous.ReceivedBytes;
        var uploadDelta = current.SentBytes - previous.SentBytes;
        if (downloadDelta < 0 || uploadDelta < 0)
        {
            // 计数器回绕/归零：差值无意义，标记本周期无效并等待下一次采样。
            return null;
        }

        return new NetworkThroughput(downloadDelta / seconds, uploadDelta / seconds);
    }

    /// <summary>丢弃指定目标的采样基线（例如采样被关闭时）。</summary>
    public void Reset(NetworkTrafficTarget target)
    {
        _samples.Remove(target);
    }

    /// <summary>丢弃所有采样基线。</summary>
    public void ResetAll()
    {
        _samples.Clear();
    }

    private readonly record struct Sample(
        string InterfaceName,
        long ReceivedBytes,
        long SentBytes,
        DateTimeOffset Timestamp);
}
