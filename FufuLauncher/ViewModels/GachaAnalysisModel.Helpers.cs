/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class GachaAnalysisModel
{
    #region Log Enrichment & Type Mapping

    private bool FillMissingFieldsFromMetadata(params List<GachaLogItem>[] logLists)
    {
        if (_savedMetadata.Count == 0) return false;
        var byName = new Dictionary<string, ScrapedMetadata>();
        var byItemId = new Dictionary<string, ScrapedMetadata>();
        foreach (var m in _savedMetadata)
        {
            if (!string.IsNullOrEmpty(m.Name) && !string.IsNullOrEmpty(m.ItemId))
                byName[m.Name] = m;
            if (!string.IsNullOrEmpty(m.ItemId))
                byItemId[m.ItemId] = m;
        }

        var filledItemId = 0;
        var filledName = 0;
        var changed = false;
        foreach (var logs in logLists)
        {
            foreach (var log in logs)
            {
                if (string.IsNullOrEmpty(log.ItemId) && !string.IsNullOrEmpty(log.Name)
                                                     && byName.TryGetValue(log.Name, out var byNameMeta))
                {
                    log.ItemId = byNameMeta.ItemId;
                    filledItemId++;
                    changed = true;
                }

                if (string.IsNullOrEmpty(log.Name) && !string.IsNullOrEmpty(log.ItemId)
                                                   && byItemId.TryGetValue(log.ItemId, out var byIdMeta))
                {
                    log.Name = byIdMeta.Name;
                    filledName++;
                    changed = true;
                }

                if (string.IsNullOrEmpty(log.RankType) && !string.IsNullOrEmpty(log.ItemId)
                                                       && byItemId.TryGetValue(log.ItemId, out var byIdRankMeta)
                                                       && !string.IsNullOrEmpty(byIdRankMeta.Rank))
                {
                    log.RankType = byIdRankMeta.Rank;
                    changed = true;
                }
            }
        }

        Debug.WriteLine(
            $"[Gacha] 通过缓存元数据补全记录：name→id 映射 {byName.Count} 条（补全 {filledItemId} 条）、id→name 映射 {byItemId.Count} 条（补全 {filledName} 条）");

        return changed;
    }

    private static long GetNewestLogId(List<GachaLogItem> logs)
    {
        if (logs == null || logs.Count == 0) return 0;
        long max = 0;
        foreach (var log in logs)
        {
            if (long.TryParse(log.Id, out var id) && id > max) max = id;
        }

        return max;
    }

    private static string GetNormalizedGachaType(string gachaType) => gachaType switch
    {
        "301" or "400" => "301",
        "302" => "302",
        "200" => "200",
        "100" => "100",
        "500" => "500",
        _ => "200"
    };

    private static string GameToUigfGachaType(string gameType) => gameType switch
    {
        "100" => "100",
        "200" => "200",
        "301" => "301",
        "302" => "302",
        "400" => "301",
        "500" => "500",
        _ => gameType
    };

    private static bool IsLogForPoolItem(GachaLogItem log, GachaPoolItem item) =>
        (item.ItemId > 0 && log.ItemId == item.ItemId.ToString()) ||
        (!string.IsNullOrWhiteSpace(item.Name) &&
         string.Equals(log.Name, item.Name, StringComparison.Ordinal));

    #endregion
}