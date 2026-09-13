using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using HotspotService.Models;
using HotspotService.Services;

namespace HotspotService.Settings;

[SettingsPageInfo(PluginIds.SettingsPage, "HotspotService", PluginIds.WifiGlyph, PluginIds.WifiGlyph)]
public sealed class HotspotSettingsPage : SettingsPageBase
{
    private readonly HotspotGuardCoordinator _coordinator;
    private readonly HotspotPluginSettingsStore _settingsStore;
    private readonly HotspotGuardRuntimeState _runtimeState;
    private readonly IReadOnlyList<OptionItem<GuardTargetState>> _targetOptions =
    [
        new("开", GuardTargetState.On),
        new("关", GuardTargetState.Off)
    ];

    private readonly CheckBox _autoStartCheckBox;
    private readonly ComboBox _startupTargetComboBox;
    private readonly ComboBox _currentTargetComboBox;
    private readonly Button _enableGuardButton;
    private readonly Button _disableGuardButton;
    private readonly Button _restartButton;
    private readonly NumericUpDown _clientRefreshIntervalBox;
    private readonly CheckBox _enableThroughputCheckBox;
    private readonly NumericUpDown _throughputIntervalBox;
    private readonly TextBlock _hotspotThroughputValue;
    private readonly TextBlock _internetThroughputValue;
    private readonly TextBlock _throughputSampledAtValue;
    private readonly ShortcutControls _shortcut;
    private readonly TextBlock _restartTipText = new();
    private int _restartTipVersion;
    private readonly TextBlock _guardEnabledValue;
    private readonly TextBlock _guardTargetValue;
    private readonly TextBlock _hotspotStateValue;
    private readonly TextBlock _lastCheckValue;
    private readonly TextBlock _lastErrorValue;
    private bool _updatingUi;

    public HotspotSettingsPage(
        HotspotPluginSettingsStore settingsStore,
        HotspotGuardRuntimeState runtimeState,
        HotspotGuardCoordinator coordinator)
    {
        _coordinator = coordinator;
        _settingsStore = settingsStore;
        _runtimeState = runtimeState;

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };

