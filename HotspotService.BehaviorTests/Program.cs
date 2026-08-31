using HotspotService.Automation;
using HotspotService.Models;
using HotspotService.Services;

namespace HotspotService.BehaviorTests;

public static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            await TestExplicitTargetApplyExecutesImmediatelyWhileGuardDisabledAsync();
            TestHotspotStateAndTargetRuleEvaluation();
            Console.WriteLine("Behavior tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task TestExplicitTargetApplyExecutesImmediatelyWhileGuardDisabledAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            var settingsStore = new HotspotPluginSettingsStore(Path.Combine(root, "settings.cfg"))
            {
                AutoStartGuard = false,
                StartupTarget = GuardTargetState.Off
            };
            var runtimeState = new HotspotGuardRuntimeState();
            var controller = new FakeHotspotController(HotspotActualState.Off);
            var coordinator = new HotspotGuardCoordinator(
                controller,
                settingsStore,
                runtimeState,
                new RecordingGuardStatusNotifier(),
                new ManualTimeProvider(new DateTimeOffset(2026, 3, 27, 0, 0, 0, TimeSpan.Zero)));

            await coordinator.InitializeAsync(CancellationToken.None);
            controller.ResetCounts();

            await coordinator.SetGuardTargetAsync(GuardTargetState.On, applyImmediately: true, cancellationToken: CancellationToken.None);

            AssertFalse(runtimeState.GuardEnabled, "Guard should remain disabled.");
            AssertEqual(GuardTargetState.On, runtimeState.GuardTarget, "Explicit apply should update the guard target.");
            AssertEqual(1, controller.StartCallCount, "Explicit apply should immediately set the hotspot.");
            AssertEqual(HotspotActualState.On, runtimeState.LastKnownHotspotState, "Runtime state should reflect the explicitly applied hotspot state.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void TestHotspotStateAndTargetRuleEvaluation()
    {
        var runtimeState = new HotspotGuardRuntimeState();
        runtimeState.SetGuardTarget(GuardTargetState.Off);
        runtimeState.SetLastKnownHotspotState(HotspotActualState.Off);

        AssertTrue(
            GuardEnabledRuleEvaluator.EvaluateSystemHotspotState(
                runtimeState,
                new SystemHotspotStateRuleSettings { ExpectedState = HotspotActualState.Off }),
            "System hotspot rule should match the current hotspot state.");
        AssertFalse(
            GuardEnabledRuleEvaluator.EvaluateSystemHotspotState(
                runtimeState,
                new SystemHotspotStateRuleSettings { ExpectedState = HotspotActualState.On }),
            "System hotspot rule should not match another hotspot state.");
        AssertTrue(
            GuardEnabledRuleEvaluator.EvaluateGuardTarget(
                runtimeState,
                new GuardTargetRuleSettings { ExpectedTarget = GuardTargetState.Off }),
            "Guard target rule should match the current target.");
        AssertFalse(
            GuardEnabledRuleEvaluator.EvaluateGuardTarget(
                runtimeState,
                new GuardTargetRuleSettings { ExpectedTarget = GuardTargetState.On }),
            "Guard target rule should not match another target.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "HotspotService.BehaviorTests", Guid.NewGuid().ToString("N"));
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

    private sealed class RecordingGuardStatusNotifier : IGuardStatusNotifier
    {
        public void NotifyGuardStatusChanged()
        {
        }
    }

    private sealed class FakeHotspotController : IHotspotController
    {
        public FakeHotspotController(HotspotActualState initialState)
        {
            CurrentState = initialState;
        }

        public HotspotActualState CurrentState { get; set; }

        public int ConnectedClientCount { get; set; }

        public int MaxClientCount { get; set; } = 8;

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public Task<HotspotActualState> GetStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CurrentState);
        }

        public Task<int> GetConnectedClientCountAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ConnectedClientCount);
        }

        public Task<int> GetMaxClientCountAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(MaxClientCount);
        }

        public Task SetStateAsync(GuardTargetState target, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target == GuardTargetState.On)
            {
                StartCallCount++;
            }
            else
            {
                StopCallCount++;
            }

            CurrentState = target.ToActualState();
            return Task.CompletedTask;
        }

        public Task RestartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCallCount++;
            CurrentState = HotspotActualState.Off;
            StartCallCount++;
            CurrentState = HotspotActualState.On;
            return Task.CompletedTask;
        }

        public void ResetCounts()
        {
            StartCallCount = 0;
            StopCallCount = 0;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
