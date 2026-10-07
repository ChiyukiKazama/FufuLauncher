/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;

namespace FufuLauncher.ViewModels;

public partial class PluginSettingsViewModel
{
    #region 预设管理

    private string GetTargetDllHash()
    {
        if (!File.Exists(_dllPath)) return string.Empty;

        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(_dllPath);
            var hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void ManagePresets(Dictionary<string, Dictionary<string, string>> currentIniData)
    {
        var request = new ConfigurationLoadRequest(_iniFile, _iniPath, _dllPath, _presetsDir,
            SelectedPluginIndex, IsLightweightMode, _isDevFeaturesEnabled, IsAutoCreatePresetEnabled);
        PresetLoadResult result;
        ConfigurationPreparationGate.Wait();
        try
        {
            result = PreparePresetState(request, currentIniData, CancellationToken.None);
        }
        finally
        {
            ConfigurationPreparationGate.Release();
        }

        _presetsDir = result.Directory;
        AvailablePresets.Clear();
        foreach (var preset in result.Presets)
        {
            AvailablePresets.Add(preset);
        }
        CurrentPreset = result.CurrentPreset;
        foreach (var notification in result.Notifications)
        {
            WeakReferenceMessenger.Default.Send(notification);
        }
    }

    public void ClearAllPresets()
    {
        try
        {
            if (Directory.Exists(_presetsDir))
            {
                var files = Directory.GetFiles(_presetsDir, "*.json");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }

            AvailablePresets.Clear();
            CurrentPreset = null;
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "清除失败",
                $"无法清除预设文件\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }

    public PresetModel CreateNewPreset(string name, Dictionary<string, Dictionary<string, string>> data, string hash)
    {
        var cleanData = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in data)
        {
            if (!kvp.Key.Equals("General", StringComparison.OrdinalIgnoreCase))
            {
                cleanData[kvp.Key] = new Dictionary<string, string>(kvp.Value, StringComparer.OrdinalIgnoreCase);
            }
        }

        var preset = new PresetModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            DllHash = hash,
            ConfigData = cleanData
        };

        preset.FilePath = Path.Combine(_presetsDir, $"{preset.Id}.json");
        SavePresetToFile(preset);
        AvailablePresets.Add(preset);
        return preset;
    }

    public bool RenamePreset(PresetModel preset, string name)
    {
        if (preset == null || string.IsNullOrWhiteSpace(name)) return false;

        var newName = name.Trim();
        if (preset.Name == newName) return true;

        var previousName = preset.Name;
        preset.Name = newName;
        if (SavePresetToFile(preset)) return true;

        preset.Name = previousName;
        return false;
    }

    private bool SavePresetToFile(PresetModel preset)
    {
        if (string.IsNullOrEmpty(preset.FilePath)) return false;
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(preset.FilePath, JsonSerializer.Serialize(preset, options));
            return true;
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "预设保存失败",
                $"无法保存预设文件，可能缺少写入权限\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
            return false;
        }
    }

    private void SaveActiveState()
    {
        if (CurrentPreset == null) return;
        try
        {
            var stateFile = Path.Combine(_presetsDir, "active_state.json");
            var stateDict = new Dictionary<string, string> { { "ActiveId", CurrentPreset.Id } };
            File.WriteAllText(stateFile, JsonSerializer.Serialize(stateDict));
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "状态保存失败",
                $"无法保存激活状态记录，可能缺少写入权限\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }

    private void ApplyPresetConfigToIni(PresetModel preset)
    {
        if (preset == null) return;

        var configData =
            new Dictionary<string, Dictionary<string, string>>(preset.ConfigData, StringComparer.OrdinalIgnoreCase);
        configData.Remove("General");
        ConfigurationPreparationGate.Wait();
        try
        {
            _iniFile.UpdateMultiple(configData);
        }
        finally
        {
            ConfigurationPreparationGate.Release();
        }
    }

    private void OnSettingValueChanged(string section, string key, string value)
    {
        if (CurrentPreset == null) return;

        if (section.Equals("General", StringComparison.OrdinalIgnoreCase)) return;

        if (!CurrentPreset.ConfigData.ContainsKey(section))
        {
            CurrentPreset.ConfigData[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        CurrentPreset.ConfigData[section][key] = value;
        SavePresetToFile(CurrentPreset);
    }

    public string GetPresetLockReason(PresetModel preset)
    {
        if (preset == null || !preset.IsLocked) return string.Empty;

        var currentHash = GetTargetDllHash();
        if (string.IsNullOrEmpty(preset.DllHash))
        {
            return "此预设没有记录插件 Hash，无法确认它是否适用于当前插件版本。";
        }

        if (string.IsNullOrEmpty(currentHash))
        {
            return "当前插件文件不存在或无法读取 Hash，无法确认此预设是否适用于当前插件版本。";
        }

        if (!string.Equals(preset.DllHash, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            return "此预设记录的插件 Hash 与当前插件 Hash 不一致，可能来自旧版本或不同版本的插件。";
        }

        return "此预设当前被标记为锁定。";
    }

    public void ForceUnlockAndSwitchPreset(PresetModel targetPreset)
    {
        if (targetPreset == null) return;

        targetPreset.DllHash = GetTargetDllHash();
        targetPreset.IsLocked = false;
        SavePresetToFile(targetPreset);
        SwitchPreset(targetPreset);
    }

    public void SwitchPreset(PresetModel targetPreset)
    {
        if (targetPreset == null || targetPreset.IsLocked) return;

        CurrentPreset = targetPreset;
        SaveActiveState();

        try
        {
            ApplyPresetConfigToIni(CurrentPreset);
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "配置更新失败",
                $"切换预设时无法写入配置文件\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }

        LoadConfiguration();

        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            "预设已切换",
            $"当前预设: {targetPreset.Name}",
            NotificationType.Success,
            3000
        ));
    }

    public void DeletePreset(PresetModel targetPreset)
    {
        if (targetPreset == null || string.IsNullOrEmpty(targetPreset.FilePath)) return;

        try
        {
            if (File.Exists(targetPreset.FilePath))
            {
                File.Delete(targetPreset.FilePath);
            }

            AvailablePresets.Remove(targetPreset);

            if (CurrentPreset?.Id == targetPreset.Id)
            {
                LoadConfiguration();
            }
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "预设删除失败",
                $"无法删除指定的预设文件，文件可能被占用或权限不足\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }

    #endregion
}