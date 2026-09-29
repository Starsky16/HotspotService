using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using HotspotService.Models;
using HotspotService.Services;

namespace HotspotService.Components;

/// <summary>
/// 主界面极简展示组件：实心圆点颜色表示守护开关（绿=开启，红=关闭），
/// 旁边显示热点状态（不存在无线网卡时显示 “None”，开启时显示连接设备数，关闭时显示 “Off”）。
/// 网速按「下行一行、上行一行」的小字并排展示热点与外网两路；圆点与文字可在组件设置中独立开关，设置修改即时生效。
/// </summary>
[ComponentInfo(
    PluginIds.HotspotStatusComponent,
    "移动热点守护",
    PluginIds.WifiGlyph,
    "以圆点与文字展示守护状态与热点状态（不存在无线网卡时显示 None，关闭时显示 Off），并可显示热点/外网网速。")]
public sealed class HotspotStatusComponent : ComponentBase<HotspotStatusComponentSettings>
{
    /// <summary>组件内小字使用的宿主机字号资源键（ThemeBase.axaml 中的次级字号，默认 14）。</summary>
    private const string SecondaryFontSizeResourceKey = "MainWindowSecondaryFontSize";

    private static readonly Color EnabledDotColor = Color.FromRgb(0x66, 0xBB, 0x6A);
    private static readonly Color DisabledDotColor = Color.FromRgb(0xEF, 0x53, 0x50);
    private static readonly IBrush EnabledDotBrush = new SolidColorBrush(EnabledDotColor);
    private static readonly IBrush DisabledDotBrush = new SolidColorBrush(DisabledDotColor);

    private readonly HotspotGuardRuntimeState _runtimeState;
    private readonly TextBlock _statusDotText = new();
    private readonly TextBlock _clientCountText = new();

    /// <summary>网速区域：热点两行块与外网（WAN 前缀 + 两行块）水平并排。</summary>
    private readonly StackPanel _throughputPanel = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly StackPanel _hotspotLines = CreateThroughputLines();
    private readonly StackPanel _wanBlock = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly TextBlock _hotspotDownText = CreateThroughputLine();
    private readonly TextBlock _hotspotUpText = CreateThroughputLine();
    private readonly TextBlock _wanLabelText = CreateThroughputLine();
    private readonly TextBlock _wanDownText = CreateThroughputLine();
    private readonly TextBlock _wanUpText = CreateThroughputLine();
    private HotspotStatusComponentSettings? _subscribedSettings;
    private bool _subscribedRuntimeState;

    public HotspotStatusComponent(HotspotGuardRuntimeState runtimeState)
    {
        _runtimeState = runtimeState;

        _hotspotLines.Children.Add(_hotspotDownText);
        _hotspotLines.Children.Add(_hotspotUpText);
        _wanLabelText.Text = "WAN";
        var wanLines = CreateThroughputLines();
        wanLines.Children.Add(_wanDownText);
        wanLines.Children.Add(_wanUpText);
        _wanBlock.Children.Add(_wanLabelText);
        _wanBlock.Children.Add(wanLines);
        _throughputPanel.Children.Add(_hotspotLines);
        _throughputPanel.Children.Add(_wanBlock);

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
        panel.Children.Add(_throughputPanel);
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

    /// <summary>
    /// 创建一行网速小字。字号动态绑定宿主机的次级字号资源
    /// （<c>ThemeBase.axaml</c> 的 <c>MainWindowSecondaryFontSize</c>，默认 14，由宿主注入主窗口）；
    /// 资源缺失时回落到继承的正文字号，不会抛异常。
    /// </summary>
    private static TextBlock CreateThroughputLine()
    {
        var text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        var style = new Style(x => x.OfType<TextBlock>());
        style.Setters.Add(new Setter(
            TextBlock.FontSizeProperty,
            new DynamicResourceExtension(SecondaryFontSizeResourceKey)));
        text.Styles.Add(style);
        return text;
    }

    /// <summary>承载「下行一行、上行一行」的垂直容器。</summary>
    private static StackPanel CreateThroughputLines()
    {
        return new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center
        };
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
        // 不存在无线网卡或状态未知显示 None；热点开启显示真实连接数；关闭/切换中显示 Off。
        _clientCountText.Text = _runtimeState.TetheringSupport.ToComponentDisplayText(
            _runtimeState.LastKnownHotspotState,
            _runtimeState.ConnectedClientCount);

        // 网速：每路都是「下行一行、上行一行」，热点与外网两块水平并排；两路都不可见时整块隐藏。
        var hotspotSegment = NetworkSpeedFormatter.FormatComponentSegment(_runtimeState.HotspotThroughput);
        var showHotspot = (settings?.ShowHotspotThroughput ?? true) && hotspotSegment is not null;
        if (hotspotSegment is { } hotspot)
        {
            _hotspotDownText.Text = hotspot.Down;
            _hotspotUpText.Text = hotspot.Up;
        }

        var internetSegment = NetworkSpeedFormatter.FormatComponentSegment(_runtimeState.InternetThroughput);
        var showInternet = (settings?.ShowInternetThroughput ?? false) && internetSegment is not null;
        if (internetSegment is { } internet)
        {
            _wanDownText.Text = internet.Down;
            _wanUpText.Text = internet.Up;
        }

        _hotspotLines.IsVisible = showHotspot;
        _wanBlock.IsVisible = showInternet;
        _throughputPanel.IsVisible = showHotspot || showInternet;
    }
}