/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class GachaAnalysisModel
{
    private bool _historyDirty = true;

    [ObservableProperty] private ObservableCollection<GachaHistoryPeriodDisplayItem> _historyPeriods = new();
    [ObservableProperty] private GachaHistoryPeriodDisplayItem? _selectedHistoryPeriod;
    [ObservableProperty] private bool _isHistoryLoading;
    [ObservableProperty] private bool _isHistoryReady;

    public bool ShowHistoryLoading => IsHistorySelected && IsHistoryLoading;
    public bool ShowHistoryContent => IsHistorySelected && IsHistoryReady && SelectedHistoryPeriod != null;
    public bool ShowHistoryEmpty => IsHistorySelected && IsHistoryReady && HistoryPeriods.Count == 0;

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
        var selectedKey = SelectedHistoryPeriod?.Key;

        IsHistoryLoading = true;
        IsHistoryReady = false;

        try
        {
            var periods = await Task.Run(() => BuildHistoryPeriods(
                characterLogs,
                weaponLogs,
                chronicledLogs,
                itemMetadata));

            if (_refreshVersion != version) return;

            HistoryPeriods = new ObservableCollection<GachaHistoryPeriodDisplayItem>(periods);
            SelectedHistoryPeriod = periods.FirstOrDefault(period => period.Key == selectedKey)
                                    ?? periods.FirstOrDefault();
            _historyDirty = false;
            IsHistoryReady = true;
        }
        finally
        {
            IsHistoryLoading = false;
        }
    }

    private List<GachaHistoryPeriodDisplayItem> BuildHistoryPeriods(
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

        var sources = LoadPoolMetadataFromDb("301")
            .Select(pool => new HistoryPoolSource(pool, "301", "角色活动", characterLogs))
            .Concat(LoadPoolMetadataFromDb("302")
                .Select(pool => new HistoryPoolSource(pool, "302", "武器活动", weaponLogs)))
            .Concat(LoadPoolMetadataFromDb("500")
                .Select(pool => new HistoryPoolSource(pool, "500", "集录祈愿", chronicledLogs)))
            .Select(source => TryCreateHistoryPool(source, metadataById, metadataByName))
            .Where(source => source != null)
            .Select(source => source!)
            .ToList();

        return sources
            .GroupBy(source => NormalizeHistoryVersion(source.Source.Pool.Version), StringComparer.Ordinal)
            .Select(group =>
            {
                var orderedPools = group
                    .OrderBy(source => GetHistoryPoolOrder(source.Source.PoolType))
                    .ThenBy(source => source.Source.Pool.Version, StringComparer.Ordinal)
                    .ToList();
                var startsAt = orderedPools.Min(source => source.StartsAt);
                var endsAt = orderedPools.Max(source => source.EndsAt);
                var totalPulls = orderedPools
                    .GroupBy(source => source.Source.PoolType, StringComparer.Ordinal)
                    .Sum(poolGroup => CountLogsInPeriod(
                        poolGroup.First().Source.Logs,
                        poolGroup.Min(source => source.StartsAt),
                        poolGroup.Max(source => source.EndsAt)));

                return new HistoryPeriodBuildResult(
                    startsAt,
                    new GachaHistoryPeriodDisplayItem
                    {
                        Key = $"{group.Key}|{startsAt:O}",
                        DisplayName = group.Key,
                        DateRange = $"{startsAt:yyyy-MM-dd} 至 {endsAt:yyyy-MM-dd}",
                        TotalPulls = $"共 {totalPulls} 抽",
                        Pools = orderedPools.Select(source => source.Display).ToList()
                    });
            })
            .OrderByDescending(result => result.StartsAt)
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
            .Where(log => IsLogInPeriod(log, startsAt, endsAt))
            .ToList();
        var featuredItems = source.Pool.Items
            .Select(item =>
            {
                metadataById.TryGetValue(item.ItemId.ToString(), out var metadata);
                if (metadata == null && !string.IsNullOrWhiteSpace(item.Name))
                {
                    metadataByName.TryGetValue(item.Name, out metadata);
                }

                var rank = item.RankType;
                if (rank <= 0 && !int.TryParse(metadata?.Rank, out rank))
                {
                    rank = 0;
                }

                var pullCount = pulls.Count(log =>
                    (item.ItemId > 0 && log.ItemId == item.ItemId.ToString()) ||
                    (!string.IsNullOrWhiteSpace(item.Name) &&
                     string.Equals(log.Name, item.Name, StringComparison.Ordinal)));

                return new GachaHistoryItemDisplayItem
                {
                    Name = item.Name,
                    ImageUrl = string.IsNullOrWhiteSpace(metadata?.ImgSrc)
                        ? "ms-appx:///Assets/StoreLogo.png"
                        : metadata.ImgSrc,
                    Rank = rank,
                    PullCount = pullCount
                };
            })
            .OrderByDescending(item => item.Rank)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();

        return new HistoryPoolBuildResult(
            source,
            startsAt,
            endsAt,
            new GachaHistoryPoolDisplayItem
            {
                Title = $"{source.PoolName} · {FormatPoolVersion(source.Pool.Version)}",
                Period = $"{startsAt:yyyy-MM-dd HH:mm} 至 {endsAt:yyyy-MM-dd HH:mm}",
                TotalPulls = $"当期 {pulls.Count} 抽",
                FeaturedItems = featuredItems
            });
    }

    private static int CountLogsInPeriod(
        IReadOnlyList<GachaLogItem> logs,
        DateTime startsAt,
        DateTime endsAt) => logs.Count(log => IsLogInPeriod(log, startsAt, endsAt));

    private static bool IsLogInPeriod(GachaLogItem log, DateTime startsAt, DateTime endsAt) =>
        DateTime.TryParse(log.Time, out var time) && time >= startsAt && time <= endsAt;

    private static string NormalizeHistoryVersion(string version)
    {
        var normalized = Regex.Replace(version ?? string.Empty, @"-\d+$", string.Empty)
            .Replace("上半", "上", StringComparison.Ordinal)
            .Replace("下半", "下", StringComparison.Ordinal)
            .Replace("中", "下", StringComparison.Ordinal)
            .Replace("混池", "集录", StringComparison.Ordinal)
            .Trim();
        return normalized.StartsWith('v') ? normalized : $"v{normalized}";
    }

    private static string FormatPoolVersion(string version) =>
        Regex.Replace(version ?? string.Empty, @"-(\d+)$", " · 卡池 $1");

    private static int GetHistoryPoolOrder(string poolType) => poolType switch
    {
        "301" => 0,
        "302" => 1,
        "500" => 2,
        _ => 3
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

    partial void OnSelectedHistoryPeriodChanged(GachaHistoryPeriodDisplayItem? value)
    {
        OnPropertyChanged(nameof(ShowHistoryContent));
    }

    partial void OnHistoryPeriodsChanged(ObservableCollection<GachaHistoryPeriodDisplayItem> value)
    {
        OnPropertyChanged(nameof(ShowHistoryEmpty));
    }

    private sealed record HistoryPoolSource(
        GachaPoolMetadata Pool,
        string PoolType,
        string PoolName,
        IReadOnlyList<GachaLogItem> Logs);

    private sealed record HistoryPoolBuildResult(
        HistoryPoolSource Source,
        DateTime StartsAt,
        DateTime EndsAt,
        GachaHistoryPoolDisplayItem Display);

    private sealed record HistoryPeriodBuildResult(
        DateTime StartsAt,
        GachaHistoryPeriodDisplayItem Display);
}
