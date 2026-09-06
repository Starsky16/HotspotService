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
}
