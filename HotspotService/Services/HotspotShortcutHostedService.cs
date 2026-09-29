using HotspotService.Models;
using Microsoft.Extensions.Hosting;

namespace HotspotService.Services;

/// <summary>
/// 全局快捷键后台服务：
/// <list type="bullet">
/// <item>周期性尝试连接 KeyboardCapture（可选依赖，可能晚于本插件加载或运行中才被启用）；</item>
/// <item>把连接状态与最近触发结果写入运行状态，供设置页展示；</item>
/// <item>命中快捷键后在线程池执行对应动作（重启热点 / 切换守护开关 / 切换守护目标），不阻塞键盘钩子线程。</item>
/// </list>
/// 三组快捷键按「重启热点 → 切换守护开关 → 切换守护目标」的顺序依次匹配，一次按键最多触发一个动作。
/// </summary>
public sealed class HotspotShortcutHostedService : BackgroundService
{
    /// <summary>未连接时的重试间隔。</summary>
    private static readonly TimeSpan AttachRetryInterval = TimeSpan.FromSeconds(5);

    private readonly HotspotShortcutMatchers _matchers;
    private readonly IHotspotHotkeySource _hotkeySource;
    private readonly HotspotPluginSettingsStore _settingsStore;
    private readonly HotspotGuardRuntimeState _runtimeState;
    private readonly HotspotGuardCoordinator _coordinator;

    public HotspotShortcutHostedService(
        HotspotShortcutMatchers matchers,
        IHotspotHotkeySource hotkeySource,
        HotspotPluginSettingsStore settingsStore,
        HotspotGuardRuntimeState runtimeState,
        HotspotGuardCoordinator coordinator)
    {
        _matchers = matchers;
        _hotkeySource = hotkeySource;
        _settingsStore = settingsStore;
        _runtimeState = runtimeState;
        _coordinator = coordinator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _hotkeySource.KeyPressed += OnKeyPressed;
        try
        {
            RefreshSourceState();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(AttachRetryInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                RefreshSourceState();
            }
        }
        finally
        {
            _hotkeySource.KeyPressed -= OnKeyPressed;

            // 停机时必须断开对宿主级单例 KeyboardCapture 服务的订阅，否则单例会一直持有本实例。
            _hotkeySource.Detach();
        }
    }

    /// <summary>
    /// 尝试连接快捷键来源并把状态同步到运行状态。
    /// 自测程序也用该入口验证「未安装 KeyboardCapture」时的降级行为。
    /// </summary>
    public void RefreshSourceState()
    {
        if (!_hotkeySource.IsAvailable)
        {
            _hotkeySource.TryAttach();
        }

        _runtimeState.SetShortcutSourceAvailable(_hotkeySource.IsAvailable);
        _runtimeState.SetShortcutSourceMessage(_hotkeySource.LastError);
    }

    private void OnKeyPressed(object? sender, HotspotKeyEvent keyEvent)
    {
        // 事件由键盘钩子线程触发，转到线程池执行，避免阻塞按键分发。
        _ = HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// 处理一次按键事件：命中哪一组快捷键就执行哪个动作。
    /// 返回的任务在动作结束后完成，自测程序据此验证完整触发链路。
    /// </summary>
    public async Task HandleKeyEventAsync(HotspotKeyEvent keyEvent)
    {
        if (_matchers.Restart.TryMatch(_settingsStore.Shortcut, keyEvent))
        {
            _runtimeState.SetLastShortcutTriggeredAt(_matchers.Restart.LastTriggeredAt);
            await Task.Run(RestartForShortcutAsync);
            return;
        }

        if (_matchers.GuardToggle.TryMatch(_settingsStore.GuardToggleShortcut, keyEvent))
        {
            _runtimeState.SetLastGuardToggleTriggeredAt(_matchers.GuardToggle.LastTriggeredAt);
            await Task.Run(ToggleGuardForShortcutAsync);
            return;
        }

        if (_matchers.GuardTargetToggle.TryMatch(_settingsStore.GuardTargetToggleShortcut, keyEvent))
        {
            _runtimeState.SetLastGuardTargetToggleTriggeredAt(_matchers.GuardTargetToggle.LastTriggeredAt);
            await Task.Run(ToggleGuardTargetForShortcutAsync);
        }
    }

    /// <summary>
    /// 执行一次快捷键触发的热点重启。失败原因写入运行状态，不向调用方抛出；
    /// 状态变化时的规则集通知由 <see cref="HotspotGuardCoordinator.RestartHotspotAsync"/> 内部负责。
    /// </summary>
    public async Task RestartForShortcutAsync()
    {
        try
        {
            await _coordinator.RestartHotspotAsync(CancellationToken.None);
            _runtimeState.SetLastShortcutError(null);
        }
        catch (Exception ex)
        {
            _runtimeState.SetLastShortcutError(ex.Message);
        }
    }

    /// <summary>
    /// 执行一次快捷键触发的守护开关切换：取当前状态的反值交给协调器，
    /// 由协调器负责同步热点与通知规则集。失败原因写入运行状态，不向调用方抛出。
    /// </summary>
    public async Task ToggleGuardForShortcutAsync()
    {
        try
        {
            var enabled = !_runtimeState.GuardEnabled;
            await _coordinator.SetGuardEnabledAsync(enabled, CancellationToken.None);
            _runtimeState.SetLastGuardToggleError(null);
        }
        catch (Exception ex)
        {
            _runtimeState.SetLastGuardToggleError(ex.Message);
        }
    }

    /// <summary>
    /// 执行一次快捷键触发的守护目标切换：在「要热点开」与「要热点关」之间翻转。
    /// 翻转值取自运行状态（而非启动设置）；守护开启时协调器会立即把热点拉到新目标，
    /// 守护关闭时只更新目标、不干预系统热点（与设置页「守护目标」下拉的语义一致）。
    /// 失败原因写入运行状态，不向调用方抛出。
    /// </summary>
    public async Task ToggleGuardTargetForShortcutAsync()
    {
        try
        {
            var next = _runtimeState.GuardTarget == GuardTargetState.On
                ? GuardTargetState.Off
                : GuardTargetState.On;
            await _coordinator.SetGuardTargetAsync(next, CancellationToken.None);
            _runtimeState.SetLastGuardTargetToggleError(null);
        }
        catch (Exception ex)
        {
            _runtimeState.SetLastGuardTargetToggleError(ex.Message);
        }
    }
}