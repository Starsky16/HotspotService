using System.Net.NetworkInformation;
using HotspotService.Automation;
using HotspotService.Models;
using HotspotService.Services;

namespace HotspotService.Tests;

public static class Program
{
    private static readonly List<string> Failures = [];

    public static async Task<int> Main()
    {
        await RunTestAsync("Settings store roundtrip persists values", TestSettingsStoreRoundtripAsync);
        await RunTestAsync("Initialize applies startup target and auto-start sync", TestInitializeAppliesStartupConfigurationAsync);
        await RunTestAsync("Enable guard keeps target and syncs existing target", TestEnableGuardKeepsExistingTargetAsync);
        await RunTestAsync("Disable guard does not change hotspot state", TestDisableGuardDoesNotTouchHotspotAsync);
        await RunTestAsync("Changing target syncs only when guard is enabled", TestChangingTargetSyncsOnlyWhenEnabledAsync);
        await RunTestAsync("Guard-enabled rule evaluates from runtime state", TestGuardEnabledRuleEvaluationAsync);
        await RunTestAsync("Failures are retried and cleared after success", TestFailureRetryAsync);
        await RunTestAsync("Concurrent sync requests do not overlap", TestConcurrentSyncDoesNotOverlapAsync);
        await RunTestAsync("Periodic check refreshes connected client count", TestPeriodicCheckRefreshesClientCountAsync);
        await RunTestAsync("Client count is zero while hotspot is off", TestClientCountZeroWhileHotspotOffAsync);
        await RunTestAsync("Client count is read once per periodic check", TestClientCountReadOncePerCheckAsync);
        await RunTestAsync("Periodic check refreshes connected client list", TestPeriodicCheckRefreshesConnectedClientsAsync);
        await RunTestAsync("Client list failure degrades without failing sync", TestClientListReadFailureDoesNotFailSyncAsync);
        await RunTestAsync("Client info refresh respects configured interval", TestClientInfoRefreshRespectsIntervalAsync);
        await RunTestAsync("Client info refresh is forced after guard restarts hotspot", TestRefreshForcedAfterGuardRestartsHotspotAsync);
        await RunTestAsync("Client-count restart ignored while hotspot is not running", TestClientCountRestartIgnoredWhileHotspotNotRunningAsync);
        await RunTestAsync("Unsupported device reports None and skips hotspot operations", TestUnsupportedDeviceSkipsOperationsAsync);
        await RunTestAsync("Settings store restart policy roundtrips", TestSettingsStoreRestartPolicyRoundtripAsync);
        await RunTestAsync("Legacy key-value settings are migrated to JSON", TestLegacyKeyValueSettingsAreMigratedAsync);
        await RunTestAsync("Auto restart triggers after consecutive failures", TestAutoRestartAfterConsecutiveFailuresAsync);
        await RunTestAsync("Auto restart triggers at client count threshold", TestAutoRestartAtClientCountThresholdAsync);
        await RunTestAsync("Auto restart cooldown prevents repeated restarts", TestRestartCooldownPreventsRepeatedRestartsAsync);
        await RunTestAsync("Auto restart is skipped while guard disabled", TestAutoRestartRequiresGuardEnabledAsync);
        await RunTestAsync("Manual restart stops then starts the hotspot", TestManualRestartStopsThenStartsAsync);
        await RunTestAsync("Failed restart records an error", TestRestartFailureRecordsErrorAsync);
        await RunTestAsync("Stuck transitioning triggers restart after timeout", TestStuckTransitioningRestartAsync);
        await RunTestAsync("Auto restart pauses after repeated restarts", TestAutoRestartPausesAfterRepeatedRestartsAsync);
        await RunTestAsync("Restart counter resets when condition clears", TestRestartCountResetsWhenConditionClearsAsync);
        await RunTestAsync("Client count read failure does not fail sync", TestClientCountReadFailureDoesNotFailSyncAsync);
        await RunTestAsync("Transitioning hotspots keep retrying while the client count is zero", TestTransitioningRetriesWhileCountZeroAsync);
        await RunTestAsync("Transitioning hotspots keep the last known client count", TestTransitioningKeepsLastClientCountAsync);
        await RunTestAsync("Client count recovers inside the post-restart recovery window", TestClientCountRecoversInRecoveryWindowAsync);
        await RunTestAsync("Client count recovery window expires", TestClientCountRecoveryWindowExpiresAsync);
        await RunTestAsync("Failed client count read is retried on the next check", TestClientCountFailureRetriesNextCheckAsync);
        await RunTestAsync("Guard status notification failure does not break the periodic check", TestNotifierFailureDoesNotBreakCheckAsync);
        await RunTestAsync("Failed manual restart propagates exception", TestManualRestartFailurePropagatesAsync);
        await RunTestAsync("Network speed text is formatted for display", TestNetworkSpeedTextFormattingAsync);
        await RunTestAsync("Network interface resolver picks hotspot and internet adapters", TestNetworkInterfaceResolverAsync);
        await RunTestAsync("Throughput calculator needs two samples on the same interface", TestThroughputCalculatorAsync);
        await RunTestAsync("Settings store throughput settings roundtrip and clamp", TestSettingsStoreThroughputRoundtripAsync);
        await RunTestAsync("Throughput sampling service samples both targets", TestThroughputSamplingServiceAsync);
        await RunTestAsync("Shortcut matcher requires the exact key and modifiers", TestShortcutMatcherAsync);
        await RunTestAsync("Settings store shortcut settings roundtrip and clamp", TestSettingsStoreShortcutRoundtripAsync);
        await RunTestAsync("Keyboard shortcut triggers a hotspot restart", TestShortcutRestartAsync);
        await RunTestAsync("Optional keyboard source stays safe without a host", TestKeyboardCaptureSourceWithoutHostAsync);

        if (Failures.Count == 0)
        {
            Console.WriteLine("All tests passed.");
            return 0;
        }

        Console.Error.WriteLine("Test failures:");
        foreach (var failure in Failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return 1;
    }

    private static async Task RunTestAsync(string name, Func<Task> test)
    {
        try
        {
            await test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Failures.Add($"{name}: {ex.Message}");
            Console.WriteLine($"FAIL {name}");
        }
    }

    private static Task TestSettingsStoreRoundtripAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "settings.json");
            var first = new HotspotPluginSettingsStore(path)
            {
                AutoStartGuard = true,
                StartupTarget = GuardTargetState.Off
            };

            var second = new HotspotPluginSettingsStore(path);
            AssertTrue(second.AutoStartGuard, "AutoStartGuard should roundtrip as true.");
            AssertEqual(GuardTargetState.Off, second.StartupTarget, "StartupTarget should roundtrip as Off.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task TestInitializeAppliesStartupConfigurationAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.Off, autoStartGuard: true, startupTarget: GuardTargetState.On);

        await context.Coordinator.InitializeAsync(CancellationToken.None);

        AssertTrue(context.RuntimeState.GuardEnabled, "Guard should be enabled after initialization.");
        AssertEqual(GuardTargetState.On, context.RuntimeState.GuardTarget, "Guard target should come from persisted startup target.");
        AssertEqual(1, context.Controller.StartCallCount, "Initialization should immediately reconcile to the startup target.");
        AssertEqual(HotspotActualState.On, context.RuntimeState.LastKnownHotspotState, "Runtime state should reflect the latest hotspot state.");
        AssertEqual(1, context.Notifier.NotificationCount, "Guard status change should notify the ruleset service once.");
    }

