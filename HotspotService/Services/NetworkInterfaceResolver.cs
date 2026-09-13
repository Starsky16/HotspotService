using System.Net.NetworkInformation;
using HotspotService.Models;

namespace HotspotService.Services;

/// <summary>
/// 网卡选择规则（纯函数，便于在没有任何真实网卡的机器上自测）：
/// <list type="bullet">
/// <item>热点网卡：已连接，且拥有 192.168.137.x 地址（Windows ICS 默认网段）或名称/描述包含 “Wi-Fi Direct”；</item>
/// <item>外网网卡：已连接、具有非 0.0.0.0 的 IPv4 默认网关、且不承载热点。</item>
/// </list>
/// 同一目标有多个候选时取速率最高的网卡，速率相同则按名称排序取第一个，保证结果稳定。
/// </summary>
public static class NetworkInterfaceResolver
{
    private const string HotspotAddressPrefix = "192.168.137.";
    private const string WifiDirectMarker = "Wi-Fi Direct";
    private const string EmptyGateway = "0.0.0.0";

    /// <summary>按目标选择网卡，找不到时返回 null。</summary>
    public static NetworkAdapterInfo? Resolve(NetworkTrafficTarget target, IEnumerable<NetworkAdapterInfo> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        var usable = adapters.Where(IsUsable).ToList();
        return target switch
        {
            NetworkTrafficTarget.Hotspot => SelectBest(usable.Where(IsHotspotAdapter)),
            NetworkTrafficTarget.Internet => SelectBest(usable.Where(IsInternetAdapter)),
            _ => null
        };
    }

    /// <summary>是否为承载移动热点的网卡。</summary>
    public static bool IsHotspotAdapter(NetworkAdapterInfo adapter)
    {
        return adapter.UnicastAddresses.Any(HasHotspotAddress)
               || ContainsMarker(adapter.Description, WifiDirectMarker)
               || ContainsMarker(adapter.Name, WifiDirectMarker);
    }

    /// <summary>是否为可承载外网访问的网卡。</summary>
    public static bool IsInternetAdapter(NetworkAdapterInfo adapter)
    {
        return !IsHotspotAdapter(adapter)
               && adapter.GatewayAddresses.Any(address => !string.IsNullOrWhiteSpace(address) && address != EmptyGateway);
    }

    /// <summary>网卡是否处于可用状态（已连接、非环回、非隧道、至少有一个 IPv4 地址）。</summary>
    public static bool IsUsable(NetworkAdapterInfo adapter)
    {
        return adapter.Status == OperationalStatus.Up
               && !adapter.IsLoopback
               && adapter.InterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
               && adapter.UnicastAddresses.Any(address => !string.IsNullOrWhiteSpace(address));
    }

    /// <summary>地址是否落在 Windows ICS 热点网段 192.168.137.0/24。</summary>
    public static bool HasHotspotAddress(string address)
    {
        return !string.IsNullOrWhiteSpace(address)
               && address.StartsWith(HotspotAddressPrefix, StringComparison.Ordinal);
    }

    /// <summary>找不到网卡时展示给用户的原因。</summary>
    public static string DescribeMissing(NetworkTrafficTarget target)
    {
        return target switch
        {
            NetworkTrafficTarget.Hotspot =>
                "未找到热点网卡：系统中没有 192.168.137.x 地址的网卡，也没有 Wi-Fi Direct 虚拟适配器（通常在热点开启后才会出现）。",
            NetworkTrafficTarget.Internet =>
                "未找到外网网卡：没有处于已连接状态且具有默认网关的网卡。",
            _ => "未找到可用网卡。"
        };
    }

    private static NetworkAdapterInfo? SelectBest(IEnumerable<NetworkAdapterInfo> candidates)
    {
        NetworkAdapterInfo? best = null;
        foreach (var candidate in candidates)
        {
            if (best is not { } current
                || candidate.Speed > current.Speed
                || (candidate.Speed == current.Speed && string.CompareOrdinal(candidate.Name, current.Name) < 0))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static bool ContainsMarker(string? value, string marker)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }
}
