/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class GachaAnalysisModel
{
    #region Display Collection Building

    private void ClearCollections()
    {
        CharacterFiveStars = new();
        CharacterFourStars = new();
        WeaponFiveStars = new();
        WeaponFourStars = new();
        ChronicledFiveStars = new();
        ChronicledFourStars = new();
        StandardFiveStars = new();
        StandardFourStars = new();
        InvalidateAnalysisDashboard();
        InvalidateHistory();
    }

    private ObservableCollection<GachaDisplayItem> BuildDisplayCollection(List<FiveStarRecord> records, string typeHint,
        List<GachaPoolMetadata> pools = null, string poolType = "")
    {
        var pityStatuses = new PityStatus[records.Count];
        bool wasPreviousLost = false;

        for (var i = records.Count - 1; i >= 0; i--)
        {
            var record = records[i];

            var logItem = new GachaLogItem
            {
                Name = record.Name,
                Time = record.Time,
                RankType = record.Rank.ToString(),
                ItemId = record.ItemId ?? ""
            };

            var pityStatus = pools != null
                ? DeterminePityStatus(logItem, pools, record.PityUsed, wasPreviousLost)
                : PityStatus.None;

            if (record.Rank == 5)
            {
                wasPreviousLost = (pityStatus == PityStatus.LostPity);
            }

            pityStatuses[i] = pityStatus;
        }

        var items = new GachaDisplayItem[records.Count];
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            items[i] = new GachaDisplayItem
            {
                Name = record.Name,
                Count = record.PityUsed,
                Time = record.Time,
                Rank = record.Rank,
                Type = typeHint,
                PoolType = poolType,
                ImageUrl = "ms-appx:///Assets/StoreLogo.png",
                PityStatus = pityStatuses[i]
            };
        }

        return new ObservableCollection<GachaDisplayItem>(items);
    }

    private PityStatus DeterminePityStatus(GachaLogItem item, List<GachaPoolMetadata> pools, int pityCount,
        bool wasPreviousLost)
    {
        if (pools == null || pools.Count == 0)
            return PityStatus.None;

        if (!DateTime.TryParse(item.Time, out var pullTime))
            return PityStatus.None;

        var matchedPools = pools.Where(p =>
        {
            if (!DateTime.TryParse(p.Start, out var startTime) ||
                !DateTime.TryParse(p.End, out var endTime))
                return false;
            return pullTime >= startTime && pullTime <= endTime;
        }).ToList();

        if (matchedPools.Count == 0)
            return PityStatus.None;

        if (matchedPools.All(p => p.Items == null || p.Items.Count == 0))
            return PityStatus.None;

        var isUpItem = matchedPools.Any(pool => pool.Items.Any(p => IsLogForPoolItem(item, p)));

        if (item.RankType == "5")
        {
            if (isUpItem)
            {
                return wasPreviousLost ? PityStatus.Guaranteed : PityStatus.SmallPity;
            }
            else
            {
                return PityStatus.LostPity;
            }
        }
        else if (item.RankType == "4" && isUpItem)
        {
            return PityStatus.Up;
        }

        return PityStatus.None;
    }

    #endregion
}
