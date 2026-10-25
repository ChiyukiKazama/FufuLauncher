/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.ViewModels;

public partial class SettingsViewModel
{
    #region 背景设置

    private int _backgroundQualityChangeVersion;
    private bool _backgroundQualityLoaded;
    private bool _backgroundAntialiasLoaded;
    private CancellationTokenSource? _antialiasSaveCts;
    private CancellationTokenSource? _antialiasRefreshCts;
    private bool _isBackgroundImageAntialiasEnabled;
    private double _backgroundImageAntialiasStrength = BackgroundImageAntialiasSettings.DefaultStrength;

    public bool IsBackgroundImageAntialiasEnabled
    {
        get => _isBackgroundImageAntialiasEnabled;
        set
        {
            if (_isBackgroundImageAntialiasEnabled == value) return;
            OnPropertyChanging(nameof(IsBackgroundImageAntialiasEnabled));
            _isBackgroundImageAntialiasEnabled = value;
            OnPropertyChanged(nameof(IsBackgroundImageAntialiasEnabled));
            OnIsBackgroundImageAntialiasEnabledChanged(value);
        }
    }

    public double BackgroundImageAntialiasStrength
    {
        get => _backgroundImageAntialiasStrength;
        set
        {
            if (_backgroundImageAntialiasStrength == value) return;
            OnPropertyChanging(nameof(BackgroundImageAntialiasStrength));
            _backgroundImageAntialiasStrength = value;
            OnPropertyChanged(nameof(BackgroundImageAntialiasStrength));
            OnBackgroundImageAntialiasStrengthChanged(value);
        }
    }

    private async Task ResetBackgroundApiAsync()
    {
        CustomBackgroundApiUrl = string.Empty;
        CurrentBackgroundApiUrl = GetDefaultBackgroundApiUrl(SelectedServer);
        await _localSettingsService.SaveSettingAsync("CustomBackgroundApiUrl", string.Empty);
        await _localSettingsService.SaveSettingAsync("BackgroundJsonHash", string.Empty);
        await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", string.Empty);
        await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    private static string GetDefaultBackgroundApiUrl(ServerType server)
    {
        return server switch
        {
            ServerType.CN => ApiEndpoints.BackgroundCnApi,
            ServerType.OS => ApiEndpoints.BackgroundOsApi,
            _ => ApiEndpoints.BackgroundCnApi
        };
    }

    private async Task ClearCustomBackgroundAsync()
    {
        try
        {
            await _localSettingsService.SaveSettingAsync<string>("CustomBackgroundPath", null);
            CustomBackgroundPath = null;
            HasCustomBackground = false;

            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsViewModel.Background", $"Failed to clear custom background: {ex.Message}");
        }
    }

    private async void ShowDialogMessage(string title, string content)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "Dialog_OK".GetLocalized(),
                XamlRoot = App.MainWindow.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch
        {
        }
    }

    private async Task LoadCustomBackgroundSettingsAsync()
    {
        _backgroundAntialiasLoaded = false;

        var path = await _localSettingsService.ReadSettingAsync("CustomBackgroundPath");
        if (path != null)
        {
            CustomBackgroundPath = path.ToString();
            HasCustomBackground = File.Exists(CustomBackgroundPath);
        }
        else
        {
            CustomBackgroundPath = null;
            HasCustomBackground = false;
        }

        var isSlideshowEnabledJson = await _localSettingsService.ReadSettingAsync("IsBackgroundSlideshowEnabled");
        IsBackgroundSlideshowEnabled = isSlideshowEnabledJson != null && Convert.ToBoolean(isSlideshowEnabledJson);

        var slideshowFolderJson = await _localSettingsService.ReadSettingAsync("BackgroundSlideshowFolder");
        if (slideshowFolderJson != null)
        {
            BackgroundSlideshowFolder = slideshowFolderJson.ToString();
            HasBackgroundSlideshowFolder = Directory.Exists(BackgroundSlideshowFolder);
        }
        else
        {
            BackgroundSlideshowFolder = null;
            HasBackgroundSlideshowFolder = false;
        }

        var slideshowIntervalJson = await _localSettingsService.ReadSettingAsync("BackgroundSlideshowInterval");
        if (slideshowIntervalJson != null)
        {
            BackgroundSlideshowInterval = Convert.ToInt32(slideshowIntervalJson);
        }
        else
        {
            BackgroundSlideshowInterval = 60;
        }

        var randomBackgroundEnabledJson = await _localSettingsService.ReadSettingAsync("IsRandomBackgroundEnabled");
        IsRandomBackgroundEnabled =
            randomBackgroundEnabledJson != null && Convert.ToBoolean(randomBackgroundEnabledJson);

        var randomBackgroundTypeJson = await _localSettingsService.ReadSettingAsync("RandomBackgroundType");
        RandomBackgroundType = RandomBackgroundService.ParseType(randomBackgroundTypeJson?.ToString());

        BackgroundQuality = await BackgroundImageQualitySettings.GetEffectiveQualityAsync();
        _backgroundQualityLoaded = true;

        var antialias = await BackgroundImageAntialiasSettings.GetAsync();
        IsBackgroundImageAntialiasEnabled = antialias.Enabled;
        BackgroundImageAntialiasStrength = antialias.Strength;
        _backgroundAntialiasLoaded = true;
    }

