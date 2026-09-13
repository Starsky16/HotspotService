namespace HotspotService.Models;

/// <summary>
/// 与具体键盘插件解耦的按键事件快照：由 KeyboardCapture 的事件参数转换而来，
/// 使快捷键匹配逻辑可以在不启动键盘钩子的情况下自测。
/// </summary>
public readonly record struct HotspotKeyEvent(
    string KeyName,
    bool Ctrl,
    bool Alt,
    bool Shift,
    bool Meta,
    bool IsAutoRepeat);