    private static async Task TestEnableGuardKeepsExistingTargetAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: false, startupTarget: GuardTargetState.Off);

        await context.Coordinator.InitializeAsync(CancellationToken.None);
        await context.Coordinator.SetGuardEnabledAsync(true, CancellationToken.None);

        AssertTrue(context.RuntimeState.GuardEnabled, "Guard should be enabled.");
        AssertEqual(GuardTargetState.Off, context.RuntimeState.GuardTarget, "Enable should not rewrite the existing guard target.");
        AssertEqual(1, context.Controller.StopCallCount, "Enable should reconcile using the existing target.");
        AssertEqual(1, context.Notifier.NotificationCount, "Enabling the guard should raise one status notification.");
    }

    private static async Task TestDisableGuardDoesNotTouchHotspotAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);

        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();

        await context.Coordinator.SetGuardEnabledAsync(false, CancellationToken.None);

        AssertFalse(context.RuntimeState.GuardEnabled, "Guard should be disabled.");
        AssertEqual(0, context.Controller.SetStateCallCount, "Disabling the guard must not touch the hotspot.");
        AssertEqual(2, context.Notifier.NotificationCount, "Initialization and disabling should each notify once.");
    }

    private static async Task TestChangingTargetSyncsOnlyWhenEnabledAsync()
    {
        var enabledContext = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        await enabledContext.Coordinator.InitializeAsync(CancellationToken.None);
        enabledContext.Controller.ResetCounts();

        await enabledContext.Coordinator.SetGuardTargetAsync(GuardTargetState.Off, CancellationToken.None);

        AssertEqual(GuardTargetState.Off, enabledContext.RuntimeState.GuardTarget, "Changing target should update runtime target.");
        AssertEqual(1, enabledContext.Controller.StopCallCount, "Changing target should reconcile immediately when enabled.");

        var disabledContext = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: false, startupTarget: GuardTargetState.On);
        await disabledContext.Coordinator.InitializeAsync(CancellationToken.None);
        disabledContext.Controller.ResetCounts();

        await disabledContext.Coordinator.SetGuardTargetAsync(GuardTargetState.Off, CancellationToken.None);

        AssertEqual(GuardTargetState.Off, disabledContext.RuntimeState.GuardTarget, "Changing target should still update runtime target while disabled.");
        AssertEqual(0, disabledContext.Controller.SetStateCallCount, "Changing target while disabled must not touch the hotspot.");
    }

    private static Task TestGuardEnabledRuleEvaluationAsync()
    {
        var runtimeState = new HotspotGuardRuntimeState();
        runtimeState.SetGuardEnabled(true);

        var enabled = GuardEnabledRuleEvaluator.Evaluate(runtimeState, new GuardEnabledRuleSettings { ExpectedEnabled = true });
        var disabled = GuardEnabledRuleEvaluator.Evaluate(runtimeState, new GuardEnabledRuleSettings { ExpectedEnabled = false });

        AssertTrue(enabled, "Rule should match when guard state is enabled.");
        AssertFalse(disabled, "Rule should not match the opposite guard state.");
        return Task.CompletedTask;
    }

    private static async Task TestFailureRetryAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.Off, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.Controller.FailNextSet = true;

        await context.Coordinator.InitializeAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.StartCallCount, "Initialization should attempt the first sync.");
        AssertTrue(!string.IsNullOrWhiteSpace(context.RuntimeState.LastError), "A failed sync should record an error.");

        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(2, context.Controller.StartCallCount, "Periodic check should retry after a failure.");
        AssertTrue(string.IsNullOrWhiteSpace(context.RuntimeState.LastError), "A successful retry should clear the last error.");
        AssertEqual(HotspotActualState.On, context.RuntimeState.LastKnownHotspotState, "Runtime state should recover after retry success.");
    }

    private static async Task TestConcurrentSyncDoesNotOverlapAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        await context.Coordinator.InitializeAsync(CancellationToken.None);

        context.Controller.CurrentState = HotspotActualState.Off;
        context.Controller.SetDelay = TimeSpan.FromMilliseconds(150);
        context.Controller.ResetCounts();

        await Task.WhenAll(
            context.Coordinator.RequestSyncAsync(CancellationToken.None),
            context.Coordinator.RequestSyncAsync(CancellationToken.None),
            context.Coordinator.RequestSyncAsync(CancellationToken.None));

        AssertEqual(1, context.Controller.StartCallCount, "Only the first queued sync should need to turn the hotspot back on.");
        AssertEqual(1, context.Controller.MaxConcurrentSetCalls, "Hotspot sync should never overlap.");
    }

    private static async Task TestPeriodicCheckRefreshesClientCountAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: false, startupTarget: GuardTargetState.On);
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ConnectedClientCount = 5;
        context.Controller.ResetCounts();

        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(5, context.RuntimeState.ConnectedClientCount, "Periodic check should refresh the connected client count.");
        AssertEqual(8, context.RuntimeState.MaxClientCount, "Periodic check should refresh the max client count.");
    }

    private static async Task TestClientCountZeroWhileHotspotOffAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.Off, autoStartGuard: false, startupTarget: GuardTargetState.Off);
        await context.Coordinator.InitializeAsync(CancellationToken.None);

        AssertEqual(0, context.RuntimeState.ConnectedClientCount, "Client count should be zero while the hotspot is off.");
    }

    private static async Task TestClientCountReadOncePerCheckAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();

        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.GetClientCountCallCount, "A periodic check should read the client count exactly once after the interval elapses.");
    }

    private static Task TestSettingsStoreRestartPolicyRoundtripAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "settings.json");
            var first = new HotspotPluginSettingsStore(path)
            {
                RestartPolicy = new HotspotRestartPolicySettings
                {
                    EnableAutoRestart = true,
                    ClientCountThreshold = 4,
                    ConsecutiveFailureThreshold = 3,
                    StuckTransitioningSeconds = 30,
                    RestartCooldownSeconds = 120
                }
            };

            var second = new HotspotPluginSettingsStore(path);
            AssertTrue(second.RestartPolicy.EnableAutoRestart, "EnableAutoRestart should roundtrip as true.");
            AssertEqual(4, second.RestartPolicy.ClientCountThreshold, "ClientCountThreshold should roundtrip.");
            AssertEqual(3, second.RestartPolicy.ConsecutiveFailureThreshold, "ConsecutiveFailureThreshold should roundtrip.");
            AssertEqual(30, second.RestartPolicy.StuckTransitioningSeconds, "StuckTransitioningSeconds should roundtrip.");
            AssertEqual(120, second.RestartPolicy.RestartCooldownSeconds, "RestartCooldownSeconds should roundtrip.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static Task TestLegacyKeyValueSettingsAreMigratedAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "settings.cfg");
            File.WriteAllLines(path, new[] { "AutoStartGuard=False", "StartupTarget=Off" });

            var store = new HotspotPluginSettingsStore(path);

            AssertFalse(store.AutoStartGuard, "Legacy AutoStartGuard should be loaded.");
            AssertEqual(GuardTargetState.Off, store.StartupTarget, "Legacy StartupTarget should be loaded.");
            AssertTrue(File.ReadAllText(path).TrimStart().StartsWith('{'), "Legacy settings should be migrated to JSON.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task TestAutoRestartAfterConsecutiveFailuresAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.RestartPolicy = new HotspotRestartPolicySettings
        {
            EnableAutoRestart = true,
            ConsecutiveFailureThreshold = 2
        };
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();

        context.Controller.CurrentState = HotspotActualState.Off;
        context.Controller.FailNextSet = true;
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(0, context.Controller.StopCallCount, "One failure should not yet trigger a restart.");

        context.Controller.CurrentState = HotspotActualState.Off;
        context.Controller.FailNextSet = true;
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.StopCallCount, "Reaching the failure threshold should restart the hotspot.");
        AssertTrue(string.IsNullOrWhiteSpace(context.RuntimeState.LastError), "A successful restart should clear the last error.");
        AssertEqual(HotspotActualState.On, context.RuntimeState.LastKnownHotspotState, "Hotspot should be back on after restart.");
    }

    private static async Task TestAutoRestartAtClientCountThresholdAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.RestartPolicy = new HotspotRestartPolicySettings
        {
            EnableAutoRestart = true,
            ClientCountThreshold = 3
        };
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();
        context.Controller.ConnectedClientCount = 4;

        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.StopCallCount, "Reaching the client threshold should restart the hotspot.");
        AssertEqual(HotspotActualState.On, context.RuntimeState.LastKnownHotspotState, "Hotspot should be back on after restart.");
    }

    private static async Task TestRestartCooldownPreventsRepeatedRestartsAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.RestartPolicy = new HotspotRestartPolicySettings
        {
            EnableAutoRestart = true,
            ClientCountThreshold = 3,
            RestartCooldownSeconds = 60
        };
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();
        context.Controller.ConnectedClientCount = 4;

        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.StopCallCount, "Cooldown should prevent a second restart.");
    }

    private static async Task TestAutoRestartRequiresGuardEnabledAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: false, startupTarget: GuardTargetState.On);
        context.SettingsStore.RestartPolicy = new HotspotRestartPolicySettings
        {
            EnableAutoRestart = true,
            ClientCountThreshold = 3
        };
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();
        context.Controller.ConnectedClientCount = 4;

        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(0, context.Controller.StopCallCount, "Auto restart must not run while the guard is disabled.");
    }

    private static async Task TestManualRestartStopsThenStartsAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();

        await context.Coordinator.RestartHotspotAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.StopCallCount, "Restart should stop the hotspot first.");
        AssertEqual(1, context.Controller.StartCallCount, "Restart should start the hotspot afterwards.");
    }

    private static async Task TestRestartFailureRecordsErrorAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.RestartPolicy = new HotspotRestartPolicySettings
        {
            EnableAutoRestart = true,
            ClientCountThreshold = 3,
            RestartCooldownSeconds = 60
        };
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();
        context.Controller.ConnectedClientCount = 4;
        context.Controller.FailNextSet = true;

        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.StopCallCount, "A restart attempt should have been made.");
        AssertTrue(!string.IsNullOrWhiteSpace(context.RuntimeState.LastError), "A failed restart should record an error.");
    }

    private static async Task TestStuckTransitioningRestartAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.Transitioning, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.RestartPolicy = new HotspotRestartPolicySettings
        {
            EnableAutoRestart = true,
            StuckTransitioningSeconds = 5
        };
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();

        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(0, context.Controller.StopCallCount, "A single check should not yet count as stuck.");

        context.TimeProvider.Advance(TimeSpan.FromSeconds(6));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.StopCallCount, "A hotspot stuck in transitioning should be restarted.");
    }

    private static async Task TestAutoRestartPausesAfterRepeatedRestartsAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.RestartPolicy = new HotspotRestartPolicySettings
        {
            EnableAutoRestart = true,
            ClientCountThreshold = 3,
            RestartCooldownSeconds = 0
        };
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();
        context.Controller.ConnectedClientCount = 4;

        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        for (var i = 0; i < 3; i++)
        {
            await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        }

        AssertEqual(3, context.Controller.StopCallCount, "Auto restart should stop after reaching the repeated-restart limit.");

        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(3, context.Controller.StopCallCount, "Auto restart should be paused after repeated restarts.");

        context.TimeProvider.Advance(TimeSpan.FromMinutes(31));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(4, context.Controller.StopCallCount, "Auto restart should resume after the pause expires.");
    }

    private static async Task TestRestartCountResetsWhenConditionClearsAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.RestartPolicy = new HotspotRestartPolicySettings
        {
            EnableAutoRestart = true,
            ClientCountThreshold = 3,
            RestartCooldownSeconds = 0
        };
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();
        context.Controller.ConnectedClientCount = 4;

        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(1, context.Controller.StopCallCount, "First restart should occur.");

        context.Controller.ConnectedClientCount = 0;
        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(1, context.Controller.StopCallCount, "No restart should occur while the condition is cleared.");

        context.Controller.ConnectedClientCount = 4;
        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        // 若计数未重置，第二轮会在第 2 次重启时触发暂停；重置后第 3 次巡检仍会重启。
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(4, context.Controller.StopCallCount, "Restart counter should have been reset before the second round.");
    }

    private static async Task TestClientCountReadFailureDoesNotFailSyncAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();

        context.Controller.FailNextClientCountRead = true;
        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(0, context.Controller.SetStateCallCount, "Client count read failure should not affect the main sync.");
        AssertTrue(string.IsNullOrWhiteSpace(context.RuntimeState.LastError), "Client count read failure should not record a sync error.");
    }

    /// <summary>
    /// 网卡复位后 WinRT 可能长时间报告“切换中”：此时若连接数还是 0（重启后客户端尚未回连），
    /// 必须继续重读，否则界面会永久停在 0。
    /// </summary>
    private static async Task TestTransitioningRetriesWhileCountZeroAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.Transitioning, autoStartGuard: true, startupTarget: GuardTargetState.On);
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        AssertEqual(0, context.RuntimeState.ConnectedClientCount, "Client count should start at zero in this scenario.");

        context.Controller.ConnectedClientCount = 3;
        context.Controller.ResetCounts();
        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.GetClientCountCallCount, "A zero client count must keep being read while the hotspot reports a transition.");
        AssertEqual(3, context.RuntimeState.ConnectedClientCount, "Client count should recover while the hotspot reports a transition.");
    }

    /// <summary>切换态下已有有效连接数时保持原有语义：不读取，避免瞬时 0 覆盖真实数据。</summary>
    private static async Task TestTransitioningKeepsLastClientCountAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.Transitioning, autoStartGuard: true, startupTarget: GuardTargetState.On);
        await context.Coordinator.InitializeAsync(CancellationToken.None);

        context.Controller.ConnectedClientCount = 3;
        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(3, context.RuntimeState.ConnectedClientCount, "Client count should be read while it is still zero.");

        context.Controller.ResetCounts();
        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(0, context.Controller.GetClientCountCallCount, "A known client count must not be re-read while the hotspot reports a transition.");
        AssertEqual(3, context.RuntimeState.ConnectedClientCount, "Client count should stay unchanged while the hotspot reports a transition.");
    }

    /// <summary>
    /// 重启/被守护拉起后，客户端回连需要时间：恢复窗口内即使刷新间隔远未到期，
    /// 只要读数还是 0 就要按轮询周期继续重读。
    /// </summary>
    private static async Task TestClientCountRecoversInRecoveryWindowAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.Off, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.ClientCountRefreshSeconds = 3600;
        await context.Coordinator.InitializeAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.StartCallCount, "Guard should start the hotspot on initialization.");
        AssertEqual(0, context.RuntimeState.ConnectedClientCount, "Client count should be zero right after the hotspot starts.");

        context.Controller.ConnectedClientCount = 2;
        context.Controller.ResetCounts();
        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.GetClientCountCallCount, "Client count should be re-read inside the recovery window even when the refresh interval has not elapsed.");
        AssertEqual(2, context.RuntimeState.ConnectedClientCount, "Client count should recover as soon as clients reconnect.");
    }

    /// <summary>恢复窗口过期后必须回到按配置间隔刷新，避免 0 值长期高频读取。</summary>
    private static async Task TestClientCountRecoveryWindowExpiresAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.Off, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.ClientCountRefreshSeconds = 300;
        await context.Coordinator.InitializeAsync(CancellationToken.None);

        context.Controller.ConnectedClientCount = 2;
        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(2, context.RuntimeState.ConnectedClientCount, "Client count should be read inside the recovery window.");

        // 窗口（2 分钟）已过期且刷新间隔（5 分钟）未到：读数为 0 也不再追读。
        context.Controller.ConnectedClientCount = 0;
        context.Controller.ResetCounts();
        context.TimeProvider.Advance(TimeSpan.FromMinutes(2));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(0, context.Controller.GetClientCountCallCount, "The configured refresh interval must apply again after the recovery window expires.");

        // 间隔到期后恢复常规刷新。
        context.Controller.ConnectedClientCount = 3;
        context.TimeProvider.Advance(TimeSpan.FromMinutes(5));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(3, context.RuntimeState.ConnectedClientCount, "Scheduled refresh should resume after the configured interval elapses.");
    }

    /// <summary>读取失败时不能推进刷新时间戳，否则要再等一个完整间隔才会重试。</summary>
    private static async Task TestClientCountFailureRetriesNextCheckAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.ClientCountRefreshSeconds = 300;
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ConnectedClientCount = 2;

        context.Controller.FailNextClientCountRead = true;
        context.TimeProvider.Advance(TimeSpan.FromSeconds(301));
        context.Controller.ResetCounts();
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(1, context.Controller.GetClientCountCallCount, "The scheduled client count read should be attempted.");
        AssertEqual(0, context.RuntimeState.ConnectedClientCount, "A failed read must not change the displayed client count.");

        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        context.Controller.ResetCounts();
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(1, context.Controller.GetClientCountCallCount, "A failed read must be retried on the next check instead of waiting for a full interval.");
        AssertEqual(2, context.RuntimeState.ConnectedClientCount, "The retry should pick up the real client count.");
    }

    /// <summary>规则集通知失败属于副作用异常，不能中断巡检（否则守护与连接数刷新会永久停止）。</summary>
    private static async Task TestNotifierFailureDoesNotBreakCheckAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ConnectedClientCount = 4;
        context.Controller.ResetCounts();
        context.Notifier.ThrowOnNotify = true;

        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(4, context.RuntimeState.ConnectedClientCount, "A failing status notification must not stop the client count refresh.");
    }

    private static async Task TestPeriodicCheckRefreshesConnectedClientsAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.Controller.ConnectedClients =
        [
            new HotspotClientInfo("AA:BB:CC:DD:EE:FF", ["device-a"]),
            new HotspotClientInfo("11:22:33:44:55:66", ["device-b"])
        ];
        await context.Coordinator.InitializeAsync(CancellationToken.None);

        AssertEqual(2, context.RuntimeState.ConnectedClients.Count, "Sync should refresh the connected client list.");
        AssertEqual("AA:BB:CC:DD:EE:FF", context.RuntimeState.ConnectedClients[0].MacAddress, "Client list should preserve the MAC address.");
        AssertEqual("device-b", context.RuntimeState.ConnectedClients[1].HostNames[0], "Client list should preserve host names.");
    }

    private static async Task TestClientListReadFailureDoesNotFailSyncAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();

        context.Controller.FailNextClientListRead = true;
        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(0, context.Controller.SetStateCallCount, "Client list read failure should not affect the main sync.");
        AssertTrue(string.IsNullOrWhiteSpace(context.RuntimeState.LastError), "Client list read failure should not record a sync error.");
    }

    private static async Task TestClientInfoRefreshRespectsIntervalAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.ClientCountRefreshSeconds = 5;
        await context.Coordinator.InitializeAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.GetClientCountCallCount, "Initialize should refresh client info once.");
        context.Controller.ResetCounts();

        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(0, context.Controller.GetClientCountCallCount, "Client info should not refresh before the configured interval elapses.");

        context.TimeProvider.Advance(TimeSpan.FromSeconds(6));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(1, context.Controller.GetClientCountCallCount, "Client info should refresh after the configured interval elapses.");
    }

    private static async Task TestRefreshForcedAfterGuardRestartsHotspotAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.ClientCountRefreshSeconds = 3600;
        context.Controller.ConnectedClientCount = 3;
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();

        // 模拟：系统设置里手动关闭热点后，守护把热点重新拉起。
        context.Controller.CurrentState = HotspotActualState.Off;
        context.Controller.ConnectedClientCount = 5;
        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.StartCallCount, "Guard should restart the hotspot after a manual shutdown.");
        AssertEqual(1, context.Controller.GetClientCountCallCount, "Transition to running should force an immediate client count refresh.");
        AssertEqual(5, context.RuntimeState.ConnectedClientCount, "Client count should reflect the refreshed value after restart.");
    }

    private static async Task TestClientCountRestartIgnoredWhileHotspotNotRunningAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.Transitioning, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.SettingsStore.RestartPolicy.EnableAutoRestart = true;
        context.SettingsStore.RestartPolicy.ClientCountThreshold = 3;
        context.SettingsStore.RestartPolicy.RestartCooldownSeconds = 0;
        context.Controller.ConnectedClientCount = 8;
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();

        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(0, context.Controller.StopCallCount, "Stale client count must not trigger a restart while the hotspot is not running.");
    }

    private static async Task TestUnsupportedDeviceSkipsOperationsAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        context.Controller.TetheringSupport = HotspotSupportState.NotSupported;
        await context.Coordinator.InitializeAsync(CancellationToken.None);

        AssertEqual(HotspotSupportState.NotSupported, context.RuntimeState.TetheringSupport, "Support state should be recorded.");
        AssertEqual(HotspotActualState.Off, context.RuntimeState.LastKnownHotspotState, "Hotspot state should read as off on unsupported hardware.");
        AssertEqual(0, context.RuntimeState.ConnectedClientCount, "Client count should be zero on unsupported hardware.");
        AssertEqual(0, context.Controller.GetStateCallCount, "Unsupported hardware should not trigger hotspot state reads.");
        AssertEqual(0, context.Controller.GetClientCountCallCount, "Unsupported hardware should not trigger client reads.");
        AssertEqual(0, context.Controller.StartCallCount, "Unsupported hardware should not try to start the hotspot.");
        AssertTrue(string.IsNullOrWhiteSpace(context.RuntimeState.LastError), "Unsupported hardware should not record a sync error.");

        context.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(0, context.Controller.GetStateCallCount, "Periodic checks should keep skipping operations on unsupported hardware.");
        AssertEqual(0, context.Controller.StartCallCount, "Periodic checks should keep skipping start attempts on unsupported hardware.");
    }

    private static async Task TestManualRestartFailurePropagatesAsync()
    {
        var context = CreateContext(controllerState: HotspotActualState.On, autoStartGuard: true, startupTarget: GuardTargetState.On);
        await context.Coordinator.InitializeAsync(CancellationToken.None);
        context.Controller.ResetCounts();

        context.Controller.FailNextSet = true;

        var threw = false;
        try
        {
            await context.Coordinator.RestartHotspotAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        AssertTrue(threw, "A failed manual restart should propagate the exception to the caller.");
    }

    private static Task TestShortcutMatcherAsync()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 3, 27, 0, 0, 0, TimeSpan.Zero));
        var matcher = new HotspotShortcutMatcher(timeProvider);
        var settings = new HotspotShortcutSettings
        {
            Enabled = true,
            KeyName = "F9",
            Ctrl = true,
            Alt = true,
            CooldownSeconds = 5
        };

        AssertTrue(
            matcher.TryMatch(settings, new HotspotKeyEvent("f9", Ctrl: true, Alt: true, Shift: false, Meta: false, IsAutoRepeat: false)),
            "Key names should be compared case-insensitively.");
        AssertTrue(matcher.LastTriggeredAt is not null, "A match should record the trigger time.");
        AssertFalse(
            matcher.TryMatch(settings, new HotspotKeyEvent("F9", Ctrl: true, Alt: true, Shift: false, Meta: false, IsAutoRepeat: false)),
            "The cooldown should suppress an immediate second trigger.");

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        AssertTrue(
            matcher.TryMatch(settings, new HotspotKeyEvent("F9", Ctrl: true, Alt: true, Shift: false, Meta: false, IsAutoRepeat: false)),
            "The shortcut should match again once the cooldown elapsed.");

        AssertFalse(
            matcher.TryMatch(settings, new HotspotKeyEvent("F9", Ctrl: true, Alt: false, Shift: false, Meta: false, IsAutoRepeat: false)),
            "A missing modifier must not match.");
        AssertFalse(
            matcher.TryMatch(settings, new HotspotKeyEvent("F9", Ctrl: true, Alt: true, Shift: true, Meta: false, IsAutoRepeat: false)),
            "An extra modifier must not match.");
        AssertFalse(
            matcher.TryMatch(settings, new HotspotKeyEvent("F9", Ctrl: true, Alt: true, Shift: false, Meta: true, IsAutoRepeat: false)),
            "An extra Meta modifier must not match.");
        AssertFalse(
            matcher.TryMatch(settings, new HotspotKeyEvent("F8", Ctrl: true, Alt: true, Shift: false, Meta: false, IsAutoRepeat: false)),
            "Another key must not match.");
        AssertFalse(
            matcher.TryMatch(settings, new HotspotKeyEvent("F9", Ctrl: true, Alt: true, Shift: false, Meta: false, IsAutoRepeat: true)),
            "Holding the key (auto repeat) must not retrigger.");

        settings.Enabled = false;
        AssertFalse(
            matcher.TryMatch(settings, new HotspotKeyEvent("F9", Ctrl: true, Alt: true, Shift: false, Meta: false, IsAutoRepeat: false)),
            "A disabled shortcut must not match.");

        settings.Enabled = true;
        settings.KeyName = " f9 ";
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        AssertTrue(
            matcher.TryMatch(settings, new HotspotKeyEvent("F9", Ctrl: true, Alt: true, Shift: false, Meta: false, IsAutoRepeat: false)),
            "The stored key name should be trimmed before comparing.");

        matcher.Reset();
        AssertTrue(matcher.LastTriggeredAt is null, "Reset should clear the trigger time.");

        AssertTrue(HotspotShortcutKeys.Contains("a"), "Key candidate lookup should be case-insensitive.");
        AssertTrue(HotspotShortcutKeys.Contains("F12"), "Function keys should be available as candidates.");
        AssertTrue(HotspotShortcutKeys.Contains("PageUp"), "Navigation keys should be available as candidates.");
        AssertFalse(HotspotShortcutKeys.Contains("NotAKey"), "Unknown key names should be rejected.");
        AssertTrue(HotspotShortcutKeys.All.Count > 60, "The candidate list should cover letters, digits, function keys and more.");
        return Task.CompletedTask;
    }


    private static Task TestSettingsStoreShortcutRoundtripAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "settings.json");
            var first = new HotspotPluginSettingsStore(path);
            AssertFalse(first.Shortcut.Enabled, "The shortcut should be disabled by default.");
            AssertEqual("F9", first.Shortcut.KeyName, "The default trigger key should be F9.");
            AssertTrue(first.Shortcut.Ctrl && first.Shortcut.Alt, "The default combination should be Ctrl+Alt.");
            AssertEqual(5, first.Shortcut.CooldownSeconds, "The default cooldown should be 5 seconds.");

            first.UpdateShortcut(settings =>
            {
                settings.Enabled = true;
                settings.KeyName = "PageUp";
                settings.Meta = true;
                settings.CooldownSeconds = 9999;
            });
            AssertTrue(first.Shortcut.Enabled, "The shortcut flag should be updated.");
            AssertEqual(600, first.Shortcut.CooldownSeconds, "Out-of-range cooldowns should be clamped to the maximum.");
            AssertEqual("Ctrl+Alt+Win+PageUp", first.Shortcut.DescribeShortcut(), "The description should list modifiers then the key.");

            var second = new HotspotPluginSettingsStore(path);
            AssertTrue(second.Shortcut.Enabled, "The shortcut flag should roundtrip through the settings file.");
            AssertEqual("PageUp", second.Shortcut.KeyName, "The trigger key should roundtrip through the settings file.");
            AssertTrue(second.Shortcut.Meta, "Modifier flags should roundtrip through the settings file.");

            second.UpdateShortcut(settings => settings.KeyName = "不存在的键");
            AssertEqual("F9", second.Shortcut.KeyName, "An unknown key name should fall back to the default.");

            second.UpdateShortcut(settings => settings.CooldownSeconds = 0);
            AssertEqual(1, second.Shortcut.CooldownSeconds, "Cooldowns below the minimum should be clamped.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }


    private static async Task TestShortcutRestartAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            var settingsStore = new HotspotPluginSettingsStore(Path.Combine(root, "settings.json"))
            {
                AutoStartGuard = true,
                StartupTarget = GuardTargetState.On
            };
            settingsStore.UpdateShortcut(settings =>
            {
                settings.Enabled = true;
                settings.KeyName = "F9";
                settings.Ctrl = true;
                settings.Alt = true;
                settings.CooldownSeconds = 5;
            });

            var runtimeState = new HotspotGuardRuntimeState();
            var controller = new FakeHotspotController(HotspotActualState.On);
            var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 3, 27, 0, 0, 0, TimeSpan.Zero));
            var coordinator = new HotspotGuardCoordinator(
                controller,
                settingsStore,
                runtimeState,
                new RecordingGuardStatusNotifier(),
                timeProvider);
            await coordinator.InitializeAsync(CancellationToken.None);
            controller.ResetCounts();

            var source = new FakeHotkeySource();
            var service = new HotspotShortcutHostedService(
                new HotspotShortcutMatcher(timeProvider),
                source,
                settingsStore,
                runtimeState,
                coordinator);

            service.RefreshSourceState();
            AssertEqual(1, source.AttachAttempts, "Refresh should try to attach while the source is unavailable.");
            AssertFalse(runtimeState.ShortcutSourceAvailable, "Runtime state should report a missing KeyboardCapture plugin.");
            AssertTrue(!string.IsNullOrWhiteSpace(runtimeState.ShortcutSourceMessage), "Runtime state should carry the source message.");

            source.IsAvailable = true;
            source.LastError = null;
            service.RefreshSourceState();
            AssertTrue(runtimeState.ShortcutSourceAvailable, "Runtime state should report an available source after attaching.");
            AssertTrue(string.IsNullOrWhiteSpace(runtimeState.ShortcutSourceMessage), "A successful attach should clear the message.");

            await service.HandleKeyEventAsync(new HotspotKeyEvent("F9", Ctrl: true, Alt: false, Shift: false, Meta: false, IsAutoRepeat: false));
            await service.HandleKeyEventAsync(new HotspotKeyEvent("F9", Ctrl: true, Alt: true, Shift: false, Meta: false, IsAutoRepeat: true));
            AssertEqual(0, controller.StopCallCount, "Unmatched events must not restart the hotspot.");

            await service.HandleKeyEventAsync(new HotspotKeyEvent("F9", Ctrl: true, Alt: true, Shift: false, Meta: false, IsAutoRepeat: false));
            AssertEqual(1, controller.StopCallCount, "A matching shortcut should stop the hotspot.");
            AssertEqual(1, controller.StartCallCount, "A matching shortcut should start the hotspot again.");
            AssertTrue(runtimeState.LastShortcutTriggeredAt is not null, "The trigger time should be recorded on the runtime state.");
            AssertTrue(string.IsNullOrWhiteSpace(runtimeState.LastShortcutError), "A successful restart should not record an error.");

            await service.HandleKeyEventAsync(new HotspotKeyEvent("F9", Ctrl: true, Alt: true, Shift: false, Meta: false, IsAutoRepeat: false));
            AssertEqual(1, controller.StopCallCount, "The cooldown should suppress a second trigger.");

            timeProvider.Advance(TimeSpan.FromSeconds(6));
            controller.FailNextSet = true;
            await service.HandleKeyEventAsync(new HotspotKeyEvent("F9", Ctrl: true, Alt: true, Shift: false, Meta: false, IsAutoRepeat: false));
            AssertTrue(!string.IsNullOrWhiteSpace(runtimeState.LastShortcutError), "A failed shortcut restart should be recorded as an error.");

            // 事件链路：服务启动后订阅来源事件，来源触发的事件应命中同一条处理链路。
            timeProvider.Advance(TimeSpan.FromSeconds(6));
            controller.FailNextSet = false;
            await service.StartAsync(CancellationToken.None);
            try
            {
                // .NET 10 起 ExecuteAsync 整体在 Task 上执行，订阅可能晚于 StartAsync 返回
                await WaitUntilAsync(() => source.SubscriberCount > 0);
                controller.ResetCounts();
                source.RaiseKeyPressed("F9", ctrl: true, alt: true, shift: false, meta: false);
                await Task.Delay(200);
                AssertEqual(1, controller.StopCallCount, "Events raised by the source should reach the shortcut handler.");
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }

            // 停机必须断开对宿主级单例服务的订阅，否则会残留订阅并导致来源实例无法回收。
            AssertEqual(1, source.DetachCallCount, "Stopping the service should detach the keyboard source exactly once.");
            AssertEqual(0, source.SubscriberCount, "Stopping the service should remove the KeyPressed subscription.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }


    private static Task TestKeyboardCaptureSourceWithoutHostAsync()
    {
        // 真实连接需要 ClassIsland 宿主提供 ClassIsland.Shared 程序集（自测进程里没有），
        // 因此这里只验证「未连接」时的安全初始状态，实际连接在 ClassIsland 里人工验证。
        var source = new KeyboardCaptureHotkeySource();
        AssertFalse(source.IsAvailable, "The optional keyboard source should start as unavailable.");
        AssertTrue(source.LastError is null, "No connection error should be recorded before the first attach attempt.");

        source.Detach();
        AssertFalse(source.IsAvailable, "Detaching an unattached source should be a no-op.");
        return Task.CompletedTask;
    }


    private static Task TestNetworkSpeedTextFormattingAsync()
    {
        AssertEqual("0 B/s", NetworkSpeedFormatter.FormatRate(0), "A zero rate should render as 0 B/s.");
        AssertEqual("0 B/s", NetworkSpeedFormatter.FormatRate(double.NaN), "Invalid rates should fall back to 0 B/s.");
        AssertEqual("512 B/s", NetworkSpeedFormatter.FormatRate(512), "Rates below 1 KB should render in B/s.");
        AssertEqual("2 KB/s", NetworkSpeedFormatter.FormatRate(2048), "Rates below 1 MB should render in KB/s.");
        AssertEqual("1.5 MB/s", NetworkSpeedFormatter.FormatRate(1.5 * 1024 * 1024), "Rates below 1 GB should render in MB/s.");
        AssertEqual("2.00 GB/s", NetworkSpeedFormatter.FormatRate(2d * 1024 * 1024 * 1024), "Large rates should render in GB/s.");

        var sampledAt = new DateTimeOffset(2026, 3, 27, 0, 0, 2, TimeSpan.Zero);
        var hotspot = NetworkThroughputReadout.Sampling(
            NetworkTrafficTarget.Hotspot,
            "Local Area Connection* 10",
            new NetworkThroughput(1024 * 1024, 512 * 1024),
            sampledAt);
        var internet = NetworkThroughputReadout.Sampling(
            NetworkTrafficTarget.Internet,
            "Ethernet",
            new NetworkThroughput(4 * 1024 * 1024, 1024 * 1024),
            sampledAt);

        AssertEqual(
            "↓1.0 MB/s ↑512 KB/s",
            NetworkSpeedFormatter.FormatComponentText(hotspot, internet, showHotspot: true, showInternet: false),
            "Component text should only contain the hotspot segment while the internet segment is hidden.");
        AssertEqual(
            "WAN ↓4.0 MB/s ↑1.0 MB/s",
            NetworkSpeedFormatter.FormatComponentText(hotspot, internet, showHotspot: false, showInternet: true),
            "The internet segment should carry the WAN prefix.");
        AssertEqual(
            "↓1.0 MB/s ↑512 KB/s | WAN ↓4.0 MB/s ↑1.0 MB/s",
            NetworkSpeedFormatter.FormatComponentText(hotspot, internet, showHotspot: true, showInternet: true),
            "Both segments should be joined when both targets are visible.");
        AssertEqual(
            string.Empty,
            NetworkSpeedFormatter.FormatComponentText(hotspot, internet, showHotspot: false, showInternet: false),
            "Component text should be empty when both targets are hidden.");
        AssertEqual(
            string.Empty,
            NetworkSpeedFormatter.FormatComponentText(
                NetworkThroughputReadout.NotSampling(NetworkTrafficTarget.Hotspot),
                internet,
                showHotspot: true,
                showInternet: false),
            "A readout without sampling enabled should not render any text.");

        var pending = NetworkThroughputReadout.Sampling(NetworkTrafficTarget.Hotspot, "Wi-Fi", null, sampledAt);
        AssertEqual(
            "↓— ↑—",
            NetworkSpeedFormatter.FormatComponentText(pending, internet, showHotspot: true, showInternet: false),
            "A sample without a rate yet should render placeholders instead of zero.");

        AssertEqual(
            "未启用采样",
            NetworkSpeedFormatter.FormatStatusLine(NetworkThroughputReadout.NotSampling(NetworkTrafficTarget.Hotspot)),
            "The status line should explain that sampling is disabled.");
        AssertEqual(
            "Ethernet：↓4.0 MB/s ↑1.0 MB/s",
            NetworkSpeedFormatter.FormatStatusLine(internet),
            "The status line should combine the interface name and both rates.");
        AssertTrue(
            NetworkSpeedFormatter
                .FormatStatusLine(NetworkThroughputReadout.Unavailable(NetworkTrafficTarget.Internet, "未找到外网网卡：测试用原因。", sampledAt))
                .Contains("未找到外网网卡", StringComparison.Ordinal),
            "An unavailable readout should expose its failure reason.");
        AssertEqual(
            "Ethernet：正在采集首个采样点…",
            NetworkSpeedFormatter.FormatStatusLine(NetworkThroughputReadout.Sampling(NetworkTrafficTarget.Internet, "Ethernet", null, sampledAt)),
            "A pending first sample should be explained on the settings page.");
        return Task.CompletedTask;
    }


    private static Task TestNetworkInterfaceResolverAsync()
    {
        var hotspotAdapter = CreateAdapter(
            name: "Local Area Connection* 10",
            description: "Microsoft Wi-Fi Direct Virtual Adapter",
            speed: 100_000_000,
            unicast: ["192.168.137.1"],
            gateways: []);
        var wifiAdapter = CreateAdapter(
            name: "Wi-Fi",
            description: "Intel(R) Wi-Fi 6 AX201 160MHz",
            speed: 866_000_000,
            unicast: ["192.168.1.20"],
            gateways: [],
            interfaceType: NetworkInterfaceType.Wireless80211);
        var ethernetAdapter = CreateAdapter(
            name: "Ethernet",
            description: "Intel(R) Ethernet Connection",
            speed: 1_000_000_000,
            unicast: ["10.0.0.5"],
            gateways: ["10.0.0.1"]);
        var tunnelAdapter = CreateAdapter(
            name: "VPN",
            description: "TAP-Windows Adapter V9",
            speed: 10_000_000,
            unicast: ["10.8.0.2"],
            gateways: ["10.8.0.1"],
            interfaceType: NetworkInterfaceType.Tunnel);
        var downAdapter = CreateAdapter(
            name: "Ethernet 2",
            description: "USB 网卡（未连接）",
            speed: 100_000_000,
            unicast: ["192.168.99.2"],
            gateways: ["192.168.99.1"],
            status: OperationalStatus.Down);

        var adapters = new[] { wifiAdapter, ethernetAdapter, hotspotAdapter, tunnelAdapter, downAdapter };

        var hotspot = RequireAdapter(
            "The hotspot adapter should be resolved from the ICS address.",
            NetworkInterfaceResolver.Resolve(NetworkTrafficTarget.Hotspot, adapters));
        AssertEqual("Local Area Connection* 10", hotspot.Name, "The adapter holding a 192.168.137.x address should win.");

        var internet = RequireAdapter(
            "The internet adapter should be resolved from the default gateway.",
            NetworkInterfaceResolver.Resolve(NetworkTrafficTarget.Internet, adapters));
        AssertEqual("Ethernet", internet.Name, "Only the connected adapter with a gateway and without the hotspot subnet should be used.");

        var wifiDirectOnly = RequireAdapter(
            "A Wi-Fi Direct virtual adapter should be recognized even without an ICS address.",
            NetworkInterfaceResolver.Resolve(
                NetworkTrafficTarget.Hotspot,
                [
                    CreateAdapter(
                        name: "Local Area Connection* 3",
                        description: "Microsoft Wi-Fi Direct Virtual Adapter #2",
                        speed: 1_000_000,
                        unicast: ["169.254.10.1"],
                        gateways: [],
                        interfaceType: NetworkInterfaceType.Wireless80211)
                ]));
        AssertEqual("Local Area Connection* 3", wifiDirectOnly.Name, "Description-based hotspot detection should work.");

        var fastestHotspot = RequireAdapter(
            "The fastest hotspot candidate should win.",
            NetworkInterfaceResolver.Resolve(
                NetworkTrafficTarget.Hotspot,
                [
                    CreateAdapter("慢热点", string.Empty, 1_000, ["192.168.137.1"], []),
                    CreateAdapter("快热点", string.Empty, 1_000_000, ["192.168.137.1"], [])
                ]));
        AssertEqual("快热点", fastestHotspot.Name, "Candidates should be ranked by interface speed.");

        AssertTrue(
            NetworkInterfaceResolver.Resolve(
                NetworkTrafficTarget.Internet,
                [CreateAdapter("热点本身", string.Empty, 1000, ["192.168.137.1"], ["192.168.137.1"])]) is null,
            "The hotspot adapter must never be used as the internet adapter.");
        AssertTrue(
            NetworkInterfaceResolver.Resolve(
                NetworkTrafficTarget.Internet,
                [CreateAdapter("无网关网卡", string.Empty, 1000, ["10.0.0.9"], [])]) is null,
            "Adapters without a gateway must not be used for internet throughput.");
        AssertTrue(
            NetworkInterfaceResolver.Resolve(
                NetworkTrafficTarget.Internet,
                [CreateAdapter("零网关网卡", string.Empty, 1000, ["10.0.0.9"], ["0.0.0.0"])]) is null,
            "The 0.0.0.0 placeholder gateway must not count as a default gateway.");
        AssertTrue(
            NetworkInterfaceResolver.Resolve(NetworkTrafficTarget.Hotspot, []) is null,
            "An empty adapter list should resolve to no adapter.");
        AssertTrue(
            NetworkInterfaceResolver.DescribeMissing(NetworkTrafficTarget.Hotspot).Contains("热点", StringComparison.Ordinal),
            "The missing-hotspot reason should mention the hotspot.");
        AssertTrue(
            NetworkInterfaceResolver.DescribeMissing(NetworkTrafficTarget.Internet).Contains("网关", StringComparison.Ordinal),
            "The missing-internet reason should mention the default gateway.");
        return Task.CompletedTask;
    }


    private static Task TestThroughputCalculatorAsync()
    {
        var calculator = new HotspotThroughputCalculator();
        var start = new DateTimeOffset(2026, 3, 27, 0, 0, 0, TimeSpan.Zero);

        AssertTrue(
            calculator.Calculate(
                NetworkTrafficTarget.Hotspot,
                new NetworkInterfaceTraffic("Wi-Fi Direct", new NetworkTrafficCounters(1000, 200), start)) is null,
            "The first sample has no baseline and must not produce a rate.");

        var throughput = calculator.Calculate(
            NetworkTrafficTarget.Hotspot,
            new NetworkInterfaceTraffic("Wi-Fi Direct", new NetworkTrafficCounters(3048, 1224), start.AddSeconds(2)));
        AssertTrue(throughput is not null, "The second sample on the same interface should produce a rate.");
        AssertEqual(1024d, throughput.GetValueOrDefault().DownloadBytesPerSecond, "Download rate should be the counter delta over elapsed seconds.");
        AssertEqual(512d, throughput.GetValueOrDefault().UploadBytesPerSecond, "Upload rate should be the counter delta over elapsed seconds.");

        AssertTrue(
            calculator.Calculate(
                NetworkTrafficTarget.Hotspot,
                new NetworkInterfaceTraffic("Ethernet", new NetworkTrafficCounters(99999, 88888), start.AddSeconds(4))) is null,
            "Switching interfaces must drop the previous baseline instead of producing a bogus rate.");
        AssertTrue(
            calculator.Calculate(
                NetworkTrafficTarget.Hotspot,
                new NetworkInterfaceTraffic("Ethernet", new NetworkTrafficCounters(0, 0), start.AddSeconds(6))) is null,
            "A counter wrap (negative delta) must not produce a rate.");
        AssertTrue(
            calculator.Calculate(
                NetworkTrafficTarget.Hotspot,
                new NetworkInterfaceTraffic("Ethernet", new NetworkTrafficCounters(10, 10), start.AddSeconds(6))) is null,
            "A sample without elapsed time must not produce a rate.");

        AssertTrue(
            calculator.Calculate(
                NetworkTrafficTarget.Internet,
                new NetworkInterfaceTraffic("Ethernet", new NetworkTrafficCounters(1, 1), start)) is null,
            "Each target keeps its own baseline.");
        var internetRate = calculator.Calculate(
            NetworkTrafficTarget.Internet,
            new NetworkInterfaceTraffic("Ethernet", new NetworkTrafficCounters(2049, 3), start.AddSeconds(1)));
        AssertTrue(internetRate is not null, "The internet target should build its own baseline.");
        AssertEqual(2048d, internetRate.GetValueOrDefault().DownloadBytesPerSecond, "Targets must not share baselines.");

        calculator.ResetAll();
        AssertTrue(
            calculator.Calculate(
                NetworkTrafficTarget.Internet,
                new NetworkInterfaceTraffic("Ethernet", new NetworkTrafficCounters(9999, 9999), start.AddSeconds(2))) is null,
            "ResetAll should drop every baseline.");
        return Task.CompletedTask;
    }

    private static Task TestSettingsStoreThroughputRoundtripAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "settings.json");
            var first = new HotspotPluginSettingsStore(path);
            AssertTrue(first.Throughput.EnableSampling, "Throughput sampling should be enabled by default.");
            AssertEqual(2, first.Throughput.SamplingIntervalSeconds, "The default sampling interval should be 2 seconds.");

            first.UpdateThroughput(settings =>
            {
                settings.EnableSampling = false;
                settings.SamplingIntervalSeconds = 99;
            });
            AssertFalse(first.Throughput.EnableSampling, "The sampling flag should be updated.");
            AssertEqual(10, first.Throughput.SamplingIntervalSeconds, "Out-of-range intervals should be clamped to the allowed maximum.");

            var second = new HotspotPluginSettingsStore(path);
            AssertFalse(second.Throughput.EnableSampling, "The sampling flag should roundtrip through the settings file.");
            AssertEqual(10, second.Throughput.SamplingIntervalSeconds, "The sampling interval should roundtrip through the settings file.");

            second.UpdateThroughput(settings => settings.SamplingIntervalSeconds = 0);
            AssertEqual(1, second.Throughput.SamplingIntervalSeconds, "Intervals below the minimum should be clamped.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }


    private static Task TestThroughputSamplingServiceAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            var settingsStore = new HotspotPluginSettingsStore(Path.Combine(root, "settings.json"));
            var runtimeState = new HotspotGuardRuntimeState();
            var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 3, 27, 0, 0, 0, TimeSpan.Zero));
            var reader = new FakeNetworkTrafficReader();
            reader.Results[NetworkTrafficTarget.Hotspot] = NetworkTrafficReadResult.Success(
                NetworkTrafficTarget.Hotspot,
                "Local Area Connection* 10",
                new NetworkTrafficCounters(1024, 512),
                timeProvider.GetUtcNow());
            reader.Results[NetworkTrafficTarget.Internet] =
                NetworkTrafficReadResult.Failure(NetworkTrafficTarget.Internet, "未找到外网网卡：测试用原因。");
            var service = new HotspotThroughputBackgroundService(
                settingsStore,
                runtimeState,
                reader,
                new HotspotThroughputCalculator(),
                timeProvider);

            service.SampleOnce();

            AssertEqual(2, reader.ReadTargets.Count, "One pass should sample both the hotspot and the internet adapter.");
            AssertEqual("Local Area Connection* 10", runtimeState.HotspotThroughput.InterfaceName, "The hotspot readout should expose the resolved interface.");
            AssertTrue(runtimeState.HotspotThroughput.Throughput is null, "The first sample should not report a rate yet.");
            AssertTrue(runtimeState.HotspotThroughput.IsSampling, "The hotspot readout should be marked as sampling.");
            AssertTrue(string.IsNullOrWhiteSpace(runtimeState.HotspotThroughput.Error), "A successful read should not record an error.");
            AssertEqual("未找到外网网卡：测试用原因。", runtimeState.InternetThroughput.Error, "Read failures should be surfaced as an error readout.");
            AssertTrue(runtimeState.InternetThroughput.Throughput is null, "A failed read should not report a rate.");
            AssertTrue(
                runtimeState.LastThroughputSampleAt == timeProvider.GetUtcNow(),
                "The sample time should be recorded on the runtime state.");

            timeProvider.Advance(TimeSpan.FromSeconds(2));
            reader.Results[NetworkTrafficTarget.Hotspot] = NetworkTrafficReadResult.Success(
                NetworkTrafficTarget.Hotspot,
                "Local Area Connection* 10",
                new NetworkTrafficCounters(1024 + 4096, 512 + 2048),
                timeProvider.GetUtcNow());
            service.SampleOnce();

            var hotspotThroughput = runtimeState.HotspotThroughput.Throughput;
            AssertTrue(hotspotThroughput is not null, "The second sample should produce a rate.");
            AssertEqual(2048d, hotspotThroughput.GetValueOrDefault().DownloadBytesPerSecond, "The download rate should come from the counter delta.");
            AssertEqual(1024d, hotspotThroughput.GetValueOrDefault().UploadBytesPerSecond, "The upload rate should come from the counter delta.");

            settingsStore.UpdateThroughput(settings => settings.EnableSampling = false);
            reader.ReadTargets.Clear();
            service.SampleOnce();

            AssertEqual(0, reader.ReadTargets.Count, "Disabled sampling should not touch the network at all.");
            AssertFalse(runtimeState.HotspotThroughput.IsSampling, "Disabled sampling should mark the hotspot readout as not sampling.");
            AssertFalse(runtimeState.InternetThroughput.IsSampling, "Disabled sampling should mark the internet readout as not sampling.");
            AssertTrue(string.IsNullOrWhiteSpace(runtimeState.HotspotThroughput.Error), "Disabled sampling should not leave an error behind.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static NetworkAdapterInfo RequireAdapter(string message, NetworkAdapterInfo? adapter)
    {
        AssertTrue(adapter is not null, message);
        return adapter.GetValueOrDefault();
    }

    private static NetworkAdapterInfo CreateAdapter(
        string name,
        string description,
        long speed,
        string[] unicast,
        string[] gateways,
        NetworkInterfaceType interfaceType = NetworkInterfaceType.Ethernet,
        OperationalStatus status = OperationalStatus.Up)
    {
        return new NetworkAdapterInfo(name, description, interfaceType, status, speed, false, unicast, gateways);
    }

    private sealed class FakeNetworkTrafficReader : INetworkTrafficReader
    {
        public Dictionary<NetworkTrafficTarget, NetworkTrafficReadResult> Results { get; } = [];

        public List<NetworkTrafficTarget> ReadTargets { get; } = [];

        public NetworkTrafficReadResult Read(NetworkTrafficTarget target)
        {
            ReadTargets.Add(target);
            return Results.TryGetValue(target, out var result)
                ? result
                : NetworkTrafficReadResult.Failure(target, "未配置的采样目标。");
        }
    }


    private static TestContext CreateContext(HotspotActualState controllerState, bool autoStartGuard, GuardTargetState startupTarget)
    {
        var root = CreateTempDirectory();
        var settingsStore = new HotspotPluginSettingsStore(Path.Combine(root, "settings.json"))
        {
            AutoStartGuard = autoStartGuard,
            StartupTarget = startupTarget
        };
        var runtimeState = new HotspotGuardRuntimeState();
        var controller = new FakeHotspotController(controllerState);
        var notifier = new RecordingGuardStatusNotifier();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 3, 27, 0, 0, 0, TimeSpan.Zero));
        var coordinator = new HotspotGuardCoordinator(controller, settingsStore, runtimeState, notifier, timeProvider);

        return new TestContext(root, settingsStore, runtimeState, controller, notifier, timeProvider, coordinator);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "HotspotService.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; Actual: {actual}.");
        }
    }

    /// <summary>
    /// 等待条件成立（最多 <paramref name="timeoutMilliseconds"/> 毫秒）。
    /// 用于兼容 .NET 10 起 <c>BackgroundService</c> 把整个 <c>ExecuteAsync</c> 交给 Task 执行的语义：
    /// <c>StartAsync</c> 返回时后台服务可能尚未完成订阅等初始化动作。
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }
    }

    private sealed class FakeHotkeySource : IHotspotHotkeySource
    {
        private EventHandler<HotspotKeyEvent>? _keyPressed;

        public event EventHandler<HotspotKeyEvent>? KeyPressed
        {
            add => _keyPressed += value;
            remove => _keyPressed -= value;
        }

        /// <summary>当前订阅者数量，用于等待后台服务完成订阅。</summary>
        public int SubscriberCount => _keyPressed?.GetInvocationList().Length ?? 0;

        public bool IsAvailable { get; set; }

        public string? LastError { get; set; }

        public int AttachAttempts { get; private set; }

        /// <summary>Detach 调用次数，用于验证停机时确实断开了订阅。</summary>
        public int DetachCallCount { get; private set; }

        public bool TryAttach()
        {
            AttachAttempts++;
            if (!IsAvailable)
            {
                LastError = "未检测到 KeyboardCapture 插件（未安装或尚未加载）。";
            }

            return IsAvailable;
        }

        public void Detach()
        {
            // 可重复调用（幂等）。
            DetachCallCount++;
        }

        public void RaiseKeyPressed(string keyName, bool ctrl, bool alt, bool shift, bool meta, bool isAutoRepeat = false)
        {
            _keyPressed?.Invoke(this, new HotspotKeyEvent(keyName, ctrl, alt, shift, meta, isAutoRepeat));
        }
    }

    private sealed record TestContext(
        string RootDirectory,
        HotspotPluginSettingsStore SettingsStore,
        HotspotGuardRuntimeState RuntimeState,
        FakeHotspotController Controller,
        RecordingGuardStatusNotifier Notifier,
        ManualTimeProvider TimeProvider,
        HotspotGuardCoordinator Coordinator);

    private sealed class RecordingGuardStatusNotifier : IGuardStatusNotifier
    {
        public int NotificationCount { get; private set; }

        /// <summary>置为 true 后通知会抛出异常，用于验证通知失败不影响巡检。</summary>
        public bool ThrowOnNotify { get; set; }

        public void NotifyGuardStatusChanged()
        {
            NotificationCount++;

            if (ThrowOnNotify)
            {
                throw new InvalidOperationException("Simulated ruleset notification failure.");
            }
        }
    }

    private sealed class FakeHotspotController : IHotspotController
    {
        private int _concurrentSetCalls;

        public FakeHotspotController(HotspotActualState initialState)
        {
            CurrentState = initialState;
        }

        public HotspotActualState CurrentState { get; set; }

        public bool FailNextSet { get; set; }

        public TimeSpan SetDelay { get; set; }

        public int ConnectedClientCount { get; set; }

        public int MaxClientCount { get; set; } = 8;

        public IReadOnlyList<HotspotClientInfo> ConnectedClients { get; set; } = [];

        public HotspotSupportState TetheringSupport { get; set; } = HotspotSupportState.Supported;

        public bool FailNextClientCountRead { get; set; }

        public bool FailNextClientListRead { get; set; }

        public int GetStateCallCount { get; private set; }

        public int SetStateCallCount { get; private set; }

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public int GetClientCountCallCount { get; private set; }

        public int GetClientListCallCount { get; private set; }

        public int MaxConcurrentSetCalls { get; private set; }

        public Task<HotspotActualState> GetStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetStateCallCount++;
            return Task.FromResult(CurrentState);
        }

        public Task<int> GetConnectedClientCountAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetClientCountCallCount++;
            if (FailNextClientCountRead)
            {
                FailNextClientCountRead = false;
                throw new InvalidOperationException("Simulated client count read failure.");
            }
            return Task.FromResult(ConnectedClientCount);
        }

        public Task<int> GetMaxClientCountAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(MaxClientCount);
        }

        public Task<IReadOnlyList<HotspotClientInfo>> GetConnectedClientsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetClientListCallCount++;
            if (FailNextClientListRead)
            {
                FailNextClientListRead = false;
                throw new InvalidOperationException("Simulated client list read failure.");
            }
            return Task.FromResult(ConnectedClients);
        }

        public Task<HotspotSupportState> GetTetheringSupportAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(TetheringSupport);
        }

        public async Task SetStateAsync(GuardTargetState target, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetStateCallCount++;
            if (target == GuardTargetState.On)
            {
                StartCallCount++;
            }
            else
            {
                StopCallCount++;
            }

            var concurrent = Interlocked.Increment(ref _concurrentSetCalls);
            MaxConcurrentSetCalls = Math.Max(MaxConcurrentSetCalls, concurrent);
            try
            {
                if (SetDelay > TimeSpan.Zero)
                {
                    await Task.Delay(SetDelay, cancellationToken);
                }

                if (FailNextSet)
                {
                    FailNextSet = false;
                    throw new InvalidOperationException("Simulated hotspot failure.");
                }

                CurrentState = target.ToActualState();
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentSetCalls);
            }
        }

        public async Task RestartAsync(CancellationToken cancellationToken)
        {
            await SetStateAsync(GuardTargetState.Off, cancellationToken);
            await SetStateAsync(GuardTargetState.On, cancellationToken);
        }

        public void ResetCounts()
        {
            GetStateCallCount = 0;
            SetStateCallCount = 0;
            StartCallCount = 0;
            StopCallCount = 0;
            GetClientCountCallCount = 0;
            GetClientListCallCount = 0;
            MaxConcurrentSetCalls = 0;
            _concurrentSetCalls = 0;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public void Advance(TimeSpan delta)
        {
            _utcNow += delta;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