        var mainPanel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20
        };

        mainPanel.Children.Add(new TextBlock
        {
            Text = "移动热点守护",
            FontSize = 14
        });

        mainPanel.Children.Add(new TextBlock
        {
            Text = "版权所有 (c) 2026 AlanCRL(陈润林) 工作室\n本项目基于 GNU 通用公共许可证第 3 版获得许可",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        _autoStartCheckBox = new CheckBox
        {
            Content = "软件启动时自动开启守护"
        };
        _autoStartCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            _settingsStore.AutoStartGuard = _autoStartCheckBox.IsChecked == true;
        };
        mainPanel.Children.Add(_autoStartCheckBox);

        var targetPanel = new StackPanel
        {
            Spacing = 8
        };
        targetPanel.Children.Add(new TextBlock
        {
            Text = "启动时默认守护目标"
        });
        _startupTargetComboBox = new ComboBox
        {
            ItemsSource = _targetOptions,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 220
        };
        _startupTargetComboBox.SelectionChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            if (_startupTargetComboBox.SelectedItem is OptionItem<GuardTargetState> option)
            {
                _settingsStore.StartupTarget = option.Value;
            }
        };
        targetPanel.Children.Add(_startupTargetComboBox);
        mainPanel.Children.Add(targetPanel);

        var manualControlPanel = new StackPanel
        {
            Spacing = 8
        };
        manualControlPanel.Children.Add(new TextBlock
        {
            Text = "手动守护控制"
        });
        var liveControlRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        liveControlRow.Children.Add(new TextBlock
        {
            Text = "当前目标状态",
            VerticalAlignment = VerticalAlignment.Center
        });
        _currentTargetComboBox = new ComboBox
        {
            ItemsSource = _targetOptions,
            MinWidth = 120
        };
        _currentTargetComboBox.SelectionChanged += async (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            if (_currentTargetComboBox.SelectedItem is OptionItem<GuardTargetState> option)
            {
                await _coordinator.SetGuardTargetAsync(option.Value, applyImmediately: true);
            }
        };
        liveControlRow.Children.Add(_currentTargetComboBox);
        _enableGuardButton = new Button
        {
            Content = "开启守护",
            MinWidth = 120
        };
        _enableGuardButton.Click += async (_, _) => await _coordinator.SetGuardEnabledAsync(true);
        liveControlRow.Children.Add(_enableGuardButton);
        _disableGuardButton = new Button
        {
            Content = "关闭守护",
            MinWidth = 120
        };
        _disableGuardButton.Click += async (_, _) => await _coordinator.SetGuardEnabledAsync(false);
        liveControlRow.Children.Add(_disableGuardButton);
        manualControlPanel.Children.Add(liveControlRow);
        mainPanel.Children.Add(manualControlPanel);

        var maintenancePanel = new StackPanel
        {
            Spacing = 8
        };
        maintenancePanel.Children.Add(new TextBlock
        {
            Text = "热点维护"
        });
        var restartRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        _restartButton = new Button
        {
            Content = "立即重启热点",
            MinWidth = 120
        };
        _restartButton.Click += async (_, _) => await OnRestartClickedAsync();
        restartRow.Children.Add(_restartButton);
        maintenancePanel.Children.Add(restartRow);
        maintenancePanel.Children.Add(_restartTipText);
        mainPanel.Children.Add(maintenancePanel);

        var refreshPanel = new StackPanel
        {
            Spacing = 8
        };
        refreshPanel.Children.Add(new TextBlock
        {
            Text = "连接数刷新间隔（秒）"
        });
        _clientRefreshIntervalBox = new NumericUpDown
        {
            Minimum = 5,
            Maximum = 3600,
            Increment = 5,
            Value = Math.Max(5, _settingsStore.ClientCountRefreshSeconds),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 120
        };
        _clientRefreshIntervalBox.ValueChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            if (_clientRefreshIntervalBox.Value is { } value)
            {
                _settingsStore.ClientCountRefreshSeconds = Math.Clamp((int)value, 5, 3600);
            }
        };
        refreshPanel.Children.Add(_clientRefreshIntervalBox);
        refreshPanel.Children.Add(new TextBlock
        {
            Text = "连接设备数量与设备列表的刷新频率，范围 5–3600 秒。守护状态本身仍按每 10 秒一次检查，不受此设置影响。",
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap
        });
        mainPanel.Children.Add(refreshPanel);

        mainPanel.Children.Add(CreateThroughputPanel(
            out _enableThroughputCheckBox,
            out _throughputIntervalBox,
            out _hotspotThroughputValue,
            out _internetThroughputValue,
            out _throughputSampledAtValue));

        _shortcut = CreateShortcutPanel();
        mainPanel.Children.Add(_shortcut.Panel);

        var statusBorder = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16)
        };
        var statusPanel = new StackPanel
        {
            Spacing = 10
        };
        statusPanel.Children.Add(new TextBlock
        {
            Text = "当前状态",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold
        });

        statusPanel.Children.Add(CreateStatusRow("守护状态", out _guardEnabledValue));
        statusPanel.Children.Add(CreateStatusRow("守护目标", out _guardTargetValue));
        statusPanel.Children.Add(CreateStatusRow("热点状态", out _hotspotStateValue));
        statusPanel.Children.Add(CreateStatusRow("最近检查", out _lastCheckValue));
        statusPanel.Children.Add(CreateStatusRow("最近错误", out _lastErrorValue, wrapValue: true));
        statusBorder.Child = statusPanel;
        mainPanel.Children.Add(statusBorder);

        var scrollViewer = new ScrollViewer
        {
            Content = mainPanel
        };
        root.Children.Add(scrollViewer);

        Content = root;

        _settingsStore.PropertyChanged += OnStateSourceChanged;
        _runtimeState.PropertyChanged += OnStateSourceChanged;
        UpdateUi();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _settingsStore.PropertyChanged -= OnStateSourceChanged;
        _runtimeState.PropertyChanged -= OnStateSourceChanged;
    }

    private void OnStateSourceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        PostUpdateUi();
    }

    private void PostUpdateUi()
    {
        Dispatcher.UIThread.Post(UpdateUi);
    }

    private async Task OnRestartClickedAsync()
    {
        _restartButton.IsEnabled = false;
        try
        {
            await _coordinator.RestartHotspotAsync();
            ShowRestartTip("重启指令已执行，热点已重新启动。");
        }
        catch (Exception ex)
        {
            ShowRestartTip($"重启失败：{ex.Message}");
        }
        finally
        {
            _restartButton.IsEnabled = true;
        }
    }

    private void ShowRestartTip(string message)
    {
        var version = ++_restartTipVersion;
        _restartTipText.Text = message;
        _ = ClearRestartTipAsync(version);
    }

    private async Task ClearRestartTipAsync(int version)
    {
        await Task.Delay(TimeSpan.FromSeconds(8));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (version == _restartTipVersion)
            {
                _restartTipText.Text = string.Empty;
            }
        });
    }

    private void UpdateUi()
    {
        _updatingUi = true;
        try
        {
            _autoStartCheckBox.IsChecked = _settingsStore.AutoStartGuard;
            _startupTargetComboBox.SelectedItem = _targetOptions.FirstOrDefault(x => x.Value == _settingsStore.StartupTarget);
            _currentTargetComboBox.SelectedItem = _targetOptions.FirstOrDefault(x => x.Value == _runtimeState.GuardTarget);
            _enableGuardButton.IsEnabled = !_runtimeState.GuardEnabled;
            _disableGuardButton.IsEnabled = _runtimeState.GuardEnabled;

            _guardEnabledValue.Text = _runtimeState.GuardEnabled ? "已开启" : "已关闭";
            _guardTargetValue.Text = _runtimeState.GuardTarget == GuardTargetState.On ? "开" : "关";
            _hotspotStateValue.Text = _runtimeState.LastKnownHotspotState.ToDisplayText();
            _lastCheckValue.Text = _runtimeState.LastCheckAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未检查";
            _lastErrorValue.Text = string.IsNullOrWhiteSpace(_runtimeState.LastError) ? "无" : _runtimeState.LastError;

            _enableThroughputCheckBox.IsChecked = _settingsStore.Throughput.EnableSampling;
            _throughputIntervalBox.Value = HotspotThroughputSettings.ClampInterval(_settingsStore.Throughput.SamplingIntervalSeconds);
            _hotspotThroughputValue.Text = NetworkSpeedFormatter.FormatStatusLine(_runtimeState.HotspotThroughput);
            _internetThroughputValue.Text = NetworkSpeedFormatter.FormatStatusLine(_runtimeState.InternetThroughput);
            _throughputSampledAtValue.Text =
                _runtimeState.LastThroughputSampleAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未采样";

            var shortcut = _settingsStore.Shortcut;
            _shortcut.EnableCheckBox.IsChecked = shortcut.Enabled;
            _shortcut.KeyComboBox.SelectedItem = HotspotShortcutKeys.All
                .FirstOrDefault(x => string.Equals(x, shortcut.KeyName, StringComparison.OrdinalIgnoreCase));
            _shortcut.CtrlCheckBox.IsChecked = shortcut.Ctrl;
            _shortcut.AltCheckBox.IsChecked = shortcut.Alt;
            _shortcut.ShiftCheckBox.IsChecked = shortcut.Shift;
            _shortcut.MetaCheckBox.IsChecked = shortcut.Meta;
            _shortcut.CooldownBox.Value = HotspotShortcutSettings.ClampCooldown(shortcut.CooldownSeconds);

            _shortcut.CombinationValue.Text = shortcut.DescribeShortcut();
            _shortcut.SourceValue.Text = _runtimeState.ShortcutSourceAvailable
                ? "已连接，快捷键可用"
                : _runtimeState.ShortcutSourceMessage ?? "未检测到 KeyboardCapture 插件";
            _shortcut.TriggeredAtValue.Text =
                _runtimeState.LastShortcutTriggeredAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未触发";
            _shortcut.ErrorValue.Text =
                string.IsNullOrWhiteSpace(_runtimeState.LastShortcutError) ? "无" : _runtimeState.LastShortcutError;
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private StackPanel CreateThroughputPanel(
        out CheckBox enableCheckBox,
        out NumericUpDown intervalBox,
        out TextBlock hotspotValue,
        out TextBlock internetValue,
        out TextBlock sampledAtValue)
    {
        var panel = new StackPanel
        {
            Spacing = 8
        };
        panel.Children.Add(new TextBlock
        {
            Text = "网速检测"
        });

        // out 参数不能进 lambda，先用局部变量接线，最后再赋给 out 参数。
        var enableBox = new CheckBox
        {
            Content = "启用网卡吞吐采样（热点网卡 + 外网网卡）"
        };
        enableBox.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            _settingsStore.UpdateThroughput(settings => settings.EnableSampling = enableBox.IsChecked == true);
        };
        panel.Children.Add(enableBox);
        enableCheckBox = enableBox;

        var intervalRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        intervalRow.Children.Add(new TextBlock
        {
            Text = "采样间隔（秒）",
            VerticalAlignment = VerticalAlignment.Center
        });
        var intervalInput = new NumericUpDown
        {
            Minimum = HotspotThroughputSettings.MinimumIntervalSeconds,
            Maximum = HotspotThroughputSettings.MaximumIntervalSeconds,
            Increment = 1,
            Value = HotspotThroughputSettings.ClampInterval(_settingsStore.Throughput.SamplingIntervalSeconds),
            MinWidth = 120
        };
        intervalInput.ValueChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            if (intervalInput.Value is { } value)
            {
                _settingsStore.UpdateThroughput(settings => settings.SamplingIntervalSeconds = (int)value);
            }
        };
        intervalRow.Children.Add(intervalInput);
        panel.Children.Add(intervalRow);
        intervalBox = intervalInput;

        panel.Children.Add(CreateStatusRow("热点网卡", out hotspotValue, wrapValue: true));
        panel.Children.Add(CreateStatusRow("外网网卡", out internetValue, wrapValue: true));
        panel.Children.Add(CreateStatusRow("最近采样", out sampledAtValue));
        panel.Children.Add(new TextBlock
        {
            Text = "网速由网卡累计流量差值换算，单位为字节每秒（B/s、KB/s、MB/s）；热点网卡优先按 192.168.137.x 地址识别，"
                   + "其次按 Wi-Fi Direct 虚拟适配器识别，因此通常需要在热点开启后才能看到数据。"
                   + "是否在展示组件中显示网速，请到“移动热点守护”组件设置中单独开关。",
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }

    private ShortcutControls CreateShortcutPanel()
    {
        var panel = new StackPanel
        {
            Spacing = 8
        };
        panel.Children.Add(new TextBlock
        {
            Text = "快捷键重启热点"
        });

        var enableCheckBox = new CheckBox
        {
            Content = "启用快捷键（需要安装 KeyboardCapture 插件）"
        };
        enableCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            _settingsStore.UpdateShortcut(settings => settings.Enabled = enableCheckBox.IsChecked == true);
        };
        panel.Children.Add(enableCheckBox);

        var keyRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        keyRow.Children.Add(new TextBlock
        {
            Text = "触发键",
            VerticalAlignment = VerticalAlignment.Center
        });
        var keyComboBox = new ComboBox
        {
            ItemsSource = HotspotShortcutKeys.All,
            MinWidth = 120
        };
        keyComboBox.SelectionChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            if (keyComboBox.SelectedItem is string keyName)
            {
                _settingsStore.UpdateShortcut(settings => settings.KeyName = keyName);
            }
        };
        keyRow.Children.Add(keyComboBox);
        panel.Children.Add(keyRow);

        var modifierRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        modifierRow.Children.Add(new TextBlock
        {
            Text = "修饰键",
            VerticalAlignment = VerticalAlignment.Center
        });

        CheckBox CreateModifierCheckBox(string text, Action<HotspotShortcutSettings, bool> apply)
        {
            var checkBox = new CheckBox
            {
                Content = text
            };
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (_updatingUi)
                {
                    return;
                }

                var value = checkBox.IsChecked == true;
                _settingsStore.UpdateShortcut(settings => apply(settings, value));
            };
            modifierRow.Children.Add(checkBox);
            return checkBox;
        }

        var ctrlCheckBox = CreateModifierCheckBox("Ctrl", static (settings, value) => settings.Ctrl = value);
        var altCheckBox = CreateModifierCheckBox("Alt", static (settings, value) => settings.Alt = value);
        var shiftCheckBox = CreateModifierCheckBox("Shift", static (settings, value) => settings.Shift = value);
        var metaCheckBox = CreateModifierCheckBox("Win", static (settings, value) => settings.Meta = value);
        panel.Children.Add(modifierRow);

        var cooldownRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        cooldownRow.Children.Add(new TextBlock
        {
            Text = "触发冷却（秒）",
            VerticalAlignment = VerticalAlignment.Center
        });
        var cooldownBox = new NumericUpDown
        {
            Minimum = HotspotShortcutSettings.MinimumCooldownSeconds,
            Maximum = HotspotShortcutSettings.MaximumCooldownSeconds,
            Increment = 1,
            Value = HotspotShortcutSettings.ClampCooldown(_settingsStore.Shortcut.CooldownSeconds),
            MinWidth = 120
        };
        cooldownBox.ValueChanged += (_, _) =>
        {
            if (_updatingUi)
            {
                return;
            }

            if (cooldownBox.Value is { } value)
            {
                _settingsStore.UpdateShortcut(settings => settings.CooldownSeconds = (int)value);
            }
        };
        cooldownRow.Children.Add(cooldownBox);
        panel.Children.Add(cooldownRow);

        panel.Children.Add(CreateStatusRow("当前组合", out var combinationValue));
        panel.Children.Add(CreateStatusRow("KeyboardCapture", out var sourceValue, wrapValue: true));
        panel.Children.Add(CreateStatusRow("最近触发", out var triggeredAtValue));
        panel.Children.Add(CreateStatusRow("最近错误", out var errorValue, wrapValue: true));
        panel.Children.Add(new TextBlock
        {
            Text = "快捷键由 KeyboardCapture 插件提供（使用非独占钩子，不会拦截系统热键）。"
                   + "未安装该插件时快捷键不可用，其余功能不受影响；组合与冷却修改后立即生效。",
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap
        });

        return new ShortcutControls(
            panel,
            enableCheckBox,
            keyComboBox,
            ctrlCheckBox,
            altCheckBox,
            shiftCheckBox,
            metaCheckBox,
            cooldownBox,
            combinationValue,
            sourceValue,
            triggeredAtValue,
            errorValue);
    }

    /// <summary>快捷键分组里的控件集合，避免设置页构造函数里出现大量 out 参数。</summary>
    private sealed record ShortcutControls(
        StackPanel Panel,
        CheckBox EnableCheckBox,
        ComboBox KeyComboBox,
        CheckBox CtrlCheckBox,
        CheckBox AltCheckBox,
        CheckBox ShiftCheckBox,
        CheckBox MetaCheckBox,
        NumericUpDown CooldownBox,
        TextBlock CombinationValue,
        TextBlock SourceValue,
        TextBlock TriggeredAtValue,
        TextBlock ErrorValue);

    private static Grid CreateStatusRow(string label, out TextBlock valueBlock, bool wrapValue = false)
    {
        valueBlock = new TextBlock
        {
            TextWrapping = wrapValue ? TextWrapping.Wrap : TextWrapping.NoWrap
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("140,*")
        };
        grid.Children.Add(new TextBlock
        {
            Text = $"{label}：",
            FontWeight = FontWeight.SemiBold
        });

        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);
        return grid;
    }
}
