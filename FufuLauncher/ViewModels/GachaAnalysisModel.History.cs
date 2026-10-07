/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class GachaAnalysisModel
{
    private bool _historyDirty = true;
    private int _historyPageIndex;
    private List<GachaHistoryPoolDisplayItem> _allHistoryPools = [];
    private List<List<GachaHistoryPoolDisplayItem>> _historyPages = [];

    [ObservableProperty] private ObservableCollection<GachaHistoryPoolDisplayItem> _historyPools = new();
    [ObservableProperty] private GachaHistoryPoolDisplayItem? _selectedHistoryPool;
    [ObservableProperty] private bool _isHistoryLoading;
    [ObservableProperty] private bool _isHistoryReady;
    [ObservableProperty] private bool _hideEmptyHistoryPools;
    [ObservableProperty] private bool _hasNewerHistoryPage;
    [ObservableProperty] private bool _hasOlderHistoryPage;
    [ObservableProperty] private string _historyPageSummary = string.Empty;

    public bool ShowHistoryLoading => IsHistorySelected && IsHistoryLoading;
    public bool ShowHistoryContent => IsHistorySelected && IsHistoryReady && SelectedHistoryPool != null;
    public bool ShowHistoryEmpty => IsHistorySelected && IsHistoryReady && HistoryPools.Count == 0;
    public string HistoryEmptyTitle => HideEmptyHistoryPools
        ? "GachaHistory_EmptyFilteredTitle".GetLocalized()
        : "GachaHistory_EmptyTitle".GetLocalized();
    public string HistoryEmptyDescription => HideEmptyHistoryPools
        ? "GachaHistory_EmptyFilteredDescription".GetLocalized()
        : "GachaHistory_EmptyDescription".GetLocalized();

    public async Task ShowHistoryAsync()
    {
        IsHistorySelected = true;
        IsOverviewSelected = false;
        await EnsureHistoryAsync();
    }

    private async Task EnsureHistoryAsync()
    {
        if (IsHistoryLoading) return;
        if (IsHistoryReady && !_historyDirty) return;

        var version = _refreshVersion;
        var characterLogs = _cachedCharacterLogs.ToList();
        var weaponLogs = _cachedWeaponLogs.ToList();
        var chronicledLogs = _cachedChronicledLogs.ToList();
        var itemMetadata = _savedMetadata.ToList();
        var selectedKey = SelectedHistoryPool?.Key;
        var selectedVersion = SelectedHistoryPool?.Version;

        IsHistoryLoading = true;
        IsHistoryReady = false;

        try
        {
            var pools = await Task.Run(() => BuildHistoryPools(
                characterLogs,
                weaponLogs,
                chronicledLogs,
                itemMetadata));

            if (_refreshVersion != version) return;

            _allHistoryPools = pools;
            ConfigureHistoryPages(selectedKey, selectedVersion);
            _historyDirty = false;
            IsHistoryReady = true;
        }
        finally
        {
            IsHistoryLoading = false;
        }
    }

    private void ConfigureHistoryPages(
        string? selectedKey,
        string? selectedVersion)
    {
        var visiblePools = HideEmptyHistoryPools
            ? _allHistoryPools.Where(pool => pool.HasObtainedItems)
            : _allHistoryPools;
        _historyPages = visiblePools
            .GroupBy(pool => pool.Version, StringComparer.Ordinal)
            .Select(group => group.ToList())
            .ToList();

        var selectedPageIndex = 0;
        if (!string.IsNullOrWhiteSpace(selectedKey))
        {
            var matchingPageIndex = _historyPages.FindIndex(page =>
                page.Any(pool => pool.Key == selectedKey));
            if (matchingPageIndex >= 0) selectedPageIndex = matchingPageIndex;
            else if (!string.IsNullOrWhiteSpace(selectedVersion))
            {
                var matchingVersionIndex = _historyPages.FindIndex(page =>
                    page.Any(pool => pool.Version == selectedVersion));
                if (matchingVersionIndex >= 0) selectedPageIndex = matchingVersionIndex;
            }
        }

        ShowHistoryPage(selectedPageIndex, selectedKey);
    }

    private void ShowHistoryPage(int pageIndex, string? selectedKey = null)
    {
        if (_historyPages.Count == 0)
        {
            _historyPageIndex = 0;
            HistoryPools = new ObservableCollection<GachaHistoryPoolDisplayItem>();
            SelectedHistoryPool = null;
            HasNewerHistoryPage = false;
            HasOlderHistoryPage = false;
            HistoryPageSummary = string.Empty;
            return;
        }

        _historyPageIndex = Math.Clamp(pageIndex, 0, _historyPages.Count - 1);
        var page = _historyPages[_historyPageIndex];
        HistoryPools = new ObservableCollection<GachaHistoryPoolDisplayItem>(page);
        SelectedHistoryPool = page.FirstOrDefault(pool => pool.Key == selectedKey)
                              ?? page.FirstOrDefault();
        HasNewerHistoryPage = _historyPageIndex > 0;
        HasOlderHistoryPage = _historyPageIndex < _historyPages.Count - 1;
        HistoryPageSummary = $"{page[0].Version} · {_historyPageIndex + 1} / {_historyPages.Count}";
    }

    [RelayCommand]
    private void ShowNewerHistoryPage()
    {
        if (!HasNewerHistoryPage) return;
        ShowHistoryPage(_historyPageIndex - 1);
    }

    [RelayCommand]
    private void ShowOlderHistoryPage()
    {
        if (!HasOlderHistoryPage) return;
        ShowHistoryPage(_historyPageIndex + 1);
    }

    private List<GachaHistoryPoolDisplayItem> BuildHistoryPools(
        IReadOnlyList<GachaLogItem> characterLogs,
        IReadOnlyList<GachaLogItem> weaponLogs,
        IReadOnlyList<GachaLogItem> chronicledLogs,
        IReadOnlyList<ScrapedMetadata> itemMetadata)
    {
        var metadataById = itemMetadata
            .Where(item => !string.IsNullOrWhiteSpace(item.ItemId))
            .GroupBy(item => item.ItemId)
            .ToDictionary(group => group.Key, group => group.First());
        var metadataByName = itemMetadata
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var sources = new List<HistoryPoolSource>();

        foreach (var window in LoadCharacterPoolCandidates()
                     .GroupBy(candidate => (candidate.Pool.Start, candidate.Pool.End)))
        {
            foreach (var candidate in ResolveCharacterPoolTypes(window.ToList(), characterLogs))
            {
                sources.Add(new HistoryPoolSource(candidate.Pool, candidate.PoolType, characterLogs));
            }
        }

        sources.AddRange(LoadPoolMetadataFromDb("302")
            .Select(pool => new HistoryPoolSource(pool, "302", weaponLogs)));
        sources.AddRange(LoadPoolMetadataFromDb("500")
            .Select(pool => new HistoryPoolSource(pool, "500", chronicledLogs)));

        return sources
            .Select(source => TryCreateHistoryPool(source, metadataById, metadataByName))
            .Where(result => result != null)
            .Select(result => result!)
            .OrderByDescending(result => result.StartsAt)
            .ThenBy(result => GetHistoryPoolOrder(result.PoolType))
            .Select(result => result.Display)
            .ToList();
    }

    private static HistoryPoolBuildResult? TryCreateHistoryPool(
        HistoryPoolSource source,
        IReadOnlyDictionary<string, ScrapedMetadata> metadataById,
        IReadOnlyDictionary<string, ScrapedMetadata> metadataByName)
    {
        if (!DateTime.TryParse(source.Pool.Start, out var startsAt) ||
            !DateTime.TryParse(source.Pool.End, out var endsAt))
        {
            return null;
        }

        var pulls = source.Logs
            .Where(log => IsLogForPool(log, source.PoolType) &&
                          IsLogInPeriod(log, startsAt, endsAt))
            .ToList();
        var featuredItems = source.Pool.Items
            .Select(item =>
            {
                var metadata = FindItemMetadata(
                    item.ItemId > 0 ? item.ItemId.ToString() : string.Empty,
                    item.Name,
                    metadataById,
                    metadataByName);
                var rank = ResolveRank(item.RankType, metadata?.Rank);

                var pullCount = pulls.Count(log => IsLogForPoolItem(log, item));

                return new GachaHistoryItemDisplayItem
                {
                    Name = item.Name,
                    ImageUrl = ResolveImageUrl(metadata?.ImgSrc),
                    Rank = rank,
                    PullCount = pullCount
                };
            })
            .OrderByDescending(item => item.Rank)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();

        var obtainedItems = pulls
            .GroupBy(log => !string.IsNullOrWhiteSpace(log.ItemId)
                ? $"id:{log.ItemId}"
                : $"name:{log.Name}", StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var metadata = FindItemMetadata(
                    first.ItemId,
                    first.Name,
                    metadataById,
                    metadataByName);
                var rank = ResolveRank(first.RankType, metadata?.Rank);
                if (rank <= 0) rank = 3;

                return new GachaHistoryItemDisplayItem
                {
                    Name = string.IsNullOrWhiteSpace(first.Name)
                        ? metadata?.Name ?? first.ItemId
                        : first.Name,
                    ImageUrl = ResolveImageUrl(metadata?.ImgSrc),
                    Rank = rank,
                    PullCount = group.Count()
                };
            })
            .OrderByDescending(item => item.PullCount)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();

        var poolName = !string.IsNullOrWhiteSpace(source.Pool.PoolName)
            ? source.Pool.PoolName
            : BuildFallbackPoolName(source.PoolType, featuredItems);

        return new HistoryPoolBuildResult(
            source.PoolType,
            startsAt,
            new GachaHistoryPoolDisplayItem
            {
                Key = $"{source.PoolType}|{source.Pool.Version}|{startsAt:O}",
                Version = NormalizeHistoryVersion(source.Pool.Version),
                Name = poolName,
                Period = string.Format("GachaHistory_PeriodFormat".GetLocalized(), startsAt, endsAt),
                TotalPulls = string.Format("GachaHistory_TotalPullsFormat".GetLocalized(), pulls.Count),
                BannerImageUrl = ResolveImageUrl(source.Pool.BannerImageUrl),
                FeaturedItems = featuredItems,
                FiveStarItems = obtainedItems.Where(item => item.Rank == 5).ToList(),
                FourStarItems = obtainedItems.Where(item => item.Rank == 4).ToList(),
                ThreeStarItems = obtainedItems.Where(item => item.Rank <= 3).ToList()
            });
    }

    private static ScrapedMetadata? FindItemMetadata(
        string itemId,
        string itemName,
        IReadOnlyDictionary<string, ScrapedMetadata> metadataById,
        IReadOnlyDictionary<string, ScrapedMetadata> metadataByName)
    {
        if (!string.IsNullOrWhiteSpace(itemId) &&
            metadataById.TryGetValue(itemId, out var metadataByItemId))
        {
            return metadataByItemId;
        }

        if (!string.IsNullOrWhiteSpace(itemName) &&
            metadataByName.TryGetValue(itemName, out var metadataByItemName))
        {
            return metadataByItemName;
        }

        return null;
    }

    private static int ResolveRank(int rank, string? metadataRank)
    {
        if (rank > 0) return rank;
        return int.TryParse(metadataRank, out var parsedRank) ? parsedRank : 0;
    }

    private static int ResolveRank(string? rank, string? metadataRank)
    {
        if (int.TryParse(rank, out var parsedRank)) return parsedRank;
        return int.TryParse(metadataRank, out parsedRank) ? parsedRank : 0;
    }

    private static string ResolveImageUrl(string? imageUrl) =>
        string.IsNullOrWhiteSpace(imageUrl)
            ? "ms-appx:///Assets/StoreLogo.png"
            : imageUrl;

    private static string BuildFallbackPoolName(
        string poolType,
        IReadOnlyList<GachaHistoryItemDisplayItem> featuredItems)
    {
        var fiveStarNames = featuredItems
            .Where(item => item.Rank == 5)
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        if (fiveStarNames.Count > 0) return string.Join(" / ", fiveStarNames);

        return poolType switch
        {
            "301" or "400" => "GachaHistory_CharacterEvent".GetLocalized(),
            "302" => "GachaHistory_WeaponEvent".GetLocalized(),
            "500" => "GachaHistory_Chronicled".GetLocalized(),
            _ => "GachaHistory_Limited".GetLocalized()
        };
    }

    private static bool IsLogForPool(GachaLogItem log, string poolType) =>
        poolType switch
        {
            "301" => log.GachaType == "301",
            "400" => log.GachaType == "400",
            "302" => log.GachaType == "302",
            "500" => log.GachaType == "500",
            _ => false
        };

    private static List<CharacterPoolCandidate> ResolveCharacterPoolTypes(
        IReadOnlyList<CharacterPoolCandidate> window,
        IReadOnlyList<GachaLogItem> characterLogs)
    {
        var ordered = window
            .OrderBy(candidate => candidate.Pool.Version, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.PoolType, StringComparer.Ordinal)
            .Take(2)
            .ToList();

        if (ordered.Count == 0) return [];
        if (ordered.Count == 1) return [new CharacterPoolCandidate(ordered[0].Pool, "301")];

        var first = ordered[0].Pool;
        var second = ordered[1].Pool;
        var direct = CountPoolTypeEvidence(first, "301", characterLogs) +
                     CountPoolTypeEvidence(second, "400", characterLogs);
        var swapped = CountPoolTypeEvidence(first, "400", characterLogs) +
                      CountPoolTypeEvidence(second, "301", characterLogs);

        return swapped > direct
            ? [new CharacterPoolCandidate(first, "400"), new CharacterPoolCandidate(second, "301")]
            : [new CharacterPoolCandidate(first, "301"), new CharacterPoolCandidate(second, "400")];
    }

    private static int CountPoolTypeEvidence(
        GachaPoolMetadata pool,
        string poolType,
        IReadOnlyList<GachaLogItem> characterLogs)
    {
        if (!DateTime.TryParse(pool.Start, out var startsAt) ||
            !DateTime.TryParse(pool.End, out var endsAt))
        {
            return 0;
        }

        return characterLogs.Count(log =>
            log.GachaType == poolType &&
            log.RankType == "5" &&
            IsLogInPeriod(log, startsAt, endsAt) &&
            pool.Items.Any(item => IsLogForPoolItem(log, item)));
    }

    private static bool IsLogInPeriod(GachaLogItem log, DateTime startsAt, DateTime endsAt) =>
        DateTime.TryParse(log.Time, out var time) && time >= startsAt && time <= endsAt;

    private static string NormalizeHistoryVersion(string version)
    {
        var match = Regex.Match(version ?? string.Empty, @"\d+\.\d+");
        if (match.Success) return $"v{match.Value}";

        var normalized = Regex.Replace(version ?? string.Empty, @"-\d+$", string.Empty)
            .Replace("上半", string.Empty, StringComparison.Ordinal)
            .Replace("下半", string.Empty, StringComparison.Ordinal)
            .Replace("混池", string.Empty, StringComparison.Ordinal)
            .Trim();
        return normalized.StartsWith('v') ? normalized : $"v{normalized}";
    }

    private static int GetHistoryPoolOrder(string poolType) => poolType switch
    {
        "301" => 0,
        "400" => 1,
        "302" => 2,
        "500" => 3,
        _ => 4
    };

    private void InvalidateHistory()
    {
        _historyDirty = true;
        IsHistoryReady = false;
    }

    partial void OnIsHistoryLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowHistoryLoading));
        OnPropertyChanged(nameof(ShowHistoryContent));
        OnPropertyChanged(nameof(ShowHistoryEmpty));
    }

    partial void OnIsHistoryReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowHistoryLoading));
        OnPropertyChanged(nameof(ShowHistoryContent));
        OnPropertyChanged(nameof(ShowHistoryEmpty));
    }

    partial void OnSelectedHistoryPoolChanged(GachaHistoryPoolDisplayItem? value)
    {
        OnPropertyChanged(nameof(ShowHistoryContent));
    }

    partial void OnHistoryPoolsChanged(ObservableCollection<GachaHistoryPoolDisplayItem> value)
    {
        OnPropertyChanged(nameof(ShowHistoryEmpty));
    }

    partial void OnHideEmptyHistoryPoolsChanged(bool value)
    {
        var selectedKey = SelectedHistoryPool?.Key;
        var selectedVersion = SelectedHistoryPool?.Version;
        ConfigureHistoryPages(selectedKey, selectedVersion);
        OnPropertyChanged(nameof(ShowHistoryContent));
        OnPropertyChanged(nameof(ShowHistoryEmpty));
        OnPropertyChanged(nameof(HistoryEmptyTitle));
        OnPropertyChanged(nameof(HistoryEmptyDescription));
    }

    private sealed record HistoryPoolSource(
        GachaPoolMetadata Pool,
        string PoolType,
        IReadOnlyList<GachaLogItem> Logs);

    private sealed record HistoryPoolBuildResult(
        string PoolType,
        DateTime StartsAt,
        GachaHistoryPoolDisplayItem Display);
}
