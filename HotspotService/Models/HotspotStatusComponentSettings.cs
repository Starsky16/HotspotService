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
}
