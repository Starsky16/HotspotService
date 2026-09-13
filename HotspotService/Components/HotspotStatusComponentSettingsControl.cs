using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ClassIsland.Core.Abstractions.Controls;
using HotspotService.Models;

namespace HotspotService.Components;

/// <summary>
/// 主界面展示组件「移动热点守护」的设置界面。
/// </summary>
public sealed class HotspotStatusComponentSettingsControl : ComponentBase<HotspotStatusComponentSettings>
{
    private readonly CheckBox _showStatusDotCheckBox = new()
    {
        Content = "显示开启状态圆点"
    };
    private readonly CheckBox _showClientCountCheckBox = new()
    {
        Content = "显示设备数量（不支持时显示 None，关闭时显示 Off）"
    };
    private readonly CheckBox _showHotspotThroughputCheckBox = new()
    {
        Content = "显示热点网速（↓下行 ↑上行，需先在插件设置页启用网速采样）"
    };
    private readonly CheckBox _showInternetThroughputCheckBox = new()
    {
        Content = "显示外网(WAN)网速"
    };
    private bool _updatingUi;

    public HotspotStatusComponentSettingsControl()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            Spacing = 8
        };
        panel.Children.Add(new TextBlock
        {
            Text = "显示内容"
        });

        _showStatusDotCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            Settings.ShowStatusDot = _showStatusDotCheckBox.IsChecked == true;
        };
        panel.Children.Add(_showStatusDotCheckBox);

        _showClientCountCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            Settings.ShowClientCount = _showClientCountCheckBox.IsChecked == true;
        };
        panel.Children.Add(_showClientCountCheckBox);

        _showHotspotThroughputCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            Settings.ShowHotspotThroughput = _showHotspotThroughputCheckBox.IsChecked == true;
        };
        panel.Children.Add(_showHotspotThroughputCheckBox);

        _showInternetThroughputCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            Settings.ShowInternetThroughput = _showInternetThroughputCheckBox.IsChecked == true;
        };
        panel.Children.Add(_showInternetThroughputCheckBox);

        Content = panel;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateUi();
    }

    private void UpdateUi()
    {
        _updatingUi = true;
        try
        {
            _showStatusDotCheckBox.IsChecked = Settings.ShowStatusDot;
            _showClientCountCheckBox.IsChecked = Settings.ShowClientCount;
            _showHotspotThroughputCheckBox.IsChecked = Settings.ShowHotspotThroughput;
            _showInternetThroughputCheckBox.IsChecked = Settings.ShowInternetThroughput;
        }
        finally
        {
            _updatingUi = false;
        }
    }
}
