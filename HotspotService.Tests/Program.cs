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
        await RunTestAsync("Failed manual restart propagates exception", TestManualRestartFailurePropagatesAsync);

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

        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(1, context.Controller.GetClientCountCallCount, "Each periodic check should read the client count exactly once.");
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

        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(1, context.Controller.StopCallCount, "First restart should occur.");

        context.Controller.ConnectedClientCount = 0;
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);
        AssertEqual(1, context.Controller.StopCallCount, "No restart should occur while the condition is cleared.");

        context.Controller.ConnectedClientCount = 4;
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
        await context.Coordinator.RunPeriodicCheckAsync(CancellationToken.None);

        AssertEqual(0, context.Controller.SetStateCallCount, "Client count read failure should not affect the main sync.");
        AssertTrue(string.IsNullOrWhiteSpace(context.RuntimeState.LastError), "Client count read failure should not record a sync error.");
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

        public void NotifyGuardStatusChanged()
        {
            NotificationCount++;
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

        public bool FailNextClientCountRead { get; set; }

        public int GetStateCallCount { get; private set; }

        public int SetStateCallCount { get; private set; }

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public int GetClientCountCallCount { get; private set; }

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
