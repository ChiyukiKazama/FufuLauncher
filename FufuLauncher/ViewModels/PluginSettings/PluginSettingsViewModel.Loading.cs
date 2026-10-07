using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FufuLauncher.ViewModels;

public partial class PluginSettingsViewModel
{
    private const int ConfigurationBatchSize = 8;
    private readonly bool _deferConfigurationLoading;
    private readonly DispatcherQueue? _configurationDispatcher = DispatcherQueue.GetForCurrentThread();
    private CancellationTokenSource? _configurationCancellation;
    private Task<ConfigurationPreferences>? _configurationPreferences;
    private bool _presentationActive;
    private bool _preferencesApplied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigurationLoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(CanUseConfigurationFunctions))]
    [NotifyPropertyChangedFor(nameof(ConfigurationLoadingText))]
    private bool isLoadingConfiguration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsInteractable))]
    [NotifyPropertyChangedFor(nameof(CanUseConfigurationFunctions))]
    private bool isConfigurationReady;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseConfigurationFunctions))]
    private bool areConfigurationPreferencesReady;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigurationLoadingText))]
    private int loadedSettingCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigurationLoadingText))]
    private int totalSettingCount;

    [ObservableProperty]
    private string configurationLoadError = string.Empty;

    [ObservableProperty]
    private bool hasConfigurationLoadError;

    public ObservableCollection<PluginSettingsGroup> SettingGroups { get; }

    public bool CanUseConfigurationFunctions => AreConfigurationPreferencesReady && (!IsLoadingConfiguration || IsConfigurationReady);

    public Visibility ConfigurationLoadingVisibility => IsLoadingConfiguration ? Visibility.Visible : Visibility.Collapsed;

    public string ConfigurationLoadingText => TotalSettingCount == 0
        ? "PluginStoreLoading".GetLocalized()
        : $"{"PluginStoreLoading".GetLocalized()} {LoadedSettingCount} / {TotalSettingCount}";

    public void ActivateConfigurationLoading()
    {
        if (!_deferConfigurationLoading || _presentationActive)
        {
            return;
        }

        _presentationActive = true;
        CheckPluginStates();
        UpdatePaths();
        LoadConfiguration();
    }

    public void SuspendConfigurationLoading()
    {
        if (!_deferConfigurationLoading)
        {
            return;
        }

        _presentationActive = false;
        CancelConfigurationLoad();
        IsLoadingConfiguration = false;
        IsConfigurationReady = false;
    }

    private void CancelConfigurationLoad()
    {
        var previous = _configurationCancellation;
        _configurationCancellation = null;
        if (previous != null)
        {
            previous.Cancel();
            previous.Dispose();
        }
    }

    private void RequestConfigurationLoad()
    {
        if (_configurationDispatcher != null && !_configurationDispatcher.HasThreadAccess)
        {
            _configurationDispatcher.TryEnqueue(RequestConfigurationLoad);
            return;
        }

        CancelConfigurationLoad();
        if (!_presentationActive)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _configurationCancellation = cancellation;
        IsConfigurationReady = false;
        IsLoadingConfiguration = true;
        LoadedSettingCount = 0;
        TotalSettingCount = 0;
        ConfigurationLoadError = string.Empty;
        HasConfigurationLoadError = false;
        Settings.Clear();
        PinnedSettings.Clear();
        _settingOrder.Clear();
        _settingOrderIndexes.Clear();
        AvailablePresets.Clear();
        CurrentPreset = null;
        NotifySelectionChanged();
        PluginName = SelectedPluginIndex == 1 ? "PluginFPS".GetLocalized() : SelectedPluginComboLabel;
        PluginDescription = string.Empty;
        PluginDeveloper = string.Empty;
        LastModifiedDate = string.Empty;
        _ = LoadConfigurationProgressivelyAsync(cancellation);
    }

    private bool IsCurrentConfiguration(CancellationTokenSource cancellation, CancellationToken token)
    {
        return _presentationActive && !token.IsCancellationRequested &&
               ReferenceEquals(_configurationCancellation, cancellation);
    }

    private async Task LoadConfigurationProgressivelyAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            await YieldConfigurationPresentationAsync(token);
            _configurationPreferences ??= ReadConfigurationPreferencesAsync();
            var preferences = await _configurationPreferences.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (!IsCurrentConfiguration(cancellation, token))
            {
                return;
            }

            if (!_preferencesApplied)
            {
                _useKeyListInput = preferences.UseKeyListInput;
                _isAutoCreatePresetEnabled = preferences.AutoCreatePresets;
                _pinnedSections = preferences.PinnedSections;
                _isDevFeaturesEnabled = preferences.DeveloperFeatures;
                _preferencesApplied = true;
                AreConfigurationPreferencesReady = true;
                OnPropertyChanged(nameof(UseKeyListInput));
                OnPropertyChanged(nameof(IsAutoCreatePresetEnabled));
                OnPropertyChanged(nameof(IsDevFeaturesEnabled));
            }

            var request = new ConfigurationLoadRequest(_iniFile, _iniPath, _dllPath, _presetsDir,
                SelectedPluginIndex, IsLightweightMode, _isDevFeaturesEnabled, IsAutoCreatePresetEnabled);
            var snapshot = await Task.Run(() =>
            {
                ConfigurationPreparationGate.Wait(token);
                try
                {
                    return ReadConfigurationSnapshot(request, token);
                }
                finally
                {
                    ConfigurationPreparationGate.Release();
                }
            }, token);

            if (!IsCurrentConfiguration(cancellation, token))
            {
                return;
            }

            if (!snapshot.Exists)
            {
                PluginName = request.PluginIndex == 1 ? "未安装 FPS 插件" : request.LightweightMode
                    ? "LightweightMode_LiteNotInstalled".GetLocalized() : "未安装 FuFuPlugin";
                PluginDescription = "请确保Plugins目录下存在对应的文件夹及config.ini文件";
                return;
            }

            PluginName = snapshot.Name;
            PluginDescription = snapshot.Description;
            PluginDeveloper = snapshot.Developer;
            LastModifiedDate = snapshot.LastModified;
            _presetsDir = snapshot.PresetState!.Directory;
            CurrentPreset = snapshot.PresetState.CurrentPreset;
            foreach (var notification in snapshot.PresetState.Notifications)
            {
                WeakReferenceMessenger.Default.Send(notification);
            }

            for (var index = 0; index < snapshot.PresetState.Presets.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                AvailablePresets.Add(snapshot.PresetState.Presets[index]);
                if ((index + 1) % ConfigurationBatchSize == 0)
                {
                    await YieldConfigurationPresentationAsync(token);
                }
            }

            TotalSettingCount = snapshot.Settings.Count;
            for (var index = 0; index < snapshot.Settings.Count; index++)
            {
                _settingOrder.Add(snapshot.Settings[index].Section);
                _settingOrderIndexes[snapshot.Settings[index].Section] = index;
            }
            IsConfigurationReady = true;
            var currentPreset = CurrentPreset;
            var ordered = snapshot.Settings.Where(setting => IsSettingPinned(setting.Section))
                .Concat(snapshot.Settings.Where(setting => !IsSettingPinned(setting.Section))).ToArray();
            var budget = Stopwatch.StartNew();
            var batchCount = 0;
            foreach (var setting in ordered)
            {
                token.ThrowIfCancellationRequested();
                if (!IsCurrentConfiguration(cancellation, token))
                {
                    return;
                }

                var item = new PluginSettingItem(request.IniFile, setting.Section, setting.Name, setting.Type,
                    setting.Value, setting.Help, (section, key, value) =>
                    {
                        if (IsCurrentConfiguration(cancellation, token) && ReferenceEquals(CurrentPreset, currentPreset))
                        {
                            OnSettingValueChanged(section, key, value);
                        }
                    }, UseKeyListInput, () => IsCurrentConfiguration(cancellation, token) && IsConfigurationReady);

                if (IsSettingPinned(setting.Section))
                {
                    item.IsPinned = true;
                    InsertBySettingOrder(PinnedSettings, item);
                }
                else
                {
                    InsertBySettingOrder(Settings, item);
                }
                LoadedSettingCount++;
                batchCount++;
                if (batchCount >= ConfigurationBatchSize || budget.ElapsedMilliseconds >= 4)
                {
                    await YieldConfigurationPresentationAsync(token);
                    batchCount = 0;
                    budget.Restart();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginSettings] Progressive configuration load failed: {ex}");
            if (IsCurrentConfiguration(cancellation, token))
            {
                Settings.Clear();
                PinnedSettings.Clear();
                AvailablePresets.Clear();
                CurrentPreset = null;
                IsConfigurationReady = false;
                LoadedSettingCount = 0;
                TotalSettingCount = 0;
                _settingOrder.Clear();
                _settingOrderIndexes.Clear();
                _configurationPreferences = _preferencesApplied ? _configurationPreferences : null;
                ConfigurationLoadError = ex.Message;
                HasConfigurationLoadError = true;
                WeakReferenceMessenger.Default.Send(new NotificationMessage("配置读取失败",
                    $"无法读取插件配置文件。\n详细信息: {ex.Message}", NotificationType.Error, 6000));
            }
        }
        finally
        {
            if (IsCurrentConfiguration(cancellation, token))
            {
                IsLoadingConfiguration = false;
                NotifySelectionChanged();
            }
        }
    }

    private async Task<ConfigurationPreferences> ReadConfigurationPreferencesAsync()
    {
        var service = App.GetService<ILocalSettingsService>();
        var keyInput = await service.ReadSettingAsync("UseKeyListInput");
        var autoPresets = await service.ReadSettingAsync("IsAutoCreatePresetEnabled");
        var devFeatures = await service.ReadSettingAsync("IsDevFeaturesEnabled");
        var pinned = await service.ReadSettingAsync(PinnedSettingsKey);
        var developerFeatures = devFeatures != null && Convert.ToBoolean(devFeatures);
        if (developerFeatures)
        {
            developerFeatures = await CheckHwidAuthorizationAsync();
            if (!developerFeatures)
            {
                SaveDevFeaturesSetting(false);
            }
        }

        return new ConfigurationPreferences(keyInput == null || Convert.ToBoolean(keyInput),
            autoPresets != null && Convert.ToBoolean(autoPresets), developerFeatures, DeserializePinnedSections(pinned));
    }

    private async Task YieldConfigurationPresentationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        if (_configurationDispatcher == null ||
            !_configurationDispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => completion.TrySetResult(true)))
        {
            throw new OperationCanceledException("The configuration dispatcher is unavailable.", token);
        }
        await completion.Task;
        await Task.Delay(16, token);
    }

    private static ConfigurationLoadSnapshot ReadConfigurationSnapshot(ConfigurationLoadRequest request,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(request.IniPath))
        {
            return new ConfigurationLoadSnapshot(false, string.Empty, string.Empty, string.Empty, string.Empty,
                Array.Empty<ConfigurationSetting>(), null);
        }

        var initialData = request.IniFile.ReadAll(token);
        var presets = PreparePresetState(request, initialData, token);
        token.ThrowIfCancellationRequested();
        var data = request.IniFile.ReadAll(token);
        var general = data.GetValueOrDefault("General", new Dictionary<string, string>());
        var settings = new List<ConfigurationSetting>();
        var developerZone = false;
        foreach (var section in data)
        {
            token.ThrowIfCancellationRequested();
            if (section.Key.Equals("General", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (section.Key.Equals("DEV", StringComparison.OrdinalIgnoreCase))
            {
                developerZone = true;
                continue;
            }
            if (developerZone && !request.DeveloperFeatures)
            {
                continue;
            }

            var name = section.Value.GetValueOrDefault("Name", section.Key);
            if (request.PluginIndex == 0)
            {
                var key = $"Plugin_{section.Key}";
                var localized = key.GetLocalized();
                name = localized != key ? localized : name;
            }
            settings.Add(new ConfigurationSetting(section.Key, name, section.Value.GetValueOrDefault("Type", "string"),
                section.Value.GetValueOrDefault("Value", string.Empty), section.Value.GetValueOrDefault("help", string.Empty)));
        }

        return new ConfigurationLoadSnapshot(true, general.GetValueOrDefault("Name", "未知插件"),
            general.GetValueOrDefault("Description", "无描述"), general.GetValueOrDefault("Developer", "未知作者"),
            File.GetLastWriteTime(request.IniPath).ToString("yyyy-MM-dd HH:mm:ss"), settings, presets);
    }

    private sealed record ConfigurationPreferences(bool UseKeyListInput, bool AutoCreatePresets,
        bool DeveloperFeatures, Dictionary<string, List<string>> PinnedSections);

    private sealed record ConfigurationLoadRequest(IniFile IniFile, string IniPath, string DllPath, string PresetsDirectory,
        int PluginIndex, bool LightweightMode, bool DeveloperFeatures, bool AutoCreatePresets);

    private sealed record ConfigurationSetting(string Section, string Name, string Type, string Value, string Help);

    private sealed record ConfigurationLoadSnapshot(bool Exists, string Name, string Description, string Developer,
        string LastModified, IReadOnlyList<ConfigurationSetting> Settings, PresetLoadResult? PresetState);
}