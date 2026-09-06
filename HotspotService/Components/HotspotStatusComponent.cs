using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using HotspotService.Services;

namespace HotspotService.Components;

/// <summary>
/// 主界面极简展示组件：实心/空心圆点表示守护是否开启，旁边以大号纯数字显示连接设备数量。
/// </summary>
[ComponentInfo(
    PluginIds.HotspotStatusComponent,
    "移动热点守护",
    PluginIds.WifiGlyph,
    "以实心/空心圆点与连接设备数展示移动热点守护状态。")]
public sealed class HotspotStatusComponent : ComponentBase
{
    private static readonly Color EnabledDotColor = Color.FromRgb(0x2E, 0xA8, 0x4A);

    private readonly HotspotGuardRuntimeState _runtimeState;
    private readonly TextBlock _statusDotText = new();
    private readonly TextBlock _clientCountText = new();

    public HotspotStatusComponent(HotspotGuardRuntimeState runtimeState)
    {
        _runtimeState = runtimeState;

        _clientCountText.FontSize = 32;
        _clientCountText.FontWeight = FontWeight.SemiBold;
        _clientCountText.VerticalAlignment = VerticalAlignment.Center;
        _clientCountText.TextAlignment = TextAlignment.Center;

        _statusDotText.FontSize = 20;
        _statusDotText.VerticalAlignment = VerticalAlignment.Center;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        _statusDotText.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(_statusDotText);
        panel.Children.Add(_clientCountText);
        Content = panel;

        _runtimeState.PropertyChanged += OnRuntimeStatePropertyChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateUi();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _runtimeState.PropertyChanged -= OnRuntimeStatePropertyChanged;
    }

    private void OnRuntimeStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateUi);
    }

    private void UpdateUi()
    {
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

        _clientCountText.Text = _runtimeState.ConnectedClientCount.ToString();
    }
}

