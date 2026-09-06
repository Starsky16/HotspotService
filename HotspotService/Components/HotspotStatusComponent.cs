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
/// 主界面极简展示组件：实心/空心圆点表示守护是否开启，旁边以常规字号显示连接设备数量。
/// 圆点与连接数可分别在组件设置中独立开关，设置修改即时生效。
/// </summary>
[ComponentInfo(
    PluginIds.HotspotStatusComponent,
    "移动热点守护",
    PluginIds.WifiGlyph,
    "以实心/空心圆点与连接设备数展示移动热点守护状态。")]
public sealed class HotspotStatusComponent : ComponentBase<HotspotStatusComponentSettings>
{
    private static readonly Color EnabledDotColor = Color.FromRgb(0x2E, 0xA8, 0x4A);

    private readonly HotspotGuardRuntimeState _runtimeState;
    private readonly TextBlock _statusDotText = new();
    private readonly TextBlock _clientCountText = new();
    private HotspotStatusComponentSettings? _subscribedSettings;

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
        panel.Children.Add(_statusDotText);
        panel.Children.Add(_clientCountText);
        Content = panel;

        _runtimeState.PropertyChanged += OnRuntimeStatePropertyChanged;
    }

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

        UpdateUi();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _runtimeState.PropertyChanged -= OnRuntimeStatePropertyChanged;

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
            // 守护开启：实心圆点。
            _statusDotText.Text = "\u25CF";
            _statusDotText.Opacity = 1.0;
            _statusDotText.Foreground = new SolidColorBrush(EnabledDotColor);
        }
        else
        {
            // 守护关闭：空心圆点。
            _statusDotText.Text = "\u25CB";
            _statusDotText.Foreground = null;
            _statusDotText.Opacity = 0.6;
        }

        _clientCountText.IsVisible = settings?.ShowClientCount ?? true;
        _clientCountText.Text = _runtimeState.ConnectedClientCount.ToString();
    }
}


