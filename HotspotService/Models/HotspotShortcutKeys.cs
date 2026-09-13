namespace HotspotService.Models;

/// <summary>
/// 设置页可选的触发键候选。键名取自 KeyboardCapture 的
/// <c>KeyboardKeys</c> 常量与 UIOHook 键名约定（字母、数字直接用单个字符）。
/// </summary>
public static class HotspotShortcutKeys
{
    /// <summary>全部候选键名，顺序为字母、数字、功能键、编辑键、锁定键。</summary>
    public static IReadOnlyList<string> All { get; } = BuildAll();

    /// <summary>键名是否在候选列表中（不区分大小写）。</summary>
    public static bool Contains(string? keyName)
    {
        return !string.IsNullOrWhiteSpace(keyName)
               && All.Any(x => string.Equals(x, keyName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> BuildAll()
    {
        var keys = new List<string>(26 + 10 + 12 + 21);
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            keys.Add(letter.ToString());
        }

        for (var digit = '0'; digit <= '9'; digit++)
        {
            keys.Add(digit.ToString());
        }

        for (var index = 1; index <= 12; index++)
        {
            keys.Add($"F{index}");
        }

        keys.AddRange(
        [
            "Up", "Down", "Left", "Right",
            "Space", "Enter", "Escape", "Tab", "Backspace", "Delete", "Insert",
            "Home", "End", "PageUp", "PageDown",
            "CapsLock", "NumLock", "ScrollLock", "PrintScreen", "Pause"
        ]);

        return keys;
    }
}
