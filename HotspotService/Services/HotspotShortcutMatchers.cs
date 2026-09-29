using HotspotService.Models;

namespace HotspotService.Services;

/// <summary>
/// 三组全局快捷键各自的匹配器：重启热点、切换守护开关、切换守护目标。
/// 每组独立持有冷却时间，互不影响（同一按键分别配置到两组时不会互相压制）。
/// </summary>
public sealed class HotspotShortcutMatchers
{
    public HotspotShortcutMatchers(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        Restart = new HotspotShortcutMatcher(timeProvider);
        GuardToggle = new HotspotShortcutMatcher(timeProvider);
        GuardTargetToggle = new HotspotShortcutMatcher(timeProvider);
    }

    /// <summary>「重启热点」快捷键的匹配器。</summary>
    public HotspotShortcutMatcher Restart { get; }

    /// <summary>「切换守护开关」快捷键的匹配器。</summary>
    public HotspotShortcutMatcher GuardToggle { get; }

    /// <summary>「切换守护目标」快捷键的匹配器。</summary>
    public HotspotShortcutMatcher GuardTargetToggle { get; }
}