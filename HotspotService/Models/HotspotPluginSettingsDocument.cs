namespace HotspotService.Models;

public sealed class HotspotPluginSettingsDocument
{
    public bool AutoStartGuard { get; set; } = true;

    public GuardTargetState StartupTarget { get; set; } = GuardTargetState.On;

    public HotspotRestartPolicySettings RestartPolicy { get; set; } = new();

    /// <summary>
    /// 连接设备信息（数量/上限/设备列表）的刷新间隔（秒）。
    /// </summary>
    public int ClientCountRefreshSeconds { get; set; } = 10;

    /// <summary>
    /// 网速采样设置（热点网卡与外网网卡吞吐）。
    /// </summary>
    public HotspotThroughputSettings Throughput { get; set; } = new();

    /// <summary>
    /// 快捷键重启热点设置（通过 KeyboardCapture 插件订阅全局按键）。
    /// </summary>
    public HotspotShortcutSettings Shortcut { get; set; } = new();
}
