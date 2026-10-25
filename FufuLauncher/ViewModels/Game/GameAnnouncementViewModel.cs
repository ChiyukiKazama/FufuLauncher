/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.GameAnnouncement;
using FufuLauncher.Services;
using FufuLauncher.Services.GameAnnouncement;

namespace FufuLauncher.ViewModels;

public sealed class GameAnnouncementTab : ObservableObject
{
    public string Header
    {
        get;
    }

    public IReadOnlyList<GameAnnouncement> AllItems
    {
        get;
    }

    public ObservableCollection<GameAnnouncement> Items
    {
        get;
    } = new();

    public GameAnnouncementTab(string header, IReadOnlyList<GameAnnouncement> allItems)
    {
        Header = header;
        AllItems = allItems;
    }
}

public partial class GameAnnouncementViewModel : ObservableObject
{
    private readonly IGameAnnouncementService _gameAnnouncementService;
    private readonly ILocalSettingsService _localSettingsService;

    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _searchDebounceCancellation;
    private bool _regionInitialized;
    private bool _initialized;

    public ObservableCollection<GameAnnouncementTab> Tabs
    {
        get;
    } = new();

    public ObservableCollection<AnnouncementRegionOption> Regions
    {
        get;
    } = new();

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private bool _hasError;

    [ObservableProperty] private bool _isEmpty;

    [ObservableProperty] private AnnouncementRegionOption? _selectedRegion;

    public GameAnnouncementViewModel(
        IGameAnnouncementService gameAnnouncementService,
        ILocalSettingsService localSettingsService)
    {
        _gameAnnouncementService = gameAnnouncementService;
        _localSettingsService = localSettingsService;

        foreach (AnnouncementRegion region in Enum.GetValues<AnnouncementRegion>())
        {
            Regions.Add(new AnnouncementRegionOption(region));
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        AnnouncementRegion region = await ResolveInitialRegionAsync();

        SelectedRegion = Regions.First(option => option.Value == region);
        _regionInitialized = true;
        _initialized = true;

        await LoadAsync(forceRefresh: false);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync(forceRefresh: true);
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        await LoadAsync(forceRefresh: false);
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceCancellation?.Cancel();
        CancellationTokenSource cts = new();
        _searchDebounceCancellation = cts;
        _ = ApplyFilterAfterDelayAsync(cts.Token);
    }

    partial void OnSelectedRegionChanged(AnnouncementRegionOption? value)
    {
        if (!_regionInitialized || value is null)
        {
            return;
        }

        _ = _localSettingsService.SaveSettingAsync(LocalSettingsService.AnnouncementRegionKey, value.Value.ToCode());
        _ = LoadAsync(forceRefresh: false);
    }

    private async Task ApplyFilterAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = SearchText?.Trim() ?? string.Empty;

        foreach (GameAnnouncementTab tab in Tabs)
        {
            tab.Items.Clear();

            foreach (GameAnnouncement item in tab.AllItems)
            {
                if (query.Length == 0
                    || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    tab.Items.Add(item);
                }
            }
        }
    }

    private async Task LoadAsync(bool forceRefresh)
    {
        _loadCancellation?.Cancel();
        CancellationTokenSource cts = new();
        _loadCancellation = cts;
        CancellationToken token = cts.Token;

        AnnouncementRegion region = SelectedRegion?.Value ?? AnnouncementRegion.CNGF01;

        IsLoading = true;
        HasError = false;

        try
        {
            AnnouncementWrapper? wrapper = await _gameAnnouncementService
                .GetAnnouncementsAsync(GetLanguageCode(), region, forceRefresh, token);

            token.ThrowIfCancellationRequested();

            if (wrapper is null)
            {
                HasError = true;
                return;
            }

            RebuildTabs(wrapper);
            ApplyFilter();
            IsEmpty = Tabs.All(tab => tab.AllItems.Count == 0);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameAnnouncementViewModel",
                $"[GameAnnouncementViewModel] Failed to load announcements: {ex.Message}");
            HasError = true;
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cts))
            {
                IsLoading = false;
            }
        }
    }

    private void RebuildTabs(AnnouncementWrapper wrapper)
    {
        Tabs.Clear();

        List<AnnouncementType> typeList = wrapper.TypeList ?? new List<AnnouncementType>();
        List<AnnouncementListWrapper> wrappers = wrapper.List ?? new List<AnnouncementListWrapper>();

        HashSet<int> consumedTypeIds = new();

        foreach (AnnouncementType type in typeList)
        {
            AnnouncementListWrapper? matched = wrappers.FirstOrDefault(item => item.TypeId == type.Id);
            if (matched?.List is null)
            {
                continue;
            }

            Tabs.Add(new GameAnnouncementTab(ResolveTabHeader(type, matched), matched.List));
            consumedTypeIds.Add(type.Id);
        }

        foreach (AnnouncementListWrapper item in wrappers)
        {
            if (consumedTypeIds.Contains(item.TypeId) || item.List is null)
            {
                continue;
            }

            Tabs.Add(new GameAnnouncementTab(item.TypeLabel, item.List));
        }
    }

    private static string ResolveTabHeader(AnnouncementType type, AnnouncementListWrapper wrapper)
    {
        if (!string.IsNullOrEmpty(type.Name))
        {
            return type.Name;
        }

        if (!string.IsNullOrEmpty(wrapper.TypeLabel))
        {
            return wrapper.TypeLabel;
        }

        return type.MI18NName;
    }

    private static string GetLanguageCode()
    {
        string? culture = ResourceExtensions.CurrentCulture;
        return string.IsNullOrEmpty(culture) ? "zh-cn" : culture.ToLowerInvariant();
    }

    private async Task<AnnouncementRegion> ResolveInitialRegionAsync()
    {
        try
        {
            string? saved = (await _localSettingsService
                .ReadSettingAsync(LocalSettingsService.AnnouncementRegionKey))?.ToString();

            if (!string.IsNullOrEmpty(saved) &&
                AnnouncementRegionExtensions.TryParse(saved, out AnnouncementRegion region))
            {
                return region;
            }

            var serverValue = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
            ServerType server = serverValue != null && Convert.ToInt32(serverValue) == 1
                ? ServerType.OS
                : ServerType.CN;

            return AnnouncementRegionExtensions.GetDefaultRegion(server);
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameAnnouncementViewModel",
                $"[GameAnnouncementViewModel] Failed to read announcement server settings: {ex.Message}");
            return AnnouncementRegion.CNGF01;
        }
    }
}