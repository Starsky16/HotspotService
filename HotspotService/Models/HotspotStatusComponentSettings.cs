using HotspotService.Infrastructure;

namespace HotspotService.Models;

/// <summary>
/// 主界面展示组件「移动热点守护」的设置模型。
/// 实现属性通知以便在设置界面修改时，正在显示的组件能即时刷新。
/// </summary>
public sealed class HotspotStatusComponentSettings : ObservableObject
{
    private bool _showStatusDot = true;
    private bool _showClientCount = true;
    private bool _showHotspotThroughput = true;
    private bool _showInternetThroughput = false;

    /// <summary>
    /// 是否显示守护开启状态圆点（实心=开启，空心=关闭）。默认显示。
    /// </summary>
    public bool ShowStatusDot
    {
        get => _showStatusDot;
        set => SetProperty(ref _showStatusDot, value);
    }

    /// <summary>
    /// 是否显示连接设备数量。默认显示。
    /// </summary>
    public bool ShowClientCount
    {
        get => _showClientCount;
        set => SetProperty(ref _showClientCount, value);
    }

    /// <summary>
    /// 是否显示热点网卡网速（↓下行 ↑上行）。默认显示，需在插件设置页启用网速采样后才有数据。
    /// </summary>
    public bool ShowHotspotThroughput
    {
        get => _showHotspotThroughput;
        set => SetProperty(ref _showHotspotThroughput, value);
    }

    /// <summary>
    /// 是否显示外网（WAN）网卡网速。默认不显示，避免与热点网速混淆。
    /// </summary>
    public bool ShowInternetThroughput
    {
        get => _showInternetThroughput;
        set => SetProperty(ref _showInternetThroughput, value);
    }
}
