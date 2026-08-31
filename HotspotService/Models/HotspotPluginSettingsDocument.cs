namespace HotspotService.Models;

public sealed class HotspotPluginSettingsDocument
{
    public bool AutoStartGuard { get; set; } = true;

    public GuardTargetState StartupTarget { get; set; } = GuardTargetState.On;

    public HotspotRestartPolicySettings RestartPolicy { get; set; } = new();
}
