/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Helpers;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public partial class PluginSettingsViewModel : ObservableObject
{
    private string _iniPath;
    private string _pluginDir;
    private string _presetsDir;
    private string _dllPath;
    private IniFile _iniFile;
    private bool _useKeyListInput = true;
    private readonly LightweightPluginService _lightweightPlugin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadSupported))]
    [NotifyPropertyChangedFor(nameof(ModeTabVisibility))]
    [NotifyPropertyChangedFor(nameof(SelectedPluginComboLabel))]
    private int selectedPluginIndex = 0;

    public bool IsDownloadSupported => SelectedPluginIndex == 0 && !IsLightweightMode;

    public bool IsLightweightMode => _lightweightPlugin.IsLightweightMode;

    public bool IsStandardModeTab => !IsLightweightMode;

    public bool IsLightweightModeTab => IsLightweightMode;

    public bool IsLitePluginInstallSupported => IsLightweightMode;

    public Microsoft.UI.Xaml.Visibility ModeTabVisibility =>
        SelectedPluginIndex == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string SelectedPluginComboLabel =>
        IsLightweightMode ? "LightweightMode_PluginComboLabel".GetLocalized() : "PluginFuFuMain".GetLocalized();

    public string PluginToggleLabel =>
        IsLightweightMode ? "LightweightMode_LiteEnabledLabel".GetLocalized() : "MainPluginEnabledLabel".GetLocalized();

    [ObservableProperty] private string pluginName;

    [ObservableProperty] private string pluginDescription;

    [ObservableProperty] private string pluginDeveloper;

    [ObservableProperty] private string lastModifiedDate;

    [ObservableProperty] private ObservableCollection<PresetModel> availablePresets = new();

    [ObservableProperty] private PresetModel currentPreset;

    private bool _isAutoCreatePresetEnabled = false;

    public bool IsAutoCreatePresetEnabled
    {
        get => _isAutoCreatePresetEnabled;
        set
        {
            if (SetProperty(ref _isAutoCreatePresetEnabled, value))
            {
                var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
                if (localSettings != null)
                {
                    _ = localSettings.SaveSettingAsync("IsAutoCreatePresetEnabled", value);
                }
            }
        }
    }

    public ObservableCollection<PluginSettingItem> Settings
    {
        get;
    } = new();

    public ObservableCollection<PluginSettingItem> PinnedSettings
    {
        get;
    } = new();

    public Microsoft.UI.Xaml.Visibility PinnedSettingsVisibility =>
        PinnedSettings.Count > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private const string PinnedSettingsKey = "PluginSettingPinnedItems";

    private readonly List<string> _settingOrder = new();
    private readonly Dictionary<string, int> _settingOrderIndexes = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _pinnedSections = new(StringComparer.OrdinalIgnoreCase);


    partial void OnSelectedPluginIndexChanged(int value)
    {
        CheckPluginStates();
        UpdatePaths();
        LoadConfiguration();
        RefreshUIState();
    }


    public PluginSettingsViewModel(bool deferConfigurationLoading = false)
    {
        _deferConfigurationLoading = deferConfigurationLoading;
        SettingGroups = new()
        {
            new PluginSettingsGroup(PinnedSettings),
            new PluginSettingsGroup(Settings, PinnedSettings)
        };
        _lightweightPlugin = App.GetService<LightweightPluginService>();
        PinnedSettings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(PinnedSettingsVisibility));
        CheckPluginStates();
        UpdatePaths();
        _pluginDir = GetMainPluginDirectory();
        _iniPath = IsLightweightMode
            ? LightweightPluginService.LitePluginConfigPath
            : Path.Combine(_pluginDir, "config.ini");
        _dllPath = IsLightweightMode
            ? LightweightPluginService.FindLitePluginDisabledPath() ?? LightweightPluginService.LitePluginDllPath
            : LightweightPluginService.MainPluginDllPath;
        _presetsDir = IsLightweightMode
            ? Path.Combine(AppPaths.PluginPresetsDir, LightweightPluginService.LitePluginFolderName)
            : AppPaths.PluginPresetsDir;

        _iniFile = new IniFile(_iniPath);
        if (_deferConfigurationLoading)
        {
            PluginName = SelectedPluginComboLabel;
            PluginDescription = string.Empty;
            PluginDeveloper = string.Empty;
            LastModifiedDate = string.Empty;
            return;
        }

        try
        {
            if (!Directory.Exists(_presetsDir))
            {
                Directory.CreateDirectory(_presetsDir);
            }
        }
        catch (UnauthorizedAccessException)
        {
            _presetsDir = Path.Combine(AppPaths.RootDir, "Data", "PluginPresets");
            try
            {
                Directory.CreateDirectory(_presetsDir);
            }
            catch (Exception inner)
            {
                System.Diagnostics.Debug.WriteLine($"目录创建失败: {inner.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"目录创建失败: {ex.Message}");
        }

        var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
        if (localSettings != null)
        {
            var keyInputTask = localSettings.ReadSettingAsync("UseKeyListInput");
            keyInputTask.Wait();
            _useKeyListInput = keyInputTask.Result == null || Convert.ToBoolean(keyInputTask.Result);

            var autoCreateTask = localSettings.ReadSettingAsync("IsAutoCreatePresetEnabled");
            autoCreateTask.Wait();
            _isAutoCreatePresetEnabled = autoCreateTask.Result != null && Convert.ToBoolean(autoCreateTask.Result);

            var devFeaturesTask = localSettings.ReadSettingAsync("IsDevFeaturesEnabled");
            devFeaturesTask.Wait();
            bool savedDevFeatures = devFeaturesTask.Result != null && Convert.ToBoolean(devFeaturesTask.Result);

            var pinnedSectionsTask = localSettings.ReadSettingAsync(PinnedSettingsKey);
            pinnedSectionsTask.Wait();
            _pinnedSections = DeserializePinnedSections(pinnedSectionsTask.Result);

            if (savedDevFeatures)
            {
                _isDevFeaturesEnabled = true;
                _ = VerifyAndApplyDevFeaturesOnStartupAsync();
            }
            else
            {
                _isDevFeaturesEnabled = false;
            }
        }

        LoadConfiguration();
    }


    public bool UseKeyListInput
    {
        get => _useKeyListInput;
        set
        {
            if (SetProperty(ref _useKeyListInput, value))
            {
                foreach (var setting in Settings.Concat(PinnedSettings)
                             .Where(s => string.Equals(s.Type, "key", StringComparison.OrdinalIgnoreCase)))
                {
                    setting.SetKeyInputMode(value);
                }

                var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
                if (localSettings != null)
                {
                    _ = localSettings.SaveSettingAsync("UseKeyListInput", value);
                }
            }
        }
    }


    private bool _isDevFeaturesEnabled;

    public bool IsDevFeaturesEnabled
    {
        get => _isDevFeaturesEnabled;
        set
        {
            if (_isDevFeaturesEnabled != value)
            {
                _isDevFeaturesEnabled = value;
                OnPropertyChanged();

                if (value)
                {
                    _ = VerifyAndApplyDevFeaturesAsync();
                }
                else
                {
                    SaveDevFeaturesSetting(false);
                    LoadConfiguration();
                }
            }
        }
    }

    public void RefreshModeState()
    {
        OnPropertyChanged(nameof(IsLightweightMode));
        OnPropertyChanged(nameof(IsStandardModeTab));
        OnPropertyChanged(nameof(IsLightweightModeTab));
        OnPropertyChanged(nameof(IsDownloadSupported));
        OnPropertyChanged(nameof(IsLitePluginInstallSupported));
        OnPropertyChanged(nameof(PluginToggleLabel));
        OnPropertyChanged(nameof(SelectedPluginComboLabel));

        CheckPluginStates();
        UpdatePaths();
        LoadConfiguration();
        RefreshUIState();
    }

    public void ToggleSettingPin(PluginSettingItem? item)
    {
        if (item == null || (!Settings.Contains(item) && !PinnedSettings.Contains(item))) return;

        ApplyPin(item, !item.IsPinned);
        SavePinnedSections();
    }

    private void ApplyPin(PluginSettingItem item, bool pinned)
    {
        item.IsPinned = pinned;

        string target = GetSettingsTargetKey();

        if (!_pinnedSections.TryGetValue(target, out var sections))
        {
            sections = new List<string>();
            _pinnedSections[target] = sections;
        }

        if (pinned)
        {
            if (!sections.Any(key => string.Equals(key, item.SectionKey, StringComparison.OrdinalIgnoreCase)))
            {
                sections.Add(item.SectionKey);
            }

            Settings.Remove(item);
            InsertBySettingOrder(PinnedSettings, item);
        }
        else
        {
            sections.RemoveAll(key => string.Equals(key, item.SectionKey, StringComparison.OrdinalIgnoreCase));
            PinnedSettings.Remove(item);
            InsertBySettingOrder(Settings, item);
        }
    }

    public IEnumerable<PluginSettingItem> SelectedSettings =>
        PinnedSettings.Concat(Settings).Where(item => item.IsSelected);

    public int SelectedSettingsCount =>
        PinnedSettings.Count(item => item.IsSelected) + Settings.Count(item => item.IsSelected);

    public Microsoft.UI.Xaml.Visibility SelectionBarVisibility =>
        SelectedSettingsCount > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string SelectionSummary =>
        string.Format("BatchActions_SelectedCount".GetLocalized(), SelectedSettingsCount);

    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedSettingsCount));
        OnPropertyChanged(nameof(SelectionBarVisibility));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    public void ClearSelection()
    {
        foreach (var item in PinnedSettings.Concat(Settings))
        {
            item.IsSelected = false;
        }

        NotifySelectionChanged();
    }

    public void BatchSetBoolValue(bool value)
    {
        foreach (var item in PinnedSettings.Concat(Settings))
        {
            if (!item.IsSelected) continue;
            if (!string.Equals(item.Type, "bool", StringComparison.OrdinalIgnoreCase)) continue;

            item.BoolValue = value;
        }

        NotifySelectionChanged();
    }

    public void BatchSetPinned(bool pinned)
    {
        var selected = PinnedSettings.Concat(Settings).Where(item => item.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var item in selected)
        {
            if (item.IsPinned != pinned)
            {
                ApplyPin(item, pinned);
            }

            item.IsSelected = false;
        }

        SavePinnedSections();
        NotifySelectionChanged();
    }

    public bool IsSettingPinned(string sectionKey)
    {
        if (!_pinnedSections.TryGetValue(GetSettingsTargetKey(), out var sections)) return false;

        return sections.Any(key => string.Equals(key, sectionKey, StringComparison.OrdinalIgnoreCase));
    }

    private string GetSettingsTargetKey()
    {
        var name = Path.GetFileName(_pluginDir);
        return string.IsNullOrEmpty(name) ? "default" : name;
    }

    private int GetSettingOrderIndex(string sectionKey) =>
        _settingOrderIndexes.GetValueOrDefault(sectionKey, -1);

    private void InsertBySettingOrder(ObservableCollection<PluginSettingItem> target, PluginSettingItem item)
    {
        var itemIndex = GetSettingOrderIndex(item.SectionKey);
        if (itemIndex < 0 || target.Count == 0 || GetSettingOrderIndex(target[^1].SectionKey) <= itemIndex)
        {
            target.Add(item);
            return;
        }

        var left = 0;
        var right = target.Count;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (GetSettingOrderIndex(target[middle].SectionKey) <= itemIndex)
            {
                left = middle + 1;
            }
            else
            {
                right = middle;
            }
        }
        target.Insert(left, item);
    }

    private void SavePinnedSections()
    {
        var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
        if (localSettings == null) return;

        _ = localSettings.SaveSettingAsync(PinnedSettingsKey, JsonSerializer.Serialize(_pinnedSections));
    }

    private static Dictionary<string, List<string>> DeserializePinnedSections(object? stored)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (stored == null) return result;

        try
        {
            var parsed =
                JsonSerializer.Deserialize<Dictionary<string, List<string>>>(stored.ToString() ?? string.Empty);
            if (parsed == null) return result;

            foreach (var pair in parsed)
            {
                result[pair.Key] = pair.Value ?? new List<string>();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[插件配置] 读取收藏项失败: {ex.Message}");
        }

        return result;
    }
}