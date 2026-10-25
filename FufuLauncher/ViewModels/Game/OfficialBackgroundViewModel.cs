/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace FufuLauncher.ViewModels;

public partial class OfficialBackgroundViewModel : ObservableObject
{
    private static readonly HttpClient _httpClient;

    private static readonly string[] _videoExtensions = { ".mp4", ".webm", ".mkv", ".mov", ".avi" };

    private static readonly string[] _imageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };

    private readonly Window _ownerWindow;

    private readonly DispatcherQueue? _dispatcherQueue;

    private readonly ILocalSettingsService _localSettingsService;

    private readonly IHoyoverseBackgroundService _backgroundService;

    private DispatcherQueueTimer? _statusTimer;
    private InMemoryRandomAccessStream? _previewStream;
    private MediaSource? _previewSource;
    private string? _previewSourceUrl;

    static OfficialBackgroundViewModel()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36");
    }

    public OfficialBackgroundViewModel(Window ownerWindow)
    {
        _ownerWindow = ownerWindow;
        _dispatcherQueue = ownerWindow.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        _localSettingsService = App.GetService<ILocalSettingsService>();
        _backgroundService = App.GetService<IHoyoverseBackgroundService>();
    }

    public ObservableCollection<OfficialBackgroundItem> Backgrounds
    {
        get;
    } = new();

    #region 状态属性

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(ContentVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    [NotifyPropertyChangedFor(nameof(ErrorVisibility))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    [NotifyPropertyChangedFor(nameof(ErrorVisibility))]
    private bool _hasLoadError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailVisibility))]
    [NotifyPropertyChangedFor(nameof(NoSelectionVisibility))]
    [NotifyPropertyChangedFor(nameof(PreviewImageVisibility))]
    [NotifyPropertyChangedFor(nameof(PreviewVideoVisibility))]
    [NotifyPropertyChangedFor(nameof(PlayPreviewVisibility))]
    [NotifyPropertyChangedFor(nameof(ApplyVisibility))]
    [NotifyPropertyChangedFor(nameof(VideoHintVisibility))]
    [NotifyPropertyChangedFor(nameof(PosterNoteVisibility))]
    [NotifyPropertyChangedFor(nameof(CurrentBadgeVisibility))]
    [NotifyPropertyChangedFor(nameof(SelectedUrl))]
    private OfficialBackgroundItem? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImageVisibility))]
    [NotifyPropertyChangedFor(nameof(PreviewVideoVisibility))]
    [NotifyPropertyChangedFor(nameof(PreviewButtonText))]
    private bool _isPreviewPlaying;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PreviewLoadingVisibility))]
    private bool _isPreviewLoading;

    [ObservableProperty] private MediaPlayer? _previewPlayer;

    [ObservableProperty] private string _summaryText = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CustomBackgroundHintVisibility))]
    private bool _isCustomBackgroundInEffect;

    [ObservableProperty] private bool _isStatusOpen;

    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private InfoBarSeverity _statusSeverity = InfoBarSeverity.Success;

    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ContentVisibility => !IsLoading && !HasLoadError && Backgrounds.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility EmptyVisibility => !IsLoading && !HasLoadError && Backgrounds.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ErrorVisibility => !IsLoading && HasLoadError ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DetailVisibility => SelectedItem != null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoSelectionVisibility => SelectedItem == null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PreviewImageVisibility =>
        SelectedItem is { IsVideo: true } && IsPreviewPlaying ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PreviewVideoVisibility =>
        SelectedItem is { IsVideo: true } && IsPreviewPlaying ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PreviewLoadingVisibility => IsPreviewLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PlayPreviewVisibility =>
        SelectedItem is { IsVideo: true } ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ApplyVisibility => SelectedItem is { IsVideo: false } ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VideoHintVisibility =>
        SelectedItem is { IsVideo: true } ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PosterNoteVisibility => SelectedItem is { IsVideo: false, IsVideoPoster: true }
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility CurrentBadgeVisibility =>
        SelectedItem is { IsCurrent: true } ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CustomBackgroundHintVisibility =>
        IsCustomBackgroundInEffect ? Visibility.Visible : Visibility.Collapsed;

    public string SelectedUrl => SelectedItem?.Url ?? string.Empty;

    public string PreviewButtonText => IsPreviewPlaying
        ? "OfficialBgWindow_StopPreview".GetLocalized()
        : "OfficialBgWindow_PlayPreview".GetLocalized();

    partial void OnSelectedItemChanged(OfficialBackgroundItem? value)
    {
        StopVideoPreview();
    }

    #endregion

    #region 加载

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        HasLoadError = false;
        StopVideoPreview();

        try
        {
            var server = await GetServerAsync();
            var backgrounds = await _backgroundService.GetAvailableBackgroundsAsync(server);

            var currentUrl =
                (await _localSettingsService.ReadSettingAsync("SelectedOnlineBackgroundUrl"))?.ToString() ??
                string.Empty;
            var hasCustomBackground = await IsCustomBackgroundInEffectAsync();

            var previousSelectionUrl = SelectedItem?.Url;
            var items = BuildItems(backgrounds, currentUrl);

            Backgrounds.Clear();
            foreach (var item in items)
            {
                Backgrounds.Add(item);
            }

            SelectedItem = Backgrounds.FirstOrDefault(i => i.IsCurrent)
                           ?? Backgrounds.FirstOrDefault(i => i.Url == previousSelectionUrl)
                           ?? Backgrounds.FirstOrDefault();

            var serverName = server == ServerType.OS ? "ServerGlobal".GetLocalized() : "ServerCN".GetLocalized();
            SummaryText = string.Format("OfficialBgWindow_SummaryFormat".GetLocalized(), serverName, Backgrounds.Count);
            IsCustomBackgroundInEffect = hasCustomBackground;

            DebugLog.Info("OfficialBackgroundViewModel",
                $"[OfficialBgWindow] Loaded {Backgrounds.Count} official backgrounds ({serverName})");
        }
        catch (Exception ex)
        {
            DebugLog.Info("OfficialBackgroundViewModel",
                $"[OfficialBgWindow] Failed to load official backgrounds: {ex.Message}");
            HasLoadError = true;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ContentVisibility));
            OnPropertyChanged(nameof(EmptyVisibility));
        }
    }

    private async Task<ServerType> GetServerAsync()
    {
        try
        {
            var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
            return serverJson != null ? (ServerType)Convert.ToInt32(serverJson) : ServerType.CN;
        }
        catch
        {
            return ServerType.CN;
        }
    }

    private async Task<bool> IsCustomBackgroundInEffectAsync()
    {
        try
        {
            var enabledJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.IsBackgroundEnabledKey);
            var isCustomEnabled = enabledJson == null || Convert.ToBoolean(enabledJson);
            if (!isCustomEnabled)
            {
                return false;
            }

            var slideshowJson = await _localSettingsService.ReadSettingAsync("IsBackgroundSlideshowEnabled");
            if (slideshowJson != null && Convert.ToBoolean(slideshowJson))
            {
                var folder = (await _localSettingsService.ReadSettingAsync("BackgroundSlideshowFolder"))?.ToString();
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    return true;
                }
            }

            var customPath = (await _localSettingsService.ReadSettingAsync("CustomBackgroundPath"))?.ToString();
            return !string.IsNullOrEmpty(customPath) && File.Exists(customPath);
        }
        catch
        {
            return false;
        }
    }

    private static List<OfficialBackgroundItem> BuildItems(List<BackgroundUrlInfo> backgrounds, string currentUrl)
    {
        var videoPosterUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bg in backgrounds.Where(b => b.IsVideo && !string.IsNullOrEmpty(b.ThumbnailUrl)))
        {
            videoPosterUrls.Add(bg.ThumbnailUrl);
        }

        var items = new List<OfficialBackgroundItem>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bg in backgrounds)
        {
            if (string.IsNullOrEmpty(bg.Url) || !seenUrls.Add(bg.Url))
            {
                continue;
            }

            items.Add(new OfficialBackgroundItem
            {
                Url = bg.Url,
                IsVideo = bg.IsVideo,
                ThumbnailUrl = string.IsNullOrEmpty(bg.ThumbnailUrl) ? bg.Url : bg.ThumbnailUrl,
                IsVideoPoster = !bg.IsVideo && videoPosterUrls.Contains(bg.Url),
                IsCurrent = !string.IsNullOrEmpty(currentUrl) &&
                            string.Equals(bg.Url, currentUrl, StringComparison.OrdinalIgnoreCase)
            });
        }

        return items;
    }

    #endregion

    #region 保存

    [RelayCommand]
    private async Task SaveAsync(OfficialBackgroundItem? item)
    {
        item ??= SelectedItem;
        if (item == null || item.IsBusy || string.IsNullOrEmpty(item.Url))
        {
            return;
        }

        item.IsBusy = true;
        try
        {
            var isVideo = item.IsVideo;
            var extension = GuessExtension(item.Url, isVideo);
            var filters = BuildSaveFilters(extension, isVideo);
            var defaultName =
                $"FufuLauncher_{(isVideo ? "BackgroundVideo" : "BackgroundImage")}_{DateTime.Now:yyyyMMddHHmmss}{extension}";
            var startLocation = isVideo ? PickerLocationId.VideosLibrary : PickerLocationId.PicturesLibrary;

            var path = await FilePickerService.PickSaveFileAsync(
                _ownerWindow, filters, defaultName, startLocation,
                message => ShowStatus(message, InfoBarSeverity.Error));
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            using var response = await _httpClient.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = new FileStream(path, FileMode.Create, FileAccess.Write);
            await source.CopyToAsync(target);

            ShowStatus(string.Format("OfficialBgWindow_SaveSuccessFormat".GetLocalized(), Path.GetFileName(path)),
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            DebugLog.Info("OfficialBackgroundViewModel", $"[OfficialBgWindow] Failed to save background: {ex.Message}");
            ShowStatus(string.Format("OfficialBgWindow_SaveFailedFormat".GetLocalized(), ex.Message),
                InfoBarSeverity.Error);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyToHomepageAsync(OfficialBackgroundItem? item)
    {
        item ??= SelectedItem;
        if (item is not { IsVideo: false } || string.IsNullOrEmpty(item.Url))
        {
            return;
        }

        try
        {
            await _localSettingsService.SaveSettingAsync("IsRandomBackgroundEnabled", false);
            await _localSettingsService.SaveSettingAsync("IsBingBackgroundEnabled", false);
            await _localSettingsService.SaveSettingAsync("IsBackgroundSlideshowEnabled", false);
            await _localSettingsService.SaveSettingAsync(LocalSettingsService.IsBackgroundEnabledKey, false);
            RandomBackgroundService.ClearCache();
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", item.Url);
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);

            foreach (var background in Backgrounds)
            {
                background.IsCurrent = ReferenceEquals(background, item);
            }

            OnPropertyChanged(nameof(CurrentBadgeVisibility));
            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
            ShowStatus("OfficialBgWindow_ApplySuccess".GetLocalized(), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            DebugLog.Info("OfficialBackgroundViewModel",
                $"[OfficialBgWindow] Failed to set launcher background: {ex.Message}");
            ShowStatus(string.Format("OfficialBgWindow_SaveFailedFormat".GetLocalized(), ex.Message),
                InfoBarSeverity.Error);
        }
    }

    private static string GuessExtension(string url, bool isVideo)
    {
        var extension = GetUrlExtension(url);
        if (extension != null && (isVideo ? _videoExtensions : _imageExtensions).Contains(extension))
        {
            return extension;
        }

        return isVideo ? ".mp4" : ".png";
    }

    private static string? GetUrlExtension(string url)
    {
        try
        {
            var extension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
            return string.IsNullOrEmpty(extension) ? null : extension;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<(string Label, string[] Extensions)> BuildSaveFilters(string extension, bool isVideo)
    {
        var extensions = new List<string> { extension };
        foreach (var candidate in (isVideo ? _videoExtensions : _imageExtensions).Take(3))
        {
            if (!extensions.Contains(candidate))
            {
                extensions.Add(candidate);
            }
        }

        var label = isVideo
            ? "OfficialBgWindow_VideoFilter".GetLocalized()
            : "OfficialBgWindow_ImageFilter".GetLocalized();
        return new[] { (label, extensions.ToArray()) };
    }

    #endregion

    #region 预览

    [RelayCommand]
    private async Task ToggleVideoPreviewAsync()
    {
        if (SelectedItem is not { IsVideo: true } item)
        {
            return;
        }

        if (IsPreviewPlaying)
        {
            StopVideoPreview();
            return;
        }

        IsPreviewLoading = true;
        try
        {
            var source = await GetOrCreatePreviewSourceAsync(item.Url);
            if (source == null)
            {
                ShowStatus("OfficialBgWindow_PreviewFailed".GetLocalized(), InfoBarSeverity.Warning);
                return;
            }

            if (PreviewPlayer == null)
            {
                var player = MediaPlayerHelper.CreateLoopingMutedPlayer();
                player.MediaFailed += OnPreviewMediaFailed;
                PreviewPlayer = player;
            }

            PreviewPlayer.Source = source;
            PreviewPlayer.Play();
            IsPreviewPlaying = true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("OfficialBackgroundViewModel", $"[OfficialBgWindow] Video preview failed: {ex.Message}");
            StopVideoPreview();
            ShowStatus("OfficialBgWindow_PreviewFailed".GetLocalized(), InfoBarSeverity.Warning);
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    private async Task<MediaSource?> GetOrCreatePreviewSourceAsync(string url)
    {
        if (_previewSource != null && string.Equals(_previewSourceUrl, url, StringComparison.Ordinal))
        {
            return _previewSource;
        }

        ReleasePreviewSource();

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length == 0)
        {
            return null;
        }

        var stream = new InMemoryRandomAccessStream();
        try
        {
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var mimeType = GetVideoMimeType(url, response.Content.Headers.ContentType?.MediaType);
            var source = MediaSource.CreateFromStream(stream, mimeType);

            _previewStream = stream;
            _previewSource = source;
            _previewSourceUrl = url;
            return source;
        }
        catch
        {
            try
            {
                stream.Dispose();
            }
            catch
            {
            }

            throw;
        }
    }

    private static string GetVideoMimeType(string url, string? contentType)
    {
        if (!string.IsNullOrEmpty(contentType) && contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return contentType;
        }

        return GetUrlExtension(url) switch
        {
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            _ => "video/mp4"
        };
    }

    private void OnPreviewMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DebugLog.Info("OfficialBackgroundViewModel",
            $"[OfficialBgWindow] Video preview playback failed: {args.Error} - {args.ErrorMessage}");
        _dispatcherQueue?.TryEnqueue(() =>
        {
            StopVideoPreview();
            ShowStatus("OfficialBgWindow_PreviewFailed".GetLocalized(), InfoBarSeverity.Warning);
        });
    }

    public void StopVideoPreview()
    {
        var player = PreviewPlayer;
        if (player != null)
        {
            try
            {
                player.Pause();
                player.Source = null;
            }
            catch (Exception ex)
            {
                DebugLog.Info("OfficialBackgroundViewModel",
                    $"[OfficialBgWindow] Failed to stop preview: {ex.Message}");
            }
        }

        IsPreviewPlaying = false;
    }

    private void ReleasePreviewSource()
    {
        _previewSource = null;
        _previewSourceUrl = null;
        try
        {
            _previewStream?.Dispose();
        }
        catch
        {
        }

        _previewStream = null;
    }

    public void Cleanup()
    {
        try
        {
            StopVideoPreview();

            var player = PreviewPlayer;
            PreviewPlayer = null;
            if (player != null)
            {
                player.MediaFailed -= OnPreviewMediaFailed;
                player.Dispose();
            }

            ReleasePreviewSource();
            _statusTimer?.Stop();
        }
        catch (Exception ex)
        {
            DebugLog.Info("OfficialBackgroundViewModel",
                $"[OfficialBgWindow] Failed to release resources: {ex.Message}");
        }
    }

    #endregion

    #region 提示条

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        IsStatusOpen = true;

        try
        {
            _statusTimer ??= CreateStatusTimer();
            if (_statusTimer == null)
            {
                return;
            }

            _statusTimer.Stop();
            _statusTimer.Interval = TimeSpan.FromSeconds(severity == InfoBarSeverity.Error ? 10 : 4);
            _statusTimer.Start();
        }
        catch (Exception ex)
        {
            DebugLog.Info("OfficialBackgroundViewModel", $"[OfficialBgWindow] Toast timer failed: {ex.Message}");
        }
    }

    private DispatcherQueueTimer? CreateStatusTimer()
    {
        var queue = _dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (queue == null)
        {
            return null;
        }

        var timer = queue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => IsStatusOpen = false;
        return timer;
    }

    #endregion
}