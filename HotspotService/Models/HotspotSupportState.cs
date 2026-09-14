namespace HotspotService.Models;

/// <summary>
/// 当前设备是否支持开启移动热点。
/// </summary>
public enum HotspotSupportState
{
    /// <summary>尚未探测或暂时无法判断。</summary>
    Unknown = 0,

    /// <summary>设备支持开启移动热点。</summary>
    Supported = 1,

    /// <summary>设备不支持开启移动热点（例如没有可用的 Wi-Fi 网卡）。</summary>
    NotSupported = 2
}
