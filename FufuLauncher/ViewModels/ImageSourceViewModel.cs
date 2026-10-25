/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.ViewModels;

public enum ImageSourceTab
{
    Official,
    RandomBlueArchive,
    Bing,
    Custom,
    Slideshow
}

public sealed record BingYearOption(int Year, string Label);

public partial class ImageSourceViewModel : ObservableObject
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IHoyoverseBackgroundService _hoyoverseBackgroundService;
    private readonly BingWallpaperService _bingWallpaperService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDevBuildDetectionService _devBuildDetectionService;

    private readonly SemaphoreSlim _sourceFlagsGate = new(1, 1);
    private bool _isLoadingSettings;
    private bool _isStepping;
    private bool _isUpdatingRandomPin;
    private int _requestVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOfficialSelected))]
    [NotifyPropertyChangedFor(nameof(IsRandomSelected))]
    [NotifyPropertyChangedFor(nameof(IsBingSelected))]
    [NotifyPropertyChangedFor(nameof(IsCustomSelected))]
    [NotifyPropertyChangedFor(nameof(IsSlideshowSelected))]
    [NotifyPropertyChangedFor(nameof(CanStep))]
    private ImageSourceTab _selectedTab;

    [ObservableProperty] private string _sourceTitle = string.Empty;
    [ObservableProperty] private string _sourceCaption = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    [ObservableProperty] private string _bingCopyright = string.Empty;
    [ObservableProperty] private bool _bingChangeOnStartup = true;
    private bool _isBingOfficial;
    private BingYearOption? _selectedBingYearOption;

    public IReadOnlyList<BingYearOption> BingYearOptions
    {
        get;
    }

    public bool IsBingOfficial
    {
        get => _isBingOfficial;
        set
        {
            if (_isBingOfficial == value) return;
            OnPropertyChanging(nameof(IsBingOfficial));
            _isBingOfficial = value;
            OnPropertyChanged(nameof(IsBingOfficial));
            OnIsBingOfficialChanged(value);
        }
    }

    public BingYearOption? SelectedBingYearOption
    {
        get => _selectedBingYearOption;
        set
        {
            if (Equals(_selectedBingYearOption, value)) return;
            OnPropertyChanging(nameof(SelectedBingYearOption));
            _selectedBingYearOption = value;
            OnPropertyChanged(nameof(SelectedBingYearOption));
            OnSelectedBingYearOptionChanged(value);
        }
    }

    [ObservableProperty] private string _customBackgroundApiUrl = string.Empty;
    [ObservableProperty] private string _currentBackgroundApiUrl = string.Empty;
    [ObservableProperty] private string _customPath = string.Empty;
    [ObservableProperty] private string _slideshowFolder = string.Empty;
    [ObservableProperty] private int _slideshowInterval = 60;
    [ObservableProperty] private ServerType _selectedServer;
    [ObservableProperty] private RandomBackgroundType _randomType = RandomBackgroundType.All;
    private bool _isRandomBackgroundPinned;

    public bool IsRandomBackgroundPinned
    {
        get => _isRandomBackgroundPinned;
        set
        {
            if (_isRandomBackgroundPinned == value) return;
            OnPropertyChanging(nameof(IsRandomBackgroundPinned));
            _isRandomBackgroundPinned = value;
            OnPropertyChanged(nameof(IsRandomBackgroundPinned));
            OnIsRandomBackgroundPinnedChanged(value);
        }
    }

    [ObservableProperty] private bool _hasCustomBackground;
    [ObservableProperty] private bool _hasSlideshowFolder;
    [ObservableProperty] private bool _isPreviewLoading;
    [ObservableProperty] private bool _isPreviewFallback;
    [ObservableProperty] private bool _isPreviewVideo;
    [ObservableProperty] private string _previewGlyph = "\uEB9F";
    [ObservableProperty] private ImageSource? _previewImage;

    public bool IsOfficialSelected => SelectedTab == ImageSourceTab.Official;

    public bool IsRandomSelected => SelectedTab == ImageSourceTab.RandomBlueArchive;

    public bool IsBingSelected => SelectedTab == ImageSourceTab.Bing;

    public bool IsCustomSelected => SelectedTab == ImageSourceTab.Custom;

    public bool IsSlideshowSelected => SelectedTab == ImageSourceTab.Slideshow;

    public bool CanStep => SelectedTab != ImageSourceTab.Custom;

    public bool IsBusy => _isStepping;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    public IAsyncRelayCommand NextCommand
    {
        get;
    }

    public IAsyncRelayCommand PreviousCommand
    {
        get;
    }

    public IAsyncRelayCommand SelectCustomBackgroundCommand
    {
        get;
    }

    public IAsyncRelayCommand ClearCustomBackgroundCommand
    {
        get;
    }

    public IAsyncRelayCommand SelectSlideshowFolderCommand
    {
        get;
    }

    public IAsyncRelayCommand ClearSlideshowFolderCommand
    {
        get;
    }

    public IAsyncRelayCommand SaveBackgroundApiCommand
    {
        get;
    }

    public IAsyncRelayCommand ResetBackgroundApiCommand
    {
        get;
    }

    public ImageSourceViewModel(
        ILocalSettingsService localSettingsService,
        IHoyoverseBackgroundService hoyoverseBackgroundService,
        BingWallpaperService bingWallpaperService,
        IFilePickerService filePickerService,
        IDevBuildDetectionService devBuildDetectionService)
    {
        _localSettingsService = localSettingsService;
        _hoyoverseBackgroundService = hoyoverseBackgroundService;
        _bingWallpaperService = bingWallpaperService;
        _filePickerService = filePickerService;
        _devBuildDetectionService = devBuildDetectionService;

        NextCommand = new AsyncRelayCommand(() => StepAsync(1));
        PreviousCommand = new AsyncRelayCommand(() => StepAsync(-1));
        SelectCustomBackgroundCommand = new AsyncRelayCommand(SelectCustomBackgroundAsync);
        ClearCustomBackgroundCommand = new AsyncRelayCommand(ClearCustomBackgroundAsync);
        SelectSlideshowFolderCommand = new AsyncRelayCommand(SelectSlideshowFolderAsync);
        ClearSlideshowFolderCommand = new AsyncRelayCommand(ClearSlideshowFolderAsync);
        SaveBackgroundApiCommand = new AsyncRelayCommand(SaveBackgroundApiAsync);
        ResetBackgroundApiCommand = new AsyncRelayCommand(ResetBackgroundApiAsync);

        var years = new List<BingYearOption>
        {
            new(0, "ImageSource_BingYearAll".GetLocalized())
        };
        for (var year = DateTime.Now.Year; year >= 2019; year--)
            years.Add(new BingYearOption(year, year.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        BingYearOptions = years;
    }

    private sealed record ImageSourceSettingsSnapshot(
        ServerType Server,
        string CustomApiUrl,
        string CustomPathValue,
        string SlideshowFolderValue,
        int SlideshowIntervalValue,
        RandomBackgroundType RandomTypeValue,
        string BingCopyrightValue,
        bool BingChangeOnStartupValue,
        bool IsRandom,
        bool IsPinned,
        bool IsBing,
        bool IsBackgroundEnabled,
        bool IsSlideshow,
        int BingYear,
        bool IsBingOfficial);

    public async Task InitializeAsync()
    {
        var started = Environment.TickCount64;
        DebugLog.Info("ImageSourceViewModel", "VM.Initialize.Start");
        _isLoadingSettings = true;
        try
        {
            var snapshot = await Task.Run(LoadSettingsSnapshotAsync);

            SelectedServer = snapshot.Server;
            CustomBackgroundApiUrl = snapshot.CustomApiUrl;
            CurrentBackgroundApiUrl = string.IsNullOrWhiteSpace(CustomBackgroundApiUrl)
                ? GetDefaultBackgroundApiUrl(SelectedServer)
                : CustomBackgroundApiUrl;
            CustomPath = snapshot.CustomPathValue;
            HasCustomBackground = !string.IsNullOrWhiteSpace(CustomPath) && File.Exists(CustomPath);
            SlideshowFolder = snapshot.SlideshowFolderValue;
            HasSlideshowFolder = !string.IsNullOrWhiteSpace(SlideshowFolder) && Directory.Exists(SlideshowFolder);
            SlideshowInterval = snapshot.SlideshowIntervalValue;
            RandomType = snapshot.RandomTypeValue;
            IsRandomBackgroundPinned = snapshot.IsPinned;
            BingCopyright = snapshot.BingCopyrightValue;
            BingChangeOnStartup = snapshot.BingChangeOnStartupValue;
            SelectedBingYearOption = BingYearOptions.FirstOrDefault(option => option.Year == snapshot.BingYear)
                                     ?? BingYearOptions[0];
            IsBingOfficial = snapshot.IsBingOfficial;

            SelectedTab = snapshot.IsRandom
                ? ImageSourceTab.RandomBlueArchive
                : snapshot.IsBing
                    ? ImageSourceTab.Bing
                    : snapshot.IsBackgroundEnabled && snapshot.IsSlideshow && HasSlideshowFolder
                        ? ImageSourceTab.Slideshow
                        : snapshot.IsBackgroundEnabled && HasCustomBackground
                            ? ImageSourceTab.Custom
                            : ImageSourceTab.Official;
        }
        finally
        {
            _isLoadingSettings = false;
        }

        UpdateSourceInfo();
        IsPreviewLoading = false;

        if (SelectedTab == ImageSourceTab.Custom && !HasCustomBackground)
            ClearPreview("ImageSource_CustomEmpty");
        else if (SelectedTab == ImageSourceTab.Slideshow && !HasSlideshowFolder)
            ClearPreview("ImageSource_SlideshowEmpty");

        DebugLog.Info("ImageSourceViewModel", "VM.Initialize.End",
            $"{Environment.TickCount64 - started}ms tab={SelectedTab}");
    }

    private async Task<ImageSourceSettingsSnapshot> LoadSettingsSnapshotAsync()
    {
        var started = Environment.TickCount64;
        DebugLog.Info("ImageSourceViewModel", "VM.Snapshot.Start");
        var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
        var server = serverJson != null ? (ServerType)Convert.ToInt32(serverJson) : ServerType.CN;

        var customApiUrl =
            (await _localSettingsService.ReadSettingAsync("CustomBackgroundApiUrl"))?.ToString() ?? string.Empty;
        var customPath = (await _localSettingsService.ReadSettingAsync("CustomBackgroundPath"))?.ToString() ??
                         string.Empty;
        var slideshowFolder =
            (await _localSettingsService.ReadSettingAsync("BackgroundSlideshowFolder"))?.ToString() ?? string.Empty;
        var intervalJson = await _localSettingsService.ReadSettingAsync("BackgroundSlideshowInterval");
        var randomTypeJson = await _localSettingsService.ReadSettingAsync("RandomBackgroundType");
        var bingCopyright =
            (await _localSettingsService.ReadSettingAsync("BingWallpaperCopyright"))?.ToString() ?? string.Empty;
        var bingChange = await ReadBoolSettingAsync("IsBingBackgroundChangeOnStartupEnabled", true);
        var bingYearJson = await _localSettingsService.ReadSettingAsync("BingWallpaperYear");
        var bingYear = 0;
        if (bingYearJson != null)
            int.TryParse(bingYearJson.ToString(), out bingYear);
        var isBingOfficial = await ReadBoolSettingAsync("IsBingWallpaperOfficial");
        var isRandom = await ReadBoolSettingAsync("IsRandomBackgroundEnabled");
        var isPinned = await ReadBoolSettingAsync(RandomBackgroundService.PinnedSettingKey);
        var isBing = await ReadBoolSettingAsync("IsBingBackgroundEnabled");
        var isBackgroundEnabled = await ReadBoolSettingAsync(LocalSettingsService.IsBackgroundEnabledKey, true);
        var isSlideshow = await ReadBoolSettingAsync("IsBackgroundSlideshowEnabled");

        DebugLog.Info("ImageSourceViewModel", "VM.Snapshot.End", $"{Environment.TickCount64 - started}ms");
        return new ImageSourceSettingsSnapshot(
            server,
            customApiUrl,
            string.IsNullOrWhiteSpace(customPath) ? string.Empty : customPath,
            string.IsNullOrWhiteSpace(slideshowFolder) ? string.Empty : slideshowFolder,
            intervalJson != null ? Math.Max(1, Convert.ToInt32(intervalJson)) : 60,
            RandomBackgroundService.ParseType(randomTypeJson?.ToString()),
            bingCopyright,
            bingChange,
            isRandom,
            isPinned,
            isBing,
            isBackgroundEnabled,
            isSlideshow,
            bingYear,
            isBingOfficial);
    }

    public async Task SelectTabAsync(ImageSourceTab tab, bool forceNewImage = false)
    {
        var started = Environment.TickCount64;
        DebugLog.Info("ImageSourceViewModel", "SelectTab.Start", $"{tab} force={forceNewImage}");
        var requestVersion = ++_requestVersion;
        if (App.MainWindow is MainWindow mainWindow)
            mainWindow.CancelPendingBackgroundLoad();
        _isStepping = true;
        SelectedTab = tab;
        StatusText = string.Empty;

        try
        {
            switch (tab)
            {
                case ImageSourceTab.Official:
                    await SaveSourceFlagsAsync(ImageSourceTab.Official, requestVersion);
                    if (requestVersion != _requestVersion) return;
                    StartLoading(requestVersion);
                    var officialUrl =
                        (await _localSettingsService.ReadSettingAsync("SelectedOnlineBackgroundUrl"))?.ToString();
                    if (requestVersion != _requestVersion) return;
                    if (forceNewImage || string.IsNullOrWhiteSpace(officialUrl))
                        await PickOfficialImageAsync(requestVersion);
                    else
                        RefreshBackground();
                    break;

                case ImageSourceTab.RandomBlueArchive:
                    await SaveSourceFlagsAsync(ImageSourceTab.RandomBlueArchive, requestVersion);
                    if (requestVersion != _requestVersion) return;
                    StartLoading(requestVersion);
                    await ReloadRandomAsync(forceNewImage, requestVersion);
                    break;

                case ImageSourceTab.Bing:
                    await SaveSourceFlagsAsync(ImageSourceTab.Bing, requestVersion);
                    if (requestVersion != _requestVersion) return;
                    StartLoading(requestVersion);
                    var bingUrl = (await _localSettingsService.ReadSettingAsync("BingWallpaperUrl"))?.ToString();
                    if (requestVersion != _requestVersion) return;
                    if (forceNewImage || string.IsNullOrWhiteSpace(bingUrl))
                        await PickBingImageAsync(requestVersion);
                    else
                        RefreshBackground();
                    break;

                case ImageSourceTab.Custom:
                    if (!HasCustomBackground)
                    {
                        ClearPreview("ImageSource_CustomEmpty");
                        return;
                    }

                    await SaveSourceFlagsAsync(ImageSourceTab.Custom, requestVersion);
                    if (requestVersion != _requestVersion) return;
                    StartLoading(requestVersion);
                    RefreshBackground();
                    break;

                case ImageSourceTab.Slideshow:
                    if (!HasSlideshowFolder)
                    {
                        ClearPreview("ImageSource_SlideshowEmpty");
                        return;
                    }

                    await SaveSourceFlagsAsync(ImageSourceTab.Slideshow, requestVersion);
                    if (requestVersion != _requestVersion) return;
                    await _localSettingsService.SaveSettingAsync("BackgroundSlideshowInterval", SlideshowInterval);
                    if (requestVersion != _requestVersion) return;
                    StartLoading(requestVersion);
                    RefreshBackground();
                    break;
            }
        }
        catch (Exception ex)
        {
            if (requestVersion == _requestVersion)
            {
                DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to switch image source: {ex.Message}");
                StatusText = ex.Message;
                IsPreviewLoading = false;
            }
        }
        finally
        {
            if (requestVersion == _requestVersion)
                _isStepping = false;
        }

        DebugLog.Info("ImageSourceViewModel", "SelectTab.End",
            $"{tab} in {Environment.TickCount64 - started}ms current={requestVersion == _requestVersion}");
    }

    public void ApplyBackgroundResult(BackgroundRenderResult? result)
    {
        DebugLog.Info("ImageSourceViewModel", "VM.ApplyBackgroundResult",
            $"source={(result?.ImageSource == null ? "null" : "set")} fallback={result?.IsFallback} tab={SelectedTab}");
        if (SelectedTab == ImageSourceTab.Custom && !HasCustomBackground) return;
        if (SelectedTab == ImageSourceTab.Slideshow && !HasSlideshowFolder) return;

        if (result == null || result.IsFallback || result.ImageSource == null)
        {
            PreviewImage = null;
            IsPreviewFallback = true;
            IsPreviewVideo = result?.IsVideo == true;
            PreviewGlyph = IsPreviewVideo ? "\uE714" : "\uEB9F";
        }
        else
        {
            PreviewImage = result.ImageSource;
            IsPreviewFallback = false;
            IsPreviewVideo = false;
            PreviewGlyph = "\uEB9F";
        }

        if (result != null && !result.IsFallback)
            StatusText = string.Empty;

        IsPreviewLoading = false;
    }

    public void SetPreviewFailure()
    {
        PreviewImage = null;
        IsPreviewFallback = true;
        IsPreviewVideo = false;
        PreviewGlyph = "\uEB9F";
        IsPreviewLoading = false;
    }

    partial void OnSelectedTabChanged(ImageSourceTab value)
    {
        UpdateSourceInfo();
    }

    partial void OnBingCopyrightChanged(string value)
    {
        UpdateSourceInfo();
    }

    partial void OnBingChangeOnStartupChanged(bool value)
    {
        if (_isLoadingSettings) return;
        _ = _localSettingsService.SaveSettingAsync("IsBingBackgroundChangeOnStartupEnabled", value);
    }

    private void OnIsBingOfficialChanged(bool value)
    {
        if (_isLoadingSettings) return;
        _ = HandleBingOfficialChangedAsync(value);
    }

    private void OnSelectedBingYearOptionChanged(BingYearOption? value)
    {
        if (_isLoadingSettings) return;
        _ = HandleBingYearChangedAsync(value);
    }

    private async Task HandleBingOfficialChangedAsync(bool value)
    {
        try
        {
            await _localSettingsService.SaveSettingAsync("IsBingWallpaperOfficial", value);
            if (SelectedTab == ImageSourceTab.Bing)
                await ReloadBingImageAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"Failed to change Bing official mode: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task HandleBingYearChangedAsync(BingYearOption? option)
    {
        if (option == null) return;

        try
        {
            await _localSettingsService.SaveSettingAsync("BingWallpaperYear", option.Year);
            if (!IsBingOfficial && SelectedTab == ImageSourceTab.Bing)
                await ReloadBingImageAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"Failed to change Bing wallpaper year: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task ReloadBingImageAsync()
    {
        var requestVersion = ++_requestVersion;
        StartLoading(requestVersion);
        await PickBingImageAsync(requestVersion);
    }

    partial void OnCustomPathChanged(string value)
    {
        UpdateSourceInfo();
    }

    partial void OnSlideshowFolderChanged(string value)
    {
        UpdateSourceInfo();
    }

    partial void OnHasCustomBackgroundChanged(bool value)
    {
        UpdateSourceInfo();
    }

    partial void OnHasSlideshowFolderChanged(bool value)
    {
        UpdateSourceInfo();
    }

    partial void OnSelectedServerChanged(ServerType value)
    {
        if (_isLoadingSettings) return;
        _ = HandleServerChangedAsync(value);
    }

    partial void OnRandomTypeChanged(RandomBackgroundType value)
    {
        if (_isLoadingSettings) return;
        _ = HandleRandomTypeChangedAsync(value);
    }

    private void OnIsRandomBackgroundPinnedChanged(bool value)
    {
        if (_isLoadingSettings || _isUpdatingRandomPin) return;
        _ = HandleRandomPinnedChangedAsync(value);
    }

    private async Task HandleRandomPinnedChangedAsync(bool value)
    {
        try
        {
            if (value && !await RandomBackgroundService.PinCurrentImageAsync())
            {
                _isUpdatingRandomPin = true;
                IsRandomBackgroundPinned = false;
                _isUpdatingRandomPin = false;
                StatusText = "ImageSource_RandomPinFailed".GetLocalized();
                return;
            }

            await _localSettingsService.SaveSettingAsync(RandomBackgroundService.PinnedSettingKey, value);

            if (value)
                StatusText = string.Empty;

            if (App.MainWindow is MainWindow mainWindow)
                await mainWindow.ReloadRandomBackgroundAsync(true);
            else
                WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"Failed to change random background pin state: {ex.Message}");
            _isUpdatingRandomPin = true;
            IsRandomBackgroundPinned = !value;
            _isUpdatingRandomPin = false;
            StatusText = "ImageSource_RandomPinFailed".GetLocalized();
        }
    }

    partial void OnSlideshowIntervalChanged(int value)
    {
        if (_isLoadingSettings) return;
        _ = HandleSlideshowIntervalChangedAsync(value);
    }

    private async Task HandleServerChangedAsync(ServerType server)
    {
        try
        {
            await _localSettingsService.SaveSettingAsync(LocalSettingsService.BackgroundServerKey, (int)server);
            CurrentBackgroundApiUrl = string.IsNullOrWhiteSpace(CustomBackgroundApiUrl)
                ? GetDefaultBackgroundApiUrl(server)
                : CustomBackgroundApiUrl;
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", string.Empty);
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
            await _localSettingsService.SaveSettingAsync("BackgroundJsonHash", string.Empty);
            UpdateSourceInfo();

            if (SelectedTab == ImageSourceTab.Official)
            {
                var requestVersion = ++_requestVersion;
                StartLoading(requestVersion);
                await PickOfficialImageAsync(requestVersion);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to switch official server: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task HandleRandomTypeChangedAsync(RandomBackgroundType type)
    {
        try
        {
            await _localSettingsService.SaveSettingAsync("RandomBackgroundType", type.ToString());
            UpdateSourceInfo();

            if (SelectedTab == ImageSourceTab.RandomBlueArchive)
            {
                var requestVersion = ++_requestVersion;
                StartLoading(requestVersion);
                await ReloadRandomAsync(true, requestVersion);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel",
                $"[ImageSource] Failed to save random background range: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task HandleSlideshowIntervalChangedAsync(int interval)
    {
        try
        {
            var normalized = Math.Max(1, interval);
            if (normalized != interval)
            {
                SlideshowInterval = normalized;
                return;
            }

            await _localSettingsService.SaveSettingAsync("BackgroundSlideshowInterval", normalized);

            if (SelectedTab == ImageSourceTab.Slideshow && HasSlideshowFolder)
            {
                StartLoading(++_requestVersion);
                RefreshBackground();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to save carousel interval: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task SelectCustomBackgroundAsync()
    {
        try
        {
            var path = await _filePickerService.PickImageOrVideoAsync();
            if (string.IsNullOrWhiteSpace(path)) return;

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var isVideo = extension is ".mp4" or ".webm" or ".mkv" or ".avi" or ".mov";
            if (isVideo && !_devBuildDetectionService.IsDevBuild)
            {
                StatusText = "Settings_LiveWallpaperDevOnly".GetLocalized();
                return;
            }

            var requestVersion = ++_requestVersion;
            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.CancelPendingBackgroundLoad();
            CustomPath = path;
            HasCustomBackground = true;
            await _localSettingsService.SaveSettingAsync("CustomBackgroundPath", path);
            if (requestVersion != _requestVersion) return;
            await SaveSourceFlagsAsync(ImageSourceTab.Custom, requestVersion);
            if (requestVersion != _requestVersion) return;
            SelectedTab = ImageSourceTab.Custom;
            StartLoading(requestVersion);
            RefreshBackground();
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to select custom background: {ex.Message}");
            StatusText = ex.Message;
            IsPreviewLoading = false;
        }
    }

    private async Task ClearCustomBackgroundAsync()
    {
        try
        {
            await _localSettingsService.SaveSettingAsync<string>("CustomBackgroundPath", null);
            CustomPath = string.Empty;
            HasCustomBackground = false;
            UpdateSourceInfo();

            if (SelectedTab == ImageSourceTab.Custom)
                ClearPreview("ImageSource_CustomEmpty");
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to clear custom background: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task SelectSlideshowFolderAsync()
    {
        try
        {
            var folder = await _filePickerService.PickFolderAsync();
            if (string.IsNullOrWhiteSpace(folder)) return;

            var requestVersion = ++_requestVersion;
            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.CancelPendingBackgroundLoad();
            SlideshowFolder = folder;
            HasSlideshowFolder = true;
            await _localSettingsService.SaveSettingAsync("BackgroundSlideshowFolder", folder);
            if (requestVersion != _requestVersion) return;
            await SaveSourceFlagsAsync(ImageSourceTab.Slideshow, requestVersion);
            if (requestVersion != _requestVersion) return;
            await _localSettingsService.SaveSettingAsync("BackgroundSlideshowInterval", SlideshowInterval);
            if (requestVersion != _requestVersion) return;
            SelectedTab = ImageSourceTab.Slideshow;
            StartLoading(requestVersion);
            RefreshBackground();
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to select carousel folder: {ex.Message}");
            StatusText = ex.Message;
            IsPreviewLoading = false;
        }
    }

    private async Task ClearSlideshowFolderAsync()
    {
        try
        {
            await _localSettingsService.SaveSettingAsync<string>("BackgroundSlideshowFolder", null);
            SlideshowFolder = string.Empty;
            HasSlideshowFolder = false;
            UpdateSourceInfo();

            if (SelectedTab == ImageSourceTab.Slideshow)
            {
                if (App.MainWindow is MainWindow mainWindow)
                    mainWindow.StopSlideshowBackground();

                ClearPreview("ImageSource_SlideshowEmpty");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to clear carousel folder: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task SaveBackgroundApiAsync()
    {
        try
        {
            var normalized = CustomBackgroundApiUrl.Trim();
            if (!string.IsNullOrEmpty(normalized) &&
                (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                StatusText = "ImageSource_InvalidApiUrl".GetLocalized();
                return;
            }

            CustomBackgroundApiUrl = normalized;
            await _localSettingsService.SaveSettingAsync("CustomBackgroundApiUrl", normalized);
            CurrentBackgroundApiUrl = string.IsNullOrWhiteSpace(normalized)
                ? GetDefaultBackgroundApiUrl(SelectedServer)
                : normalized;
            await _localSettingsService.SaveSettingAsync("BackgroundJsonHash", string.Empty);
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", string.Empty);
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
            StatusText = string.Empty;

            if (SelectedTab == ImageSourceTab.Official)
            {
                var requestVersion = ++_requestVersion;
                StartLoading(requestVersion);
                await PickOfficialImageAsync(requestVersion);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to save official API: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task ResetBackgroundApiAsync()
    {
        CustomBackgroundApiUrl = string.Empty;
        await SaveBackgroundApiAsync();
    }

    private async Task PickOfficialImageAsync(int requestVersion)
    {
        try
        {
            var server = SelectedServer;
            var backgrounds = await _hoyoverseBackgroundService.GetAvailableBackgroundsAsync(server);
            if (requestVersion != _requestVersion || SelectedTab != ImageSourceTab.Official) return;

            var images = backgrounds
                .Where(item => !item.IsVideo && !string.IsNullOrWhiteSpace(item.Url))
                .ToList();
            if (images.Count == 0)
            {
                StatusText = "OfficialBgWindow_Empty".GetLocalized();
                RefreshBackground();
                return;
            }

            var currentUrl =
                (await _localSettingsService.ReadSettingAsync("SelectedOnlineBackgroundUrl"))?.ToString();
            if (requestVersion != _requestVersion || SelectedTab != ImageSourceTab.Official) return;
            var candidates = string.IsNullOrWhiteSpace(currentUrl)
                ? images
                : images.Where(item => !string.Equals(item.Url, currentUrl, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            if (candidates.Count == 0) candidates = images;

            var selected = candidates[Random.Shared.Next(candidates.Count)];
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", selected.Url);
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
            if (requestVersion != _requestVersion || SelectedTab != ImageSourceTab.Official) return;
            RefreshBackground();
        }
        catch (Exception ex)
        {
            if (requestVersion == _requestVersion && SelectedTab == ImageSourceTab.Official)
            {
                DebugLog.Info("ImageSourceViewModel",
                    $"[ImageSource] Failed to fetch official backgrounds: {ex.Message}");
                StatusText = ex.Message;
                IsPreviewLoading = false;
            }
        }
    }

    private async Task PickBingImageAsync(int requestVersion)
    {
        try
        {
            var currentUrl = (await _localSettingsService.ReadSettingAsync("BingWallpaperUrl"))?.ToString();
            var wallpaper = await _bingWallpaperService.GetRandomWallpaperAsync(
                currentUrl,
                SelectedBingYearOption?.Year ?? 0,
                IsBingOfficial);
            if (requestVersion != _requestVersion || SelectedTab != ImageSourceTab.Bing) return;

            if (wallpaper == null)
            {
                StatusText = "ImageSource_BingEmpty".GetLocalized();
                RefreshBackground();
                return;
            }

            await _localSettingsService.SaveSettingAsync("BingWallpaperUrl", wallpaper.Url);
            await _localSettingsService.SaveSettingAsync("BingWallpaperCopyright", wallpaper.Copyright);
            if (requestVersion != _requestVersion || SelectedTab != ImageSourceTab.Bing) return;
            BingCopyright = wallpaper.Copyright;
            UpdateSourceInfo();
            RefreshBackground();
        }
        catch (Exception ex)
        {
            if (requestVersion == _requestVersion && SelectedTab == ImageSourceTab.Bing)
            {
                DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to fetch Bing wallpaper: {ex.Message}");
                StatusText = ex.Message;
                IsPreviewLoading = false;
            }
        }
    }

    private async Task ReloadRandomAsync(bool forceNewImage, int requestVersion)
    {
        if (requestVersion != _requestVersion) return;

        if (App.MainWindow is MainWindow mainWindow)
            await mainWindow.ReloadRandomBackgroundAsync(forceNewImage);
        else
            RefreshBackground();
    }

    private async Task StepAsync(int direction)
    {
        if (!CanStep || _isStepping) return;

        _isStepping = true;
        var requestVersion = ++_requestVersion;
        StatusText = string.Empty;

        try
        {
            switch (SelectedTab)
            {
                case ImageSourceTab.Official:
                    StartLoading(requestVersion);
                    await PickOfficialImageAsync(requestVersion);
                    break;

                case ImageSourceTab.RandomBlueArchive:
                    StartLoading(requestVersion);
                    await ReloadRandomAsync(true, requestVersion);
                    break;

                case ImageSourceTab.Bing:
                    StartLoading(requestVersion);
                    await PickBingImageAsync(requestVersion);
                    break;

                case ImageSourceTab.Slideshow:
                    StartLoading(requestVersion);
                    if (App.MainWindow is MainWindow mainWindow)
                    {
                        var moved = await mainWindow.StepSlideshowAsync(direction);
                        if (!moved && requestVersion == _requestVersion)
                            IsPreviewLoading = false;
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            if (requestVersion == _requestVersion)
            {
                DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to switch image: {ex.Message}");
                StatusText = ex.Message;
                IsPreviewLoading = false;
            }
        }
        finally
        {
            if (requestVersion == _requestVersion)
                _isStepping = false;
        }
    }

    private async Task SaveSourceFlagsAsync(ImageSourceTab tab, int requestVersion)
    {
        await _sourceFlagsGate.WaitAsync();
        try
        {
            if (requestVersion != _requestVersion) return;

            var random = tab == ImageSourceTab.RandomBlueArchive;
            var bing = tab == ImageSourceTab.Bing;
            var slideshow = tab == ImageSourceTab.Slideshow;
            var custom = tab is ImageSourceTab.Custom or ImageSourceTab.Slideshow;

            await _localSettingsService.SaveSettingAsync("IsRandomBackgroundEnabled", random);
            await _localSettingsService.SaveSettingAsync("IsBingBackgroundEnabled", bing);
            await _localSettingsService.SaveSettingAsync("IsBackgroundSlideshowEnabled", slideshow);
            await _localSettingsService.SaveSettingAsync(LocalSettingsService.IsBackgroundEnabledKey, custom);
        }
        finally
        {
            _sourceFlagsGate.Release();
        }
    }

    private void RefreshBackground()
    {
        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    private void StartLoading(int requestVersion)
    {
        IsPreviewLoading = true;
        _ = ClearLoadingAfterDelayAsync(requestVersion);
    }

    private async Task ClearLoadingAfterDelayAsync(int requestVersion)
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        if (requestVersion == _requestVersion && IsPreviewLoading)
            IsPreviewLoading = false;
    }

    private void ClearPreview(string statusKey)
    {
        DebugLog.Info("ImageSourceViewModel", "VM.ClearPreview", statusKey);
        PreviewImage = null;
        IsPreviewFallback = true;
        IsPreviewVideo = false;
        PreviewGlyph = "\uEB9F";
        IsPreviewLoading = false;
        StatusText = statusKey.GetLocalized();
    }

    private void UpdateSourceInfo()
    {
        SourceTitle = SelectedTab switch
        {
            ImageSourceTab.Official => "ImageSource_SourceOfficial".GetLocalized(),
            ImageSourceTab.RandomBlueArchive => "ImageSource_SourceRandom".GetLocalized(),
            ImageSourceTab.Bing => "ImageSource_SourceBing".GetLocalized(),
            ImageSourceTab.Custom => "ImageSource_SourceCustom".GetLocalized(),
            ImageSourceTab.Slideshow => "ImageSource_SourceSlideshow".GetLocalized(),
            _ => "ImageSource_Title".GetLocalized()
        };

        SourceCaption = SelectedTab switch
        {
            ImageSourceTab.Official => SelectedServer == ServerType.OS
                ? "ServerGlobal".GetLocalized()
                : "ServerCN".GetLocalized(),
            ImageSourceTab.RandomBlueArchive => RandomType switch
            {
                RandomBackgroundType.Home => "BgSourceRandomModeHome".GetLocalized(),
                RandomBackgroundType.Cg => "BgSourceRandomModeCg".GetLocalized(),
                _ => "BgSourceRandomModeAll".GetLocalized()
            },
            ImageSourceTab.Bing => string.IsNullOrWhiteSpace(BingCopyright)
                ? "ImageSource_SourceBing".GetLocalized()
                : BingCopyright,
            ImageSourceTab.Custom => HasCustomBackground
                ? CustomPath
                : "ImageSource_CustomEmpty".GetLocalized(),
            ImageSourceTab.Slideshow => HasSlideshowFolder
                ? SlideshowFolder
                : "ImageSource_SlideshowEmpty".GetLocalized(),
            _ => string.Empty
        };
    }

    private async Task<bool> ReadBoolSettingAsync(string key, bool fallback = false)
    {
        try
        {
            var value = await _localSettingsService.ReadSettingAsync(key);
            if (value is bool boolean) return boolean;
            if (value is string text && bool.TryParse(text, out var parsed)) return parsed;
            return value != null ? Convert.ToBoolean(value) : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string GetDefaultBackgroundApiUrl(ServerType server) => server switch
    {
        ServerType.OS => ApiEndpoints.BackgroundOsApi,
        _ => ApiEndpoints.BackgroundCnApi
    };
}