using System.Text.Json;
using System.Text.Json.Serialization;
using HotspotService.Infrastructure;
using HotspotService.Models;

namespace HotspotService.Services;

public sealed class HotspotPluginSettingsStore : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsFilePath;
    private readonly object _fileLock = new();
    private bool _autoStartGuard = true;
    private GuardTargetState _startupTarget = GuardTargetState.On;
    private HotspotRestartPolicySettings _restartPolicy = new();
    private int _clientCountRefreshSeconds = 10;

    public HotspotPluginSettingsStore(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        Load();
    }

    public bool AutoStartGuard
    {
        get => _autoStartGuard;
        set
        {
            if (SetProperty(ref _autoStartGuard, value))
            {
                Save();
            }
        }
    }

    public GuardTargetState StartupTarget
    {
        get => _startupTarget;
        set
        {
            if (SetProperty(ref _startupTarget, value))
            {
                Save();
            }
        }
    }

    public HotspotRestartPolicySettings RestartPolicy
    {
        get => _restartPolicy;
        set
        {
            if (SetProperty(ref _restartPolicy, value))
            {
                Save();
            }
        }
    }

    public int ClientCountRefreshSeconds
    {
        get => _clientCountRefreshSeconds;
        set
        {
            if (SetProperty(ref _clientCountRefreshSeconds, value))
            {
                Save();
            }
        }
    }

    private void Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            Save();
            return;
        }

        var migrated = false;
        try
        {
            var document = JsonSerializer.Deserialize<HotspotPluginSettingsDocument>(
                File.ReadAllText(_settingsFilePath), JsonOptions);
            if (document is not null)
            {
                _autoStartGuard = document.AutoStartGuard;
                _startupTarget = document.StartupTarget;
                _restartPolicy = document.RestartPolicy ?? new HotspotRestartPolicySettings();
                _clientCountRefreshSeconds = Math.Max(1, document.ClientCountRefreshSeconds);
                return;
            }

            migrated = true;
        }
        catch
        {
            migrated = true;
        }

        if (migrated)
        {
            LoadLegacyFormat();
            Save();
        }
    }

    private void LoadLegacyFormat()
    {
        try
        {
            foreach (var line in File.ReadAllLines(_settingsFilePath))
            {
                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                if (parts[0].Equals(nameof(HotspotPluginSettingsDocument.AutoStartGuard), StringComparison.OrdinalIgnoreCase)
                    && bool.TryParse(parts[1], out var autoStartGuard))
                {
                    _autoStartGuard = autoStartGuard;
                }

                if (parts[0].Equals(nameof(HotspotPluginSettingsDocument.StartupTarget), StringComparison.OrdinalIgnoreCase)
                    && Enum.TryParse<GuardTargetState>(parts[1], true, out var startupTarget))
                {
                    _startupTarget = startupTarget;
                }
            }
        }
        catch
        {
            _autoStartGuard = true;
            _startupTarget = GuardTargetState.On;
        }
    }

    private void Save()
    {
        lock (_fileLock)
        {
            var document = new HotspotPluginSettingsDocument
            {
                AutoStartGuard = _autoStartGuard,
                StartupTarget = _startupTarget,
                RestartPolicy = _restartPolicy,
                ClientCountRefreshSeconds = _clientCountRefreshSeconds
            };
            var json = JsonSerializer.Serialize(document, JsonOptions);

            // 先写临时文件再原子替换，避免进程崩溃导致配置文件损坏。
            var tempPath = _settingsFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsFilePath, overwrite: true);
        }
    }
}
