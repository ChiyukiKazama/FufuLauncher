/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace FufuLauncher.ViewModels;

public partial class MainViewModel
{
    #region 背景管理

    [ObservableProperty] private ImageSource _backgroundImageSource;
    [ObservableProperty] private MediaPlayer _backgroundVideoPlayer;
    private InMemoryRandomAccessStream _backgroundVideoStream;
    [ObservableProperty] private bool _isVideoBackground;
    [ObservableProperty] private bool _isBackgroundLoading;

    [ObservableProperty] private string _customBackgroundPath;
    [ObservableProperty] private bool _hasCustomBackground;

    [ObservableProperty] private ObservableCollection<BackgroundUrlInfo> _availableBackgrounds = new();

    public IAsyncRelayCommand<BackgroundUrlInfo> SelectSpecificBackgroundCommand
    {
        get;
    }

    [ObservableProperty] private bool _preferVideoBackground = true;

    [ObservableProperty] private bool _isBackgroundToggleEnabled = true;

    public Visibility ImageVisibility => IsVideoBackground ? Visibility.Collapsed : Visibility.Visible;
    public Visibility VideoVisibility => IsVideoBackground ? Visibility.Visible : Visibility.Collapsed;
    public string BackgroundTypeToggleText => "Main_WallpaperSwitch".GetLocalized();

    public IAsyncRelayCommand LoadBackgroundCommand
    {
        get;
    }

    public IRelayCommand ToggleBackgroundTypeCommand
    {
        get;
    }

    partial void OnIsVideoBackgroundChanged(bool value)
    {
        OnPropertyChanged(nameof(ImageVisibility));
        OnPropertyChanged(nameof(VideoVisibility));
    }

    partial void OnHasCustomBackgroundChanged(bool value)
    {
        IsBackgroundToggleEnabled = !value;
    }

    private async Task LoadAvailableBackgroundsAsync()
    {
        try
        {
            var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
            int serverValue = serverJson != null ? Convert.ToInt32(serverJson) : 0;
            var server = (Models.ServerType)serverValue;

            var backgroundService = App.GetService<IHoyoverseBackgroundService>();
            var backgrounds = await backgroundService.GetAvailableBackgroundsAsync(server);

            var visibleBackgrounds = backgrounds
                .Where(b => _devBuildDetectionService.IsDevBuild || !b.IsVideo)
                .ToList();

            await UpdateUI(() =>
            {
                AvailableBackgrounds.Clear();
                foreach (var bg in visibleBackgrounds)
                {
                    AvailableBackgrounds.Add(bg);
                }
            });

            var imageUrls = backgrounds
                .Where(b => !b.IsVideo && !string.IsNullOrEmpty(b.Url))
                .Select(b => b.Url);
            _ = _backgroundRenderer.PreloadImageBackgroundsAsync(imageUrls);
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainViewModel.Background", $"Failed to load selectable backgrounds: {ex.Message}");
        }
    }

    private async Task SelectSpecificBackgroundAsync(BackgroundUrlInfo info)
    {
        if (info == null) return;

        if (info.IsVideo && !_devBuildDetectionService.IsDevBuild)
        {
            _notificationService.Show("Main_LiveWallpaper".GetLocalized(),
                "Main_LiveWallpaperNotSupported".GetLocalized(), NotificationType.Warning, 5000);
            return;
        }

        await _localSettingsService.SaveSettingAsync("IsRandomBackgroundEnabled", false);
        await _localSettingsService.SaveSettingAsync("IsBingBackgroundEnabled", false);
        await _localSettingsService.SaveSettingAsync("IsBackgroundSlideshowEnabled", false);
        await _localSettingsService.SaveSettingAsync(LocalSettingsService.IsBackgroundEnabledKey, false);
        RandomBackgroundService.ClearCache();
        await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", info.Url);
        await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", info.IsVideo);

        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    public async Task LoadCustomBackgroundPathAsync()
    {
        var path = await _localSettingsService.ReadSettingAsync("CustomBackgroundPath");
        if (path != null)
        {
            CustomBackgroundPath = path.ToString();
            HasCustomBackground = File.Exists(CustomBackgroundPath);
        }
        else
        {
            HasCustomBackground = false;
        }

        IsBackgroundToggleEnabled = !HasCustomBackground;
    }

    private async Task SwitchToStaticBackgroundOnVersionChangeAsync()
    {
        try
        {
            var currentVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "";
            var lastVersion = await _localSettingsService.ReadSettingAsync("LastAppVersion");
            string lastVersionStr = lastVersion?.ToString() ?? "";

            if (!string.IsNullOrEmpty(lastVersionStr) && lastVersionStr != currentVersion)
            {
                if (PreferVideoBackground)
                {
                    PreferVideoBackground = false;
                    await _localSettingsService.SaveSettingAsync("PreferVideoBackground", false);
                    await _localSettingsService.SaveSettingAsync("UserPreferVideoBackground", false);
                    DebugLog.Info("MainViewModel.Background",
                        $"[MainViewModel] Version changed ({lastVersionStr} -> {currentVersion}), switched animated background to static");
                }
            }

            await _localSettingsService.SaveSettingAsync("LastAppVersion", currentVersion);
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainViewModel.Background",
                $"[MainViewModel] Version-change background switch check failed: {ex.Message}");
        }
    }

