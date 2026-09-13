namespace HotspotService.Models;

/// <summary>
/// 网速采样的目标网卡。
/// </summary>
public enum NetworkTrafficTarget
{
    /// <summary>
    /// 承载移动热点的网卡。Windows 的 ICS 通常把 192.168.137.0/24 网段分配给共享热点，
    /// 使用 Wi-Fi Direct 虚拟适配器时其描述中包含 “Wi-Fi Direct”。
    /// </summary>
    Hotspot = 0,

    /// <summary>
    /// 承载外网访问的网卡：已连接、具有默认网关、且不承载热点。
    /// </summary>
    Internet = 1
}
