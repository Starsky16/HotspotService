using System.Net.NetworkInformation;
using System.Net.Sockets;
using HotspotService.Models;

namespace HotspotService.Services;

/// <summary>
/// 通过 <see cref="NetworkInterface"/> 读取真实网卡的 IPv4 累计流量。
/// 找不到网卡、网卡被拔出/禁用或读取出错时返回失败结果，不抛出异常。
/// </summary>
public sealed class NetworkInterfaceTrafficReader : INetworkTrafficReader
{
    private readonly TimeProvider _timeProvider;

    public NetworkInterfaceTrafficReader(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public NetworkTrafficReadResult Read(NetworkTrafficTarget target)
    {
        try
        {
            var adapters = new List<NetworkAdapterInfo>();
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (TryDescribe(networkInterface, out var adapter))
                {
                    adapters.Add(adapter);
                }
            }

            if (NetworkInterfaceResolver.Resolve(target, adapters) is not { } resolved)
            {
                return NetworkTrafficReadResult.Failure(target, NetworkInterfaceResolver.DescribeMissing(target));
            }

            var matched = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(x => string.Equals(x.Name, resolved.Name, StringComparison.Ordinal));
            if (matched is null)
            {
                return NetworkTrafficReadResult.Failure(target, $"网卡“{resolved.Name}”已不可用。");
            }

            var statistics = matched.GetIPv4Statistics();
            return NetworkTrafficReadResult.Success(
                target,
                resolved.Name,
                new NetworkTrafficCounters(statistics.BytesReceived, statistics.BytesSent),
                _timeProvider.GetUtcNow());
        }
        catch (Exception ex)
        {
            // 采样失败不应影响宿主：统一转成面向用户的失败结果。
            return NetworkTrafficReadResult.Failure(target, $"读取网卡流量失败：{ex.Message}");
        }
    }

    private static bool TryDescribe(NetworkInterface networkInterface, out NetworkAdapterInfo adapter)
    {
        adapter = default;
        try
        {
            long speed = 0;
            try
            {
                speed = networkInterface.Speed;
            }
            catch (NetworkInformationException)
            {
                // 部分虚拟网卡不支持读取速率，按 0 处理（只影响多候选时的排序优先级）。
            }

            var properties = networkInterface.GetIPProperties();
            var unicastAddresses = properties.UnicastAddresses
                .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(x => x.Address.ToString())
                .ToArray();
            var gatewayAddresses = properties.GatewayAddresses
                .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(x => x.Address.ToString())
                .ToArray();

            adapter = new NetworkAdapterInfo(
                networkInterface.Name,
                networkInterface.Description,
                networkInterface.NetworkInterfaceType,
                networkInterface.OperationalStatus,
                speed,
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback,
                unicastAddresses,
                gatewayAddresses);
            return true;
        }
        catch (Exception)
        {
            // 单个网卡读不到属性（常见于适配器正在被移除）时跳过它，不影响其它网卡的采样。
            return false;
        }
    }
}
