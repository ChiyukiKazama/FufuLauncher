/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Data.Entities;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class GachaAnalysisModel
{
    #region Pool Metadata

    private async Task FetchGachaPoolMetadataAsync(bool deferRefresh = false)
    {
        if (_isFetchingPoolMetadata) return;
        _isFetchingPoolMetadata = true;

        try
        {
            if (_savedMetadata.Count == 0)
            {
                Debug.WriteLine("[Gacha] 跳过卡池元数据拉取：物品元数据尚未就绪");
                return;
            }

            CrawlerStatus = "正在获取卡池元数据...";

            _charNameToIdMap = BuildNameToIdMap("char");
            _weaponNameToIdMap = BuildNameToIdMap("weapon");
            Debug.WriteLine($"[Gacha] 卡池数据 name→id 映射：角色 {_charNameToIdMap.Count} 个、武器 {_weaponNameToIdMap.Count} 个");

            var response = await _httpClient.GetStringAsync(ApiEndpoints.WishHistoryUrl);
            var data = JsonSerializer.Deserialize<WishHistoryResponse>(response);
            if (data?.Result == null || data.Weapon == null) return;

            var allPools = new List<(GachaPoolMetadata pool, string poolType)>();

            var charPoolsRaw = AssignVersionSuffixes(data.Result);
            foreach (var (item, displayVersion) in charPoolsRaw)
            {
                var period = GetVersionPeriod(item.Version);
                var poolType = period == "混池" ? "500" : "301";
                var (startTime, endTime) = ParseTimeRange(item.Time, period);

                allPools.Add((new GachaPoolMetadata
                {
                    Version = displayVersion,
                    Start = startTime,
                    End = endTime,
                    Items = ConvertNamesToItems(item.Star5Role, item.Star4Role, _charNameToIdMap, data.AvatarList)
                }, poolType));
            }

            var weaponPoolsRaw = AssignVersionSuffixes(data.Weapon);
            foreach (var (item, displayVersion) in weaponPoolsRaw)
            {
                var period = GetVersionPeriod(item.Version);
                var poolType = period == "混池" ? "500" : "302";
                var (startTime, endTime) = ParseTimeRange(item.Time, period);

                allPools.Add((new GachaPoolMetadata
                {
                    Version = displayVersion,
                    Start = startTime,
                    End = endTime,
                    Items = ConvertNamesToItems(item.Star5Role, item.Star4Role, _weaponNameToIdMap, data.AvatarList)
                }, poolType));
            }

            var grouped = allPools.GroupBy(p => p.poolType);
            foreach (var group in grouped)
            {
                await SavePoolMetadataToDbAsync(group.Select(g => g.pool).ToList(), group.Key);
            }

            var count301 = allPools.Count(p => p.poolType == "301");
            var count302 = allPools.Count(p => p.poolType == "302");
            var count500 = allPools.Count(p => p.poolType == "500");
            CrawlerStatus = $"卡池元数据更新完成（共 {allPools.Count} 个历史卡池：角色 {count301}、武器 {count302}、集录 {count500}）";

            if (!deferRefresh &&
                _cachedCharacterLogs.Count + _cachedWeaponLogs.Count + _cachedChronicledLogs.Count > 0)
            {
                App.MainWindow.DispatcherQueue.TryEnqueue(() => RefreshUIFromCache());
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gacha] 获取卡池元数据失败: {ex.Message}");
        }
        finally
        {
            _isFetchingPoolMetadata = false;
        }
    }

    private Dictionary<string, int> BuildNameToIdMap(string type)
    {
        var map = new Dictionary<string, int>();
        foreach (var m in _savedMetadata)
        {
            if (m.Type != type) continue;
            if (string.IsNullOrEmpty(m.Name) || string.IsNullOrEmpty(m.ItemId)) continue;
            if (!map.ContainsKey(m.Name) && int.TryParse(m.ItemId, out var id))
                map[m.Name] = id;
        }

        return map;
    }

    private static string GetVersionPeriod(string fullVersion)
    {
        if (fullVersion.Contains("混池")) return "混池";
        if (fullVersion.Contains("上半")) return "上半";
        if (fullVersion.Contains("下半")) return "下半";
        if (fullVersion.Contains("中")) return "下半";
        return "";
    }

    private static List<(WishBannerItem Item, string DisplayVersion)> AssignVersionSuffixes(List<WishBannerItem> items)
    {
        var result = new List<(WishBannerItem, string)>();
        var versionCounts = new Dictionary<string, int>();

        foreach (var item in items)
        {
            versionCounts.TryGetValue(item.Version, out var count);
            count++;
            versionCounts[item.Version] = count;

            var displayVersion = count > 1 ? $"{item.Version}-{count}" : item.Version;
            result.Add((item, displayVersion));
        }

        return result;
    }

    private static (string startTime, string endTime) ParseTimeRange(string timeRange, string period)
    {
        var parts = timeRange.Split('-');
        var startDate = parts[0].Trim();
        var endDate = parts[1].Trim();

        if (period == "上半")
            return ($"{startDate} 07:00:00", $"{endDate} 17:59:59");

        return ($"{startDate} 18:00:00", $"{endDate} 14:59:59");
    }

    private static List<GachaPoolItem> ConvertNamesToItems(
        List<string> star5Names, List<string> star4Names,
        Dictionary<string, int> nameToIdMap,
        Dictionary<string, string> avatarList)
    {
        var items = new List<GachaPoolItem>();

        foreach (var name in star5Names)
        {
            nameToIdMap.TryGetValue(name, out var itemId);
            items.Add(new GachaPoolItem
            {
                ItemId = itemId,
                Name = name,
                ImageUrl = avatarList.TryGetValue(name, out var url) ? url : "",
                RankType = 5
            });
        }

        foreach (var name in star4Names)
        {
            nameToIdMap.TryGetValue(name, out var itemId);
            items.Add(new GachaPoolItem
            {
                ItemId = itemId,
                Name = name,
                ImageUrl = avatarList.TryGetValue(name, out var url) ? url : "",
                RankType = 4
            });
        }

        return items;
    }

    private async Task SavePoolMetadataToDbAsync(List<GachaPoolMetadata> pools, string poolType)
    {
        if (pools == null) return;

        var jsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var entities = pools.Select(pool => new GachaPoolMetadataEntity
        {
            Version = pool.Version,
            StartTime = pool.Start,
            EndTime = pool.End,
            UpItems = JsonSerializer.Serialize(pool.Items.Select(i => i.ItemId), jsonOptions),
            UpItemNames = JsonSerializer.Serialize(pool.Items.Select(i => i.Name), jsonOptions)
        }).ToList();

        _metadataRepo.UpsertPoolMetadata(poolType, entities);
        await Task.CompletedTask;
    }

    private List<GachaPoolMetadata> LoadPoolMetadataFromDb(string poolType)
    {
        var pools = new List<GachaPoolMetadata>();
        var entities = _metadataRepo.GetPoolMetadataByType(poolType);

        foreach (var entity in entities)
        {
            List<int> ids;
            try
            {
                ids = JsonSerializer.Deserialize<List<int>>(entity.UpItems) ?? new List<int>();
            }
            catch (JsonException)
            {
                ids = new List<int>();
            }

            List<string> names;
            try
            {
                names = JsonSerializer.Deserialize<List<string>>(entity.UpItemNames) ?? new List<string>();
            }
            catch (JsonException)
            {
                names = new List<string>();
            }

            var upItems = new List<GachaPoolItem>();
            for (var i = 0; i < ids.Count; i++)
            {
                upItems.Add(new GachaPoolItem
                {
                    ItemId = ids[i],
                    Name = i < names.Count ? names[i] : ""
                });
            }

            pools.Add(new GachaPoolMetadata
            {
                Version = entity.Version,
                Start = entity.StartTime,
                End = entity.EndTime,
                Items = upItems
            });
        }

        return pools;
    }

    private bool HasPoolMetadataCache()
    {
        return _metadataRepo.HasPoolMetadata();
    }

    #endregion
}