    partial void OnCustomBackgroundApiUrlChanged(string value)
    {
        if (_isInitializing) return;
        var normalized = value?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(normalized) &&
            (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            return;
        }

        _ = _localSettingsService.SaveSettingAsync("CustomBackgroundApiUrl", normalized);
        CurrentBackgroundApiUrl = string.IsNullOrWhiteSpace(normalized)
            ? GetDefaultBackgroundApiUrl(SelectedServer)
            : normalized;
        _ = _localSettingsService.SaveSettingAsync("BackgroundJsonHash", string.Empty);
        _ = _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", string.Empty);
        _ = _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    partial void OnSelectedServerChanged(ServerType value)
    {
        if (_isInitializing) return;
        DebugLog.Info("SettingsViewModel.Background", $"SettingsViewModel: saved server setting {value}");
        CurrentBackgroundApiUrl = string.IsNullOrWhiteSpace(CustomBackgroundApiUrl)
            ? GetDefaultBackgroundApiUrl(value)
            : CustomBackgroundApiUrl;
        _ = _localSettingsService.SaveSettingAsync(LocalSettingsService.BackgroundServerKey, (int)value);
        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    partial void OnBackgroundQualityChanged(BackgroundImageQuality value)
    {
        if (_isInitializing || !_backgroundQualityLoaded ||
            !Enum.IsDefined(typeof(BackgroundImageQuality), value)) return;
        _ = ApplyBackgroundQualityAsync(value);
    }

    private async Task ApplyBackgroundQualityAsync(BackgroundImageQuality value)
    {
        var version = ++_backgroundQualityChangeVersion;
        try
        {
            await BackgroundImageQualitySettings.SetUserQualityAsync(value);
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsViewModel.Background", $"Failed to save background image quality: {ex.Message}");
        }

        if (version != _backgroundQualityChangeVersion)
            return;

        _backgroundRenderer.ClearImageCaches();
        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    private void OnIsBackgroundImageAntialiasEnabledChanged(bool value)
    {
        if (_isInitializing || !_backgroundAntialiasLoaded) return;
        _ = SaveBackgroundAntialiasEnabledAsync(value);
    }

    private async Task SaveBackgroundAntialiasEnabledAsync(bool value)
    {
        try
        {
            await BackgroundImageAntialiasSettings.SetEnabledAsync(value);
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsViewModel.Background",
                $"Failed to save background antialias enabled state: {ex.Message}");
        }

        ScheduleBackgroundAntialiasRefresh();
    }

    private void OnBackgroundImageAntialiasStrengthChanged(double value)
    {
        if (_isInitializing || !_backgroundAntialiasLoaded) return;

        _antialiasSaveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _antialiasSaveCts = cts;
        _ = SaveBackgroundAntialiasStrengthAsync((int)Math.Round(Math.Clamp(value, 0, 100)), cts.Token);
    }

    private async Task SaveBackgroundAntialiasStrengthAsync(int value, CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        try
        {
            await BackgroundImageAntialiasSettings.SetStrengthAsync(value);
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsViewModel.Background",
                $"Failed to save background antialias strength: {ex.Message}");
        }

        if (IsBackgroundImageAntialiasEnabled)
            ScheduleBackgroundAntialiasRefresh();
    }

    private void ScheduleBackgroundAntialiasRefresh()
    {
        _antialiasRefreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _antialiasRefreshCts = cts;
        _ = RefreshBackgroundAntialiasAsync(cts.Token);
    }

    private async Task RefreshBackgroundAntialiasAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        _backgroundRenderer.ClearImageCaches();
        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    partial void OnIsBackgroundSlideshowEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        if (value && IsRandomBackgroundEnabled)
            IsRandomBackgroundEnabled = false;
        if (value && !IsBackgroundEnabled)
            IsBackgroundEnabled = true;
        _ = _localSettingsService.SaveSettingAsync("IsBackgroundSlideshowEnabled", value);
        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    partial void OnIsRandomBackgroundEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = ApplyRandomBackgroundSettingAsync(value);
    }

