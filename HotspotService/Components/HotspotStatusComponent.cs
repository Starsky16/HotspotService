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
/// 主界面展示组件：实时展示移动热点守护状态、系统热点状态与连接设备数量。
/// </summary>
[ComponentInfo(
    PluginIds.HotspotStatusComponent,
    "移动热点守护",
    PluginIds.WifiGlyph,
    "显示移动热点守护状态、系统热点状态与连接设备数量。")]
public sealed class HotspotStatusComponent : ComponentBase<HotspotStatusComponentSettings>
{
    private readonly HotspotGuardRuntimeState _runtimeState;
    private readonly Grid _guardEnabledRow;
    private readonly Grid _hotspotStateRow;
    private readonly Grid _clientCountRow;
    private readonly Grid _lastErrorRow;
    private readonly TextBlock _guardEnabledValue = new();
    private readonly TextBlock _hotspotStateValue = new();
    private readonly TextBlock _clientCountValue = new();
    private readonly TextBlock _lastErrorValue = new();

    public HotspotStatusComponent(HotspotGuardRuntimeState runtimeState)
    {
        _runtimeState = runtimeState;

        var panel = new StackPanel
        {
            Spacing = 6
        };
        panel.Children.Add(new TextBlock
        {
            Text = "移动热点守护",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        });

        _guardEnabledRow = CreateStatusRow("守护状态", _guardEnabledValue);
        _hotspotStateRow = CreateStatusRow("系统热点", _hotspotStateValue);
        _clientCountRow = CreateStatusRow("连接设备", _clientCountValue);
        _lastErrorRow = CreateStatusRow("最近错误", _lastErrorValue, wrapValue: true);

        panel.Children.Add(_guardEnabledRow);
        panel.Children.Add(_hotspotStateRow);
        panel.Children.Add(_clientCountRow);
        panel.Children.Add(_lastErrorRow);

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
        var settings = Settings;
        _guardEnabledValue.Text = _runtimeState.GuardEnabled ? "已开启" : "已关闭";
        _hotspotStateValue.Text = _runtimeState.LastKnownHotspotState.ToDisplayText();

        _clientCountRow.IsVisible = settings?.ShowClientCount ?? true;
        _clientCountValue.Text = $"{_runtimeState.ConnectedClientCount} / {_runtimeState.MaxClientCount}";

        _lastErrorRow.IsVisible = settings?.ShowLastError ?? false;
        _lastErrorValue.Text = string.IsNullOrWhiteSpace(_runtimeState.LastError) ? "无" : _runtimeState.LastError;
    }

    private static Grid CreateStatusRow(string label, TextBlock valueBlock, bool wrapValue = false)
    {
        valueBlock.TextWrapping = wrapValue ? TextWrapping.Wrap : TextWrapping.NoWrap;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };
        grid.Children.Add(new TextBlock
        {
            Text = $"{label}：",
            Opacity = 0.75
        });
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);
        return grid;
    }
}
