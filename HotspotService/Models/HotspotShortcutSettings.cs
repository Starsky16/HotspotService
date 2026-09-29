namespace HotspotService.Models;

/// <summary>
/// 一组全局快捷键设置。通过 KeyboardCapture 插件（可选依赖）订阅全局按键，
/// 命中组合键时执行对应动作（重启热点 / 切换守护开关 / 切换守护目标）；
/// 三组设置各自独立保存与冷却，默认均关闭，需要用户显式开启。
/// </summary>
public sealed class HotspotShortcutSettings
{
    /// <summary>允许的最小触发冷却（秒）。</summary>
    public const int MinimumCooldownSeconds = 1;

    /// <summary>允许的最大触发冷却（秒）。</summary>
    public const int MaximumCooldownSeconds = 600;

    /// <summary>是否启用快捷键。默认关闭，避免在用户不知情时占用按键。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 触发键名，与 KeyboardCapture 的 <c>KeyboardKey.Name</c> 一致，例如 <c>A</c>、<c>F9</c>、<c>Up</c>。
    /// </summary>
    public string KeyName { get; set; } = HotspotShortcutKeys.DefaultRestartKey;

    /// <summary>是否要求按下 Ctrl。</summary>
    public bool Ctrl { get; set; } = true;

    /// <summary>是否要求按下 Alt。</summary>
    public bool Alt { get; set; } = true;

    /// <summary>是否要求按下 Shift。</summary>
    public bool Shift { get; set; } = false;

    /// <summary>是否要求按下 Win（Meta）键。</summary>
    public bool Meta { get; set; } = false;

    /// <summary>两次触发之间的冷却秒数，避免连击导致连续重启。</summary>
    public int CooldownSeconds { get; set; } = 5;

    /// <summary>把任意整数冷却秒数收进合法区间。</summary>
    public static int ClampCooldown(int value) =>
        Math.Clamp(value, MinimumCooldownSeconds, MaximumCooldownSeconds);

    /// <summary>组合键的可读描述，例如 <c>Ctrl+Alt+F9</c>。</summary>
    public string DescribeShortcut()
    {
        var parts = new List<string>(5);
        if (Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Meta)
        {
            parts.Add("Win");
        }

        parts.Add(string.IsNullOrWhiteSpace(KeyName) ? HotspotShortcutKeys.DefaultRestartKey : KeyName.Trim());
        return string.Join("+", parts);
    }
}
