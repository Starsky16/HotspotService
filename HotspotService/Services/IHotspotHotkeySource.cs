using HotspotService.Models;

namespace HotspotService.Services;

/// <summary>
/// 全局快捷键事件来源。真实实现对接 KeyboardCapture 插件提供的服务，
/// 测试实现可以直接投递 <see cref="HotspotKeyEvent"/>。
/// </summary>
public interface IHotspotHotkeySource
{
    /// <summary>按键按下事件。注意：事件在后台线程触发。</summary>
    event EventHandler<HotspotKeyEvent>? KeyPressed;

    /// <summary>是否已连接到 KeyboardCapture 服务。</summary>
    bool IsAvailable { get; }

    /// <summary>最近一次连接失败的原因（用于设置页展示），连接正常时为 null。</summary>
    string? LastError { get; }

    /// <summary>
    /// 尝试连接 KeyboardCapture 服务。未安装、尚未加载或版本不兼容时返回 false 且不抛异常，
    /// 可以重复调用（用于适配插件加载顺序）。
    /// </summary>
    bool TryAttach();
}
