using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using HotspotService.Automation;
using HotspotService.Components;
using HotspotService.Models;
using HotspotService.Services;
using HotspotService.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HotspotService;

[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var configFolder = string.IsNullOrWhiteSpace(PluginConfigFolder)
            ? Path.Combine(AppContext.BaseDirectory, "HotspotService")
            : PluginConfigFolder;
        var settingsStore = new HotspotPluginSettingsStore(Path.Combine(configFolder, "settings.cfg"));
        var runtimeState = new HotspotGuardRuntimeState();

        services.AddSingleton(settingsStore);
        services.AddSingleton(runtimeState);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGuardStatusNotifier, ClassIslandRulesetNotifier>();
        services.AddSingleton<IHotspotController, WinRtHotspotController>();
        services.AddSingleton<HotspotGuardCoordinator>();
        services.AddSingleton<INetworkTrafficReader, NetworkInterfaceTrafficReader>();
        services.AddSingleton<HotspotThroughputCalculator>();
        services.AddSingleton<IHotspotHotkeySource, KeyboardCaptureHotkeySource>();
        services.AddSingleton<HotspotShortcutMatcher>();
        services.AddHostedService<HotspotGuardBackgroundService>();
        services.AddHostedService<HotspotThroughputBackgroundService>();
        services.AddHostedService<HotspotShortcutHostedService>();

        services.AddSettingsPage<HotspotSettingsPage>();
        services.AddAction<EnableGuardAction>();
        services.AddAction<DisableGuardAction>();
        services.AddAction<SetGuardTargetAction, SetGuardTargetActionSettingsControl>();
        services.AddRule<GuardEnabledRuleSettings, GuardEnabledRuleSettingsControl>(
            PluginIds.GuardEnabledRule,
            "移动热点守护状态",
            PluginIds.WifiGlyph,
            settings => GuardEnabledRuleEvaluator.Evaluate(runtimeState, settings as GuardEnabledRuleSettings));
        services.AddRule<SystemHotspotStateRuleSettings, SystemHotspotStateRuleSettingsControl>(
            PluginIds.SystemHotspotStateRule,
            "系统热点状态",
            PluginIds.WifiGlyph,
            settings => GuardEnabledRuleEvaluator.EvaluateSystemHotspotState(runtimeState, settings as SystemHotspotStateRuleSettings));
        services.AddRule<GuardTargetRuleSettings, GuardTargetRuleSettingsControl>(
            PluginIds.GuardTargetRule,
            "移动热点守护目标",
            PluginIds.WifiGlyph,
            settings => GuardEnabledRuleEvaluator.EvaluateGuardTarget(runtimeState, settings as GuardTargetRuleSettings));

        services.AddComponent<HotspotStatusComponent, HotspotStatusComponentSettingsControl>();
    }
}
