using ClassIsland.Shared;
using HotspotService.Models;
using KeyboardCapture.Abstractions;

namespace HotspotService.Services;

/// <summary>
/// 通过 ClassIsland 的 <see cref="IAppHost"/> 连接 KeyboardCapture 插件提供的
/// <see cref="IKeyboardCaptureService"/>，并把键盘事件转换成与插件解耦的 <see cref="HotspotKeyEvent"/>。
/// </summary>
/// <remarks>
/// KeyboardCapture 在插件清单中声明为**可选依赖**（isRequired: false），因此这里：
/// <list type="bullet">
/// <item>不通过构造函数注入（未安装该插件时注入会直接导致本插件加载失败），
/// 而是按官方建议使用 <see cref="IAppHost.TryGetService{T}"/> 惰性获取；</item>
/// <item>插件可能晚于本插件加载，因此允许重复调用 <see cref="TryAttach"/>；</item>
/// <item>连接与事件转换都不抛异常，失败原因记录在 <see cref="LastError"/> 里供设置页展示。</item>
/// </list>
/// </remarks>
public sealed class KeyboardCaptureHotkeySource : IHotspotHotkeySource
{
    private readonly object _gate = new();
    private IKeyboardCaptureService? _service;

    /// <inheritdoc />
    public event EventHandler<HotspotKeyEvent>? KeyPressed;

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _service is not null;
            }
        }
    }

    /// <inheritdoc />
    public string? LastError { get; private set; }

    /// <inheritdoc />
    public bool TryAttach()
    {
        lock (_gate)
        {
            if (_service is not null)
            {
                return true;
            }
        }

        try
        {
            var service = IAppHost.TryGetService<IKeyboardCaptureService>();
            if (service is null)
            {
                LastError = "未检测到 KeyboardCapture 插件（未安装或尚未加载）。";
                return false;
            }

            service.KeyDown += OnKeyDown;
            lock (_gate)
            {
                _service = service;
            }

            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            // 例如宿主未初始化（IAppHost.Host 为空）或接口版本不兼容。
            LastError = $"连接 KeyboardCapture 失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>取消订阅（服务停止时调用）。</summary>
    public void Detach()
    {
        IKeyboardCaptureService? service;
        lock (_gate)
        {
            service = _service;
            _service = null;
        }

        if (service is null)
        {
            return;
        }

        try
        {
            service.KeyDown -= OnKeyDown;
        }
        catch (Exception ex)
        {
            LastError = $"取消订阅 KeyboardCapture 失败：{ex.Message}";
        }
    }

    private void OnKeyDown(object? sender, KeyboardKeyEventArgs e)
    {
        if (!e.IsKeyDown)
        {
            return;
        }

        var keyEvent = new HotspotKeyEvent(
            e.Key.Name,
            e.Modifiers.HasFlag(KeyModifiers.Ctrl),
            e.Modifiers.HasFlag(KeyModifiers.Alt),
            e.Modifiers.HasFlag(KeyModifiers.Shift),
            e.Modifiers.HasFlag(KeyModifiers.Meta),
            e.IsAutoRepeat);

        try
        {
            KeyPressed?.Invoke(this, keyEvent);
        }
        catch (Exception ex)
        {
            // 订阅方异常不应中断 KeyboardCapture 的钩子分发。
            LastError = $"处理按键事件失败：{ex.Message}";
        }
    }
}
