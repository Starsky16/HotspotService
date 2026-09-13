using HotspotService.Models;

namespace HotspotService.Services;

/// <summary>
/// 快捷键匹配器：键名（不区分大小写）与 Ctrl/Alt/Shift/Win 四个修饰键必须完全一致，
/// 按住键产生的自动重复事件会被忽略，两次触发之间强制冷却。
/// 键盘事件来自后台线程，因此内部加锁，可并发调用。
/// </summary>
public sealed class HotspotShortcutMatcher
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset? _lastTriggeredAt;

    public HotspotShortcutMatcher(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>最近一次匹配成功的时间；从未触发时为 null。</summary>
    public DateTimeOffset? LastTriggeredAt
    {
        get
        {
            lock (_gate)
            {
                return _lastTriggeredAt;
            }
        }
    }

    /// <summary>
    /// 判断按键事件是否命中快捷键。命中时会记录触发时间用于冷却，
    /// 同一组合在冷却时间内不会再次命中。
    /// </summary>
    public bool TryMatch(HotspotShortcutSettings settings, HotspotKeyEvent keyEvent)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled || keyEvent.IsAutoRepeat)
        {
            return false;
        }

        if (!string.Equals(settings.KeyName?.Trim(), keyEvent.KeyName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (settings.Ctrl != keyEvent.Ctrl
            || settings.Alt != keyEvent.Alt
            || settings.Shift != keyEvent.Shift
            || settings.Meta != keyEvent.Meta)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        var cooldown = TimeSpan.FromSeconds(HotspotShortcutSettings.ClampCooldown(settings.CooldownSeconds));
        lock (_gate)
        {
            if (_lastTriggeredAt is { } lastTriggeredAt && now - lastTriggeredAt < cooldown)
            {
                return false;
            }

            _lastTriggeredAt = now;
            return true;
        }
    }

    /// <summary>清空触发时间（例如重新启用快捷键时）。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _lastTriggeredAt = null;
        }
    }
}
