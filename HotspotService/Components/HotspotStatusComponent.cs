using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using HotspotService.Models;
using HotspotService.Services;

namespace HotspotService.Components;

/// <summary>
/// 主界面极简展示组件：实心圆点颜色表示守护开关（绿=开启，红=关闭），
/// 旁边显示连接设备数量；设备不支持热点时显示 “None”，热点关闭时显示 “Off”。
/// 圆点与文字可在组件设置中独立开关，设置修改即时生效。
/// </summary>
[ComponentInfo(
    PluginIds.HotspotStatusComponent,
    "移动热点守护",
    PluginIds.WifiGlyph,
    "以圆点与文字展示守护状态与连接设备数（不支持时显示 None，关闭时显示 Off），并可显示热点/外网网速。")]
public sealed class HotspotStatusComponent : ComponentBase<HotspotStatusComponentSettings>
{
    private static readonly Color EnabledDotColor = Color.FromRgb(0x66, 0xBB, 0x6A);
    private static readonly Color DisabledDotColor = Color.FromRgb(0xEF, 0x53, 0x50);
    private static readonly IBrush EnabledDotBrush = new SolidColorBrush(EnabledDotColor);
    private static readonly IBrush DisabledDotBrush = new SolidColorBrush(DisabledDotColor);

    private readonly HotspotGuardRuntimeState _runtimeState;
    private readonly TextBlock _statusDotText = new();
    private readonly TextBlock _clientCountText = new();
    private readonly TextBlock _throughputText = new();
    private HotspotStatusComponentSettings? _subscribedSettings;
    private bool _subscribedRuntimeState;

    public HotspotStatusComponent(HotspotGuardRuntimeState runtimeState)
    {
        _runtimeState = runtimeState;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        _statusDotText.VerticalAlignment = VerticalAlignment.Center;
        _clientCountText.VerticalAlignment = VerticalAlignment.Center;
        _throughputText.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(_statusDotText);
        panel.Children.Add(_clientCountText);
        panel.Children.Add(_throughputText);
        Content = panel;
    }

    /// <summary>
    /// 订阅随视觉树建立/解除：运行时状态是单例，而宿主按瞬态解析组件（每次重建都是新实例），
    /// 若只在构造函数订阅，单例会永久持有所有已离树的旧实例。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 订阅设置变化，使组件设置界面里的开关能即时反映到当前组件上。
        var settings = Settings;
        if (!ReferenceEquals(settings, _subscribedSettings))
        {
            if (_subscribedSettings is not null)
            {
                _subscribedSettings.PropertyChanged -= OnSettingsPropertyChanged;
            }

            if (settings is not null)
            {
                settings.PropertyChanged += OnSettingsPropertyChanged;
            }

            _subscribedSettings = settings;
        }

        if (!_subscribedRuntimeState)
        {
            _runtimeState.PropertyChanged += OnRuntimeStatePropertyChanged;
            _subscribedRuntimeState = true;
        }

        UpdateUi();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_subscribedRuntimeState)
        {
            _runtimeState.PropertyChanged -= OnRuntimeStatePropertyChanged;
            _subscribedRuntimeState = false;
        }

        if (_subscribedSettings is not null)
        {
            _subscribedSettings.PropertyChanged -= OnSettingsPropertyChanged;
            _subscribedSettings = null;
        }
    }

    private void OnRuntimeStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateUi);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateUi);
    }

    private void UpdateUi()
    {
        var settings = Settings;

        _statusDotText.IsVisible = settings?.ShowStatusDot ?? true;
        if (_runtimeState.GuardEnabled)
        {
            // 守护开启：绿色实心圆点。
            _statusDotText.Text = "\u25CF";
            _statusDotText.Opacity = 1.0;
            _statusDotText.Foreground = EnabledDotBrush;
        }
        else
        {
            // 守护关闭：红色实心圆点。
            _statusDotText.Text = "\u25CF";
            _statusDotText.Opacity = 1.0;
            _statusDotText.Foreground = DisabledDotBrush;
        }

        _clientCountText.IsVisible = settings?.ShowClientCount ?? true;
        // 设备不支持热点（如无 Wi-Fi 网卡）显示 None；热点关闭显示 Off；运行中显示真实连接数。
        _clientCountText.Text =
            _runtimeState.TetheringSupport == HotspotSupportState.NotSupported ? "None"
            : _runtimeState.LastKnownHotspotState == HotspotActualState.Off ? "Off"
            : _runtimeState.ConnectedClientCount.ToString();

        // 网速文本：热点网卡显示 “↓下行 ↑上行”，外网网卡额外带 WAN 前缀；两个目标都不可见时整块隐藏。
        var throughputText = NetworkSpeedFormatter.FormatComponentText(
            _runtimeState.HotspotThroughput,
            _runtimeState.InternetThroughput,
            settings?.ShowHotspotThroughput ?? true,
            settings?.ShowInternetThroughput ?? false);
        _throughputText.Text = throughputText;
        _throughputText.IsVisible = throughputText.Length > 0;
    }
}
