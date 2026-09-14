namespace HotspotService.Models;

/// <summary>
/// 描述一个当前连接到移动热点的客户端设备。
/// </summary>
/// <param name="MacAddress">设备的 MAC 地址，可能为空字符串。</param>
/// <param name="HostNames">系统解析到的主机名列表，可能为空。</param>
public sealed record HotspotClientInfo(string MacAddress, IReadOnlyList<string> HostNames);
