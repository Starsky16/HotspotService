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
    private readonly CheckBox _showClientCountCheckBox = new()
    {
        Content = "显示连接设备数量"
    };
    private readonly CheckBox _showLastErrorCheckBox = new()
    {
        Content = "显示最近同步错误"
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

        _showClientCountCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            Settings.ShowClientCount = _showClientCountCheckBox.IsChecked == true;
        };
        panel.Children.Add(_showClientCountCheckBox);

        _showLastErrorCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            Settings.ShowLastError = _showLastErrorCheckBox.IsChecked == true;
        };
        panel.Children.Add(_showLastErrorCheckBox);

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
            _showClientCountCheckBox.IsChecked = Settings.ShowClientCount;
            _showLastErrorCheckBox.IsChecked = Settings.ShowLastError;
        }
        finally
        {
            _updatingUi = false;
        }
    }
}
