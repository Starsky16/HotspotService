using System.Net.NetworkInformation;

namespace HotspotService.Models;

/// <summary>
/// 网卡快照。真实实现从 <see cref="NetworkInterface"/> 转换而来，
/// 由于只包含值类型与地址字符串，测试里可以手工构造，用于验证网卡选择规则。
/// </summary>
public readonly record struct NetworkAdapterInfo(
    string Name,
    string Description,
    NetworkInterfaceType InterfaceType,
    OperationalStatus Status,
    long Speed,
    bool IsLoopback,
    IReadOnlyList<string> UnicastAddresses,
    IReadOnlyList<string> GatewayAddresses);
