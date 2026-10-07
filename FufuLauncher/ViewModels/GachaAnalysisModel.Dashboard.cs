/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class GachaAnalysisModel
{
    #region Analysis Dashboard

    public async Task ShowOverviewAsync()
    {
        IsHistorySelected = false;
        IsOverviewSelected = true;
        await Task.CompletedTask;
    }

    public async Task ShowAnalysisAsync()
    {
        IsHistorySelected = false;
        IsOverviewSelected = false;
        await EnsureAnalysisDashboardAsync();
    }

    private async Task EnsureAnalysisDashboardAsync()
    {
        if (IsAnalysisLoading) return;
        if (IsAnalysisReady && !_analysisDashboardDirty) return;

        var version = _refreshVersion;
        var charLogs = _cachedCharacterLogs.OrderBy(x => x.Id).ToList();
        var weaponLogs = _cachedWeaponLogs.OrderBy(x => x.Id).ToList();
        var chronicledLogs = _cachedChronicledLogs.OrderBy(x => x.Id).ToList();
        var noviceLogs = _cachedNoviceLogs.OrderBy(x => x.Id).ToList();
        var standardLogs = _cachedStandardLogs.OrderBy(x => x.Id).ToList();

        IsAnalysisLoading = true;
        IsAnalysisReady = false;
        AnalysisDashboard = GachaAnalysisDashboard.Empty();

        try
        {
            var dashboard = await Task.Run(() =>
                BuildAnalysisDashboard(charLogs, weaponLogs, chronicledLogs, noviceLogs, standardLogs));

            if (_refreshVersion == version)
            {
                AnalysisDashboard = dashboard;
                _analysisDashboardDirty = false;
                IsAnalysisReady = true;
            }
        }
        finally
        {
            IsAnalysisLoading = false;
        }
    }

    private void InvalidateAnalysisDashboard()
    {
        _analysisDashboardDirty = true;
        IsAnalysisReady = false;
        AnalysisDashboard = GachaAnalysisDashboard.Empty();
    }

    private GachaAnalysisDashboard BuildAnalysisDashboard(
        List<GachaLogItem> charLogs,
        List<GachaLogItem> weaponLogs,
        List<GachaLogItem> chronicledLogs,
        List<GachaLogItem> noviceLogs,
        List<GachaLogItem> standardLogs)
    {
        var allLogs = charLogs
            .Concat(weaponLogs)
            .Concat(chronicledLogs)
            .Concat(noviceLogs)
            .Concat(standardLogs)
            .OrderBy(x => x.Id)
            .ToList();

        if (allLogs.Count == 0) return GachaAnalysisDashboard.Empty();

        var totalCount = allLogs.Count;
        var fiveStarCount = allLogs.Count(x => x.RankType == "5");
        var fourStarCount = allLogs.Count(x => x.RankType == "4");
        var threeStarCount = totalCount - fiveStarCount - fourStarCount;
        var primogems = totalCount * 160;
        var fiveStarRate = FormatRate(fiveStarCount, totalCount);
        var fourStarRate = FormatRate(fourStarCount, totalCount);
        var averageCharacterPities =
            CalculateFiveStarCharacterPities(charLogs, weaponLogs, chronicledLogs, noviceLogs, standardLogs);
        var averageCharacterPulls = averageCharacterPities.Count == 0 ? 0 : averageCharacterPities.Average();
        var fiveStarTimeline = BuildFiveStarTimeline(charLogs, "角色活动")
            .Concat(BuildFiveStarTimeline(weaponLogs, "武器活动"))
            .Concat(BuildFiveStarTimeline(chronicledLogs, "集录祈愿"))
            .Concat(BuildFiveStarTimeline(noviceLogs, "新手祈愿"))
            .Concat(BuildFiveStarTimeline(standardLogs, "常驻祈愿"))
            .OrderBy(x => x.Time)
            .ToList();
        var averageFiveStarPulls = fiveStarTimeline.Count == 0 ? 0 : fiveStarTimeline.Average(x => x.Pity);
        var bestFiveStar = fiveStarTimeline.OrderBy(x => x.Pity).FirstOrDefault();
        var worstFiveStar = fiveStarTimeline.OrderByDescending(x => x.Pity).FirstOrDefault();

        var currentPities = new[]
        {
            ("角色活动", CalculateCurrentFiveStarPity(charLogs)),
            ("武器活动", CalculateCurrentFiveStarPity(weaponLogs)),
            ("集录祈愿", CalculateCurrentFiveStarPity(chronicledLogs)),
            ("新手祈愿", CalculateCurrentFiveStarPity(noviceLogs)),
            ("常驻祈愿", CalculateCurrentFiveStarPity(standardLogs))
        };
        var deepestPity = currentPities.OrderByDescending(x => x.Item2).First();

        var monthlyGroups = allLogs
            .Select(x => TryParseTime(x.Time, out var dt) ? dt : (DateTime?)null)
            .Where(x => x.HasValue)
            .Select(x => x.Value)
            .GroupBy(x => x.ToString("yyyy-MM"))
            .OrderBy(x => x.Key)
            .ToList();
        var activeMonthCount = monthlyGroups.Count;
        var monthlyAveragePulls = activeMonthCount == 0 ? 0 : monthlyGroups.Average(x => x.Count());
        var busiestMonth = monthlyGroups.OrderByDescending(x => x.Count()).ThenByDescending(x => x.Key)
            .FirstOrDefault();

        var groupedByTime = allLogs
            .Where(x => !string.IsNullOrWhiteSpace(x.Time))
            .GroupBy(x => $"{GetNormalizedGachaType(x.GachaType)}|{x.Time}")
            .ToList();
        var tenPullGroups = groupedByTime.Where(x => x.Count() >= 10).ToList();
        var singlePullGroups = groupedByTime.Where(x => x.Count() == 1).ToList();
        var tenPullGoldCount = tenPullGroups.Count(x => x.Any(i => i.RankType == "5"));
        var singlePullGoldCount = singlePullGroups.Count(x => x.Any(i => i.RankType == "5"));

        var dashboard = new GachaAnalysisDashboard
        {
            TenPullCount = tenPullGroups.Count,
            TenPullGoldCount = tenPullGoldCount,
            TenPullGoldRateText = FormatRate(tenPullGoldCount, tenPullGroups.Count),
            SinglePullCount = singlePullGroups.Count,
            SinglePullGoldCount = singlePullGoldCount,
            SinglePullGoldRateText = FormatRate(singlePullGoldCount, singlePullGroups.Count),
            AverageFiveStarCharacterPulls = averageCharacterPulls,
            AverageFiveStarCharacterPullsText =
                averageCharacterPulls <= 0 ? "0" : averageCharacterPulls.ToString("0.#"),
            AverageFiveStarCharacterPrimogems = (int)Math.Round(averageCharacterPulls * 160),
            AverageFiveStarCharacterPrimogemsText = averageCharacterPulls <= 0
                ? "0"
                : ((int)Math.Round(averageCharacterPulls * 160)).ToString("N0"),
            AverageFiveStarPullsText = averageFiveStarPulls <= 0 ? "0" : averageFiveStarPulls.ToString("0.#"),
            CurrentDeepestPityText = $"{deepestPity.Item2} 抽",
            CurrentDeepestPityHint = deepestPity.Item2 <= 0 ? "暂无五星垫数" : $"当前最深：{deepestPity.Item1}",
            BestFiveStarPityText = fiveStarTimeline.Count == 0 ? "0 抽" : $"{bestFiveStar.Pity} 抽",
            BestFiveStarPityHint =
                fiveStarTimeline.Count == 0 ? "暂无五星记录" : $"{bestFiveStar.PoolName} · {bestFiveStar.Name}",
            WorstFiveStarPityText = fiveStarTimeline.Count == 0 ? "0 抽" : $"{worstFiveStar.Pity} 抽",
            WorstFiveStarPityHint = fiveStarTimeline.Count == 0
                ? "暂无五星记录"
                : $"{worstFiveStar.PoolName} · {worstFiveStar.Name}",
            ActiveMonthCountText = activeMonthCount.ToString(),
            MonthlyAveragePullsText = monthlyAveragePulls <= 0 ? "0" : monthlyAveragePulls.ToString("0.#"),
            BusiestMonthText = busiestMonth == null ? "暂无" : busiestMonth.Key,
            BusiestMonthPullsText = busiestMonth == null ? "0 抽" : $"{busiestMonth.Count()} 抽",
            DateRangeText = BuildDateRangeText(allLogs)
        };

        dashboard.KpiItems = new ObservableCollection<GachaKpiItem>
        {
            new GachaKpiItem
                { Glyph = "\uE8EF", Label = "总抽数", Value = totalCount.ToString(), Hint = $"约 {primogems:N0} 原石" },
            new GachaKpiItem
                { Glyph = "\uE8C7", Label = "原石估算", Value = primogems.ToString("N0"), Hint = "按每抽 160 原石" },
            new GachaKpiItem
                { Glyph = "\uE735", Label = "五星出货", Value = fiveStarCount.ToString(), Hint = fiveStarRate },
            new GachaKpiItem
                { Glyph = "\uE734", Label = "四星出货", Value = fourStarCount.ToString(), Hint = fourStarRate },
            new GachaKpiItem
            {
                Glyph = "\uE7C1", Label = "五星角色均耗", Value = $"{dashboard.AverageFiveStarCharacterPullsText} 抽",
                Hint = $"约 {dashboard.AverageFiveStarCharacterPrimogemsText} 原石"
            },
            new GachaKpiItem
            {
                Glyph = "\uE7C1", Label = "五星均抽", Value = $"{dashboard.AverageFiveStarPullsText} 抽", Hint = "全部卡池五星"
            },
            new GachaKpiItem
            {
                Glyph = "\uE8A5", Label = "当前最深垫数", Value = dashboard.CurrentDeepestPityText,
                Hint = dashboard.CurrentDeepestPityHint
            },
            new GachaKpiItem
            {
                Glyph = "\uE74C", Label = "最欧五星", Value = dashboard.BestFiveStarPityText,
                Hint = dashboard.BestFiveStarPityHint
            },
            new GachaKpiItem
            {
                Glyph = "\uE7BA", Label = "最非五星", Value = dashboard.WorstFiveStarPityText,
                Hint = dashboard.WorstFiveStarPityHint
            },
            new GachaKpiItem
            {
                Glyph = "\uE787", Label = "活跃月份", Value = dashboard.ActiveMonthCountText,
                Hint = $"月均 {dashboard.MonthlyAveragePullsText} 抽"
            }
        };

        dashboard.PoolDistribution = BuildSharePoints(new[]
        {
            ("角色活动", (double)charLogs.Count, $"{charLogs.Count} 抽"),
            ("武器活动", (double)weaponLogs.Count, $"{weaponLogs.Count} 抽"),
            ("集录祈愿", (double)chronicledLogs.Count, $"{chronicledLogs.Count} 抽"),
            ("新手祈愿", (double)noviceLogs.Count, $"{noviceLogs.Count} 抽"),
            ("常驻祈愿", (double)standardLogs.Count, $"{standardLogs.Count} 抽")
        });
        dashboard.PoolPieSlices = BuildPieSlices(dashboard.PoolDistribution);

        dashboard.RarityDistribution = BuildSharePoints(new[]
        {
            ("五星", (double)fiveStarCount, $"{fiveStarCount} 个"),
            ("四星", (double)fourStarCount, $"{fourStarCount} 个"),
            ("三星", (double)threeStarCount, $"{threeStarCount} 个")
        });
        dashboard.RarityPieSlices = BuildPieSlices(dashboard.RarityDistribution);

        dashboard.RecentFiveStarPities = BuildRelativePoints(
            fiveStarTimeline
                .TakeLast(12)
                .Select(x => (x.Name, (double)x.Pity, $"{x.Pity} 抽", $"{x.PoolName} · {FormatShortDate(x.TimeText)}")));

        dashboard.FourStarTopItems = BuildRelativePoints(
            allLogs
                .Where(x => x.RankType == "4")
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Name) ? "未知四星" : x.Name)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key)
                .Take(10)
                .Select(x => (x.Key, (double)x.Count(), $"{x.Count()} 次", "四星出货")));

        dashboard.PityBuckets = BuildRelativePoints(
            fiveStarTimeline
                .GroupBy(x => GetPityBucket(x.Pity))
                .Select(x => (x.Key.Label, (double)x.Count(), $"{x.Count()} 次", x.Key.Hint))
                .OrderBy(x => GetPityBucketOrder(x.Item1)));

        dashboard.MonthlyPulls = BuildRelativePoints(
            monthlyGroups
                .Select(x => (x.Key, (double)x.Count(), $"{x.Count()} 抽", "1-5星全部记录")));

        return dashboard;
    }

    private const double AnalysisColumnMaxHeight = 132;

    private static int CalculateCurrentFiveStarPity(List<GachaLogItem> logs)
    {
        var pity = 0;
        foreach (var item in logs.OrderBy(x => x.Id))
        {
            pity++;
            if (item.RankType == "5")
                pity = 0;
        }

        return pity;
    }

    private static ObservableCollection<GachaChartPoint> BuildSharePoints(
        IEnumerable<(string Label, double Value, string Display)> values)
    {
        var list = values.ToList();
        var total = list.Sum(x => x.Value);
        return new ObservableCollection<GachaChartPoint>(list.Select((x, index) =>
        {
            var percentage = total <= 0 ? 0 : x.Value * 100 / total;
            return new GachaChartPoint
            {
                Label = x.Label,
                Value = x.Value,
                Percentage = percentage,
                BarWidth = ToColumnWidth(percentage),
                BarHeight = ToColumnHeight(percentage),
                DisplayValue = total <= 0 ? x.Display : $"{x.Display} · {percentage:0.#}%",
                ColorIndex = index
            };
        }));
    }

    private static ObservableCollection<GachaChartPoint> BuildRelativePoints(
        IEnumerable<(string Label, double Value, string Display, string SubLabel)> values)
    {
        var list = values.ToList();
        var max = list.Count == 0 ? 0 : list.Max(x => x.Value);
        return new ObservableCollection<GachaChartPoint>(list.Select((x, index) =>
        {
            var percentage = max <= 0 ? 0 : Math.Max(4, x.Value * 100 / max);
            return new GachaChartPoint
            {
                Label = x.Label,
                SubLabel = x.SubLabel,
                Value = x.Value,
                Percentage = percentage,
                BarWidth = ToColumnWidth(percentage),
                BarHeight = ToColumnHeight(percentage),
                DisplayValue = x.Display,
                ColorIndex = index
            };
        }));
    }

    private static double ToColumnHeight(double percentage)
    {
        if (percentage <= 0) return 0;
        return Math.Max(8, percentage / 100 * AnalysisColumnMaxHeight);
    }

    private static double ToColumnWidth(double percentage)
    {
        if (percentage <= 0) return 14;
        return Math.Clamp(14 + percentage * 0.1, 16, 24);
    }

    private static ObservableCollection<GachaPieSlice> BuildPieSlices(IEnumerable<GachaChartPoint> points)
    {
        var slices = new ObservableCollection<GachaPieSlice>();
        var visiblePoints = points.Where(x => x.Value > 0).ToList();
        var total = visiblePoints.Sum(x => x.Value);
        if (total <= 0) return slices;

        double startAngle = -90;
        for (var i = 0; i < visiblePoints.Count; i++)
        {
            var point = visiblePoints[i];
            var sweepAngle = point.Value / total * 360;
            if (i == visiblePoints.Count - 1)
                sweepAngle = 270 - startAngle;

            slices.Add(new GachaPieSlice
            {
                Label = point.Label,
                DisplayValue = point.DisplayValue,
                Percentage = point.Percentage,
                StartAngle = startAngle,
                SweepAngle = sweepAngle,
                ColorIndex = point.ColorIndex
            });

            startAngle += sweepAngle;
        }

        return slices;
    }

    private static List<double> CalculateFiveStarCharacterPities(params List<GachaLogItem>[] logLists)
    {
        var pities = new List<double>();
        foreach (var logs in logLists)
        {
            var pity = 0;
            foreach (var item in logs.OrderBy(x => x.Id))
            {
                pity++;
                if (item.RankType != "5") continue;

                if (item.ItemType?.Contains("角色") == true
                    || GetNormalizedGachaType(item.GachaType) is "100" or "301")
                    pities.Add(pity);

                pity = 0;
            }
        }

        return pities;
    }

    private static List<(string Name, string PoolName, int Pity, DateTime Time, string TimeText)> BuildFiveStarTimeline(
        List<GachaLogItem> logs, string poolName)
    {
        var result = new List<(string Name, string PoolName, int Pity, DateTime Time, string TimeText)>();
        var pity = 0;
        foreach (var item in logs.OrderBy(x => x.Id))
        {
            pity++;
            if (item.RankType != "5") continue;

            result.Add((
                string.IsNullOrWhiteSpace(item.Name) ? "未知五星" : item.Name,
                poolName,
                pity,
                TryParseTime(item.Time, out var time) ? time : DateTime.MinValue,
                item.Time ?? ""));
            pity = 0;
        }

        return result;
    }

    private static string BuildDateRangeText(List<GachaLogItem> logs)
    {
        var dates = logs
            .Select(x => TryParseTime(x.Time, out var dt) ? dt : (DateTime?)null)
            .Where(x => x.HasValue)
            .Select(x => x.Value)
            .OrderBy(x => x)
            .ToList();

        if (dates.Count == 0) return "暂无时间记录";
        return $"{dates.First():yyyy.MM.dd} - {dates.Last():yyyy.MM.dd}";
    }

    private static string FormatRate(int hit, int total) => total <= 0 ? "0%" : $"{hit * 100d / total:0.##}%";

    private static bool TryParseTime(string time, out DateTime dateTime) => DateTime.TryParse(time, out dateTime);

    private static string FormatShortDate(string time) => TryParseTime(time, out var dt) ? dt.ToString("MM.dd") : "未知";

    private static (string Label, string Hint) GetPityBucket(int pity) => pity switch
    {
        <= 30 => ("1-30", "欧气区间"),
        <= 60 => ("31-60", "中段出货"),
        <= 75 => ("61-75", "接近软保底"),
        <= 90 => ("76-90", "保底区间"),
        _ => ("90+", "异常记录")
    };

    private static int GetPityBucketOrder(string label) => label switch
    {
        "1-30" => 0,
        "31-60" => 1,
        "61-75" => 2,
        "76-90" => 3,
        _ => 4
    };

    #endregion
}
