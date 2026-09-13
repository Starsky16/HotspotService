using HotspotService.Models;
using Microsoft.Extensions.Hosting;

namespace HotspotService.Services;

/// <summary>
/// 快捷键重启热点的后台服务：
/// <list type="bullet">
/// <item>周期性尝试连接 KeyboardCapture（可选依赖，可能晚于本插件加载或运行中才被启用）；</item>
/// <item>把连接状态与最近触发结果写入运行状态，供设置页展示；</item>
/// <item>命中快捷键后在线程池执行一次热点重启，不阻塞键盘钩子线程。</item>
/// </list>
/// </summary>
public sealed class HotspotShortcutHostedService : BackgroundService
{
    /// <summary>未连接时的重试间隔。</summary>
    private static readonly TimeSpan AttachRetryInterval = TimeSpan.FromSeconds(5);

    private readonly HotspotShortcutMatcher _matcher;
    private readonly IHotspotHotkeySource _hotkeySource;
    private readonly HotspotPluginSettingsStore _settingsStore;
    private readonly HotspotGuardRuntimeState _runtimeState;
    private readonly HotspotGuardCoordinator _coordinator;

    public HotspotShortcutHostedService(
        HotspotShortcutMatcher matcher,
        IHotspotHotkeySource hotkeySource,
        HotspotPluginSettingsStore settingsStore,
        HotspotGuardRuntimeState runtimeState,
        HotspotGuardCoordinator coordinator)
    {
        _matcher = matcher;
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
    /// 处理一次按键事件：命中快捷键时重启热点。
    /// 返回的任务在重启结束后完成，自测程序据此验证完整触发链路。
    /// </summary>
    public async Task HandleKeyEventAsync(HotspotKeyEvent keyEvent)
    {
        if (!_matcher.TryMatch(_settingsStore.Shortcut, keyEvent))
        {
            return;
        }

        _runtimeState.SetLastShortcutTriggeredAt(_matcher.LastTriggeredAt);
        await Task.Run(RestartForShortcutAsync);
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
}