    private async Task LoadBackgroundAsync()
    {
        await UpdateUI(() => IsBackgroundLoading = true);

        try
        {
            BackgroundRenderResult bgResult = null;
            if (HasCustomBackground && !string.IsNullOrEmpty(CustomBackgroundPath) && File.Exists(CustomBackgroundPath))
            {
                bgResult = await _backgroundRenderer.GetCustomBackgroundAsync(CustomBackgroundPath);
            }

            if (bgResult == null)
            {
                var serverJson = await _localSettingsService.ReadSettingAsync("BackgroundServerKey");
                var server = Models.ServerType.CN;
                try
                {
                    if (serverJson != null) server = (Models.ServerType)Convert.ToInt32(serverJson);
                }
                catch
                {
                }

                var preferVideo = PreferVideoBackground && _devBuildDetectionService.IsDevBuild;
                bgResult = await _backgroundRenderer.GetBackgroundAsync(server, preferVideo);
            }

            bgResult ??= await _backgroundRenderer.GetFallbackBackgroundAsync();
            await UpdateUI(() =>
            {
                if (bgResult == null) return;
                ClearBackground();
                if (bgResult.IsVideo && bgResult.VideoSource != null && _devBuildDetectionService.IsDevBuild)
                    SetupVideoPlayer(bgResult.VideoSource, bgResult.VideoStream);
                else
                    BackgroundImageSource = bgResult.ImageSource;
            });
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainViewModel.Background", $"Background load exception: {ex.Message}");
            var fallback = await _backgroundRenderer.GetFallbackBackgroundAsync();
            if (fallback != null)
                await UpdateUI(() =>
                {
                    ClearBackground();
                    BackgroundImageSource = fallback.ImageSource;
                });
        }
        finally
        {
            await UpdateUI(() => IsBackgroundLoading = false);
        }
    }

    private void SetupVideoPlayer(MediaSource source, InMemoryRandomAccessStream stream)
    {
        _backgroundVideoStream = stream;

        if (BackgroundVideoPlayer == null)
        {
            BackgroundVideoPlayer = MediaPlayerHelper.CreateLoopingMutedPlayer();
            BackgroundVideoPlayer.MediaFailed += BackgroundVideoPlayer_MediaFailed;
        }

        BackgroundVideoPlayer.Source = source;
        BackgroundVideoPlayer.Play();
        IsVideoBackground = true;
    }

    private void ClearBackground()
    {
        BackgroundImageSource = null;
        if (BackgroundVideoPlayer != null)
        {
            BackgroundVideoPlayer.Pause();
            BackgroundVideoPlayer.MediaFailed -= BackgroundVideoPlayer_MediaFailed;
            try
            {
                BackgroundVideoPlayer.Dispose();
            }
            catch
            {
            }

            BackgroundVideoPlayer = null;
        }

        _backgroundVideoStream?.Dispose();
        _backgroundVideoStream = null;
        IsVideoBackground = false;
    }

    private void BackgroundVideoPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DebugLog.Info("MainViewModel.Background", $"Background video raised MediaFailed, error type: {args.Error}");
    }

    private void ToggleBackgroundType()
    {
        if (!_devBuildDetectionService.IsDevBuild)
        {
            _notificationService.Show("Main_LiveWallpaper".GetLocalized(),
                "Main_LiveWallpaperNotSupported".GetLocalized(), NotificationType.Warning, 5000);
            return;
        }

        PreferVideoBackground = !PreferVideoBackground;
        OnPropertyChanged(nameof(BackgroundTypeToggleText));
        _ = _localSettingsService.SaveSettingAsync("UserPreferVideoBackground", PreferVideoBackground);
        _ = _localSettingsService.SaveSettingAsync("PreferVideoBackground", PreferVideoBackground);
        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    #endregion
}