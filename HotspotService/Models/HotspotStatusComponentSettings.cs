namespace HotspotService.Models;

/// <summary>
/// 主界面展示组件「移动热点守护」的设置模型。
/// </summary>
public sealed class HotspotStatusComponentSettings
{
    /// <summary>
    /// 是否显示连接设备数量（含设备上限）。默认显示。
    /// </summary>
    public bool ShowClientCount { get; set; } = true;

    /// <summary>
    /// 是否显示最近一次同步错误。默认关闭，避免把错误详情直接暴露在主界面上。
    /// </summary>
    public bool ShowLastError { get; set; }
}