    partial void OnRandomBackgroundTypeChanged(RandomBackgroundType value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("RandomBackgroundType", value.ToString());
        RandomBackgroundService.ClearCache();
        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    private async Task ApplyRandomBackgroundSettingAsync(bool value)
    {
        await _localSettingsService.SaveSettingAsync("IsRandomBackgroundEnabled", value);

        if (value)
        {
            if (IsBackgroundEnabled) IsBackgroundEnabled = false;
            if (IsBackgroundSlideshowEnabled) IsBackgroundSlideshowEnabled = false;
        }
        else
        {
            RandomBackgroundService.ClearCache();
        }

        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    partial void OnBackgroundSlideshowIntervalChanged(int value)
    {
        if (_isInitializing) return;
        if (value < 1) value = 1;
        _ = _localSettingsService.SaveSettingAsync("BackgroundSlideshowInterval", value);
        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    partial void OnIsBackgroundEnabledChanged(bool value)
    {
        if (_isInitializing) return;

        DebugLog.Info("SettingsViewModel.Background", $"SettingsViewModel: saved custom background toggle {value}");
        if (value && IsRandomBackgroundEnabled)
            IsRandomBackgroundEnabled = false;
        _ = _localSettingsService.SaveSettingAsync(LocalSettingsService.IsBackgroundEnabledKey, value);

        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());

        if (!value)
        {
            _backgroundRenderer.ClearCustomBackground();
        }
    }

    private async Task SelectCustomBackgroundAsync()
    {
        try
        {
            var path = await _filePickerService.PickImageOrVideoAsync();
            if (!string.IsNullOrEmpty(path))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var isVideo = ext is ".mp4" or ".webm" or ".mkv" or ".avi" or ".mov";
                if (isVideo && !_devBuildDetectionService.IsDevBuild)
                {
                    ShowDialogMessage("Common_Notice".GetLocalized(), "Settings_LiveWallpaperDevOnly".GetLocalized());
                    return;
                }

                CustomBackgroundPath = path;
                HasCustomBackground = true;
                if (IsRandomBackgroundEnabled)
                    IsRandomBackgroundEnabled = false;
                if (!IsBackgroundEnabled)
                    IsBackgroundEnabled = true;
                await _localSettingsService.SaveSettingAsync("CustomBackgroundPath", path);

                WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
                await RefreshMainPageBackground();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsViewModel.Background", $"Failed to select custom background: {ex.Message}");
        }
    }

    private async Task SelectBackgroundSlideshowFolderAsync()
    {
        try
        {
            var folder = await _filePickerService.PickFolderAsync();
            if (!string.IsNullOrEmpty(folder))
            {
                BackgroundSlideshowFolder = folder;
                HasBackgroundSlideshowFolder = true;
                await _localSettingsService.SaveSettingAsync("BackgroundSlideshowFolder", folder);

                WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsViewModel.Background", $"Failed to select carousel folder: {ex.Message}");
        }
    }

    private async Task ClearBackgroundSlideshowFolderAsync()
    {
        try
        {
            await _localSettingsService.SaveSettingAsync<string>("BackgroundSlideshowFolder", null);
            BackgroundSlideshowFolder = null;
            HasBackgroundSlideshowFolder = false;

            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsViewModel.Background", $"Failed to clear carousel folder: {ex.Message}");
        }
    }

    private async Task RefreshMainPageBackground()
    {
        await Task.CompletedTask;
    }

    #endregion
}