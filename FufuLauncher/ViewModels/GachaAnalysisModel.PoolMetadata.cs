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

    private const string CharacterPoolType = "301";
    private static readonly string[] CharacterPoolTypes = [CharacterPoolType, "400"];
    private static readonly string[] StoragePoolTypes = [.. CharacterPoolTypes, "302", "500"];

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

            var characterGroups = data.Result
                .Where(item => GetVersionPeriod(item.Version) != "混池")
                .GroupBy(item => (item.Version, item.Time));
            foreach (var group in characterGroups)
            {
                var entries = group.Take(2).ToList();
                if (group.Count() > 2)
                {
                    Debug.WriteLine($"[Gacha] 同期角色卡池超过两个，已忽略额外条目：{group.Key.Version} {group.Key.Time}");
                }

                foreach (var item in entries)
                {
                    var period = GetVersionPeriod(item.Version);
                    var (startTime, endTime) = ParseTimeRange(item.Time, period);
                    allPools.Add((CreatePoolMetadata(
                        item,
                        startTime,
                        endTime,
                        BuildPoolName(item.Star5Role, "角色活动祈愿"),
                        _charNameToIdMap,
                        data.AvatarList), CharacterPoolType));
                }
            }

            foreach (var item in data.Weapon.Where(item => GetVersionPeriod(item.Version) != "混池"))
            {
                var period = GetVersionPeriod(item.Version);
                var (startTime, endTime) = ParseTimeRange(item.Time, period);

                allPools.Add((CreatePoolMetadata(
                    item,
                    startTime,
                    endTime,
                    BuildPoolName(item.Star5Role, "武器活动祈愿"),
                    _weaponNameToIdMap,
                    data.AvatarList), "302"));
            }

            var chronicledEntries = data.Result
                    .Where(item => GetVersionPeriod(item.Version) == "混池")
                    .Select(item => (Item: item, Type: "char"))
                .Concat(data.Weapon
                    .Where(item => GetVersionPeriod(item.Version) == "混池")
                    .Select(item => (Item: item, Type: "weapon")));
            foreach (var group in chronicledEntries.GroupBy(entry => (entry.Item.Version, entry.Item.Time)))
            {
                var first = group.First().Item;
                var (startTime, endTime) = ParseTimeRange(first.Time, "混池");
                var items = group.SelectMany(entry => ConvertNamesToItems(
                        entry.Item.Star5Role,
                        entry.Item.Star4Role,
                        entry.Type == "char" ? _charNameToIdMap : _weaponNameToIdMap,
                        data.AvatarList))
                    .GroupBy(item => item.ItemId > 0 ? $"id:{item.ItemId}" : $"name:{item.Name}")
                    .Select(itemGroup => itemGroup.First())
                    .ToList();

                allPools.Add((new GachaPoolMetadata
                {
                    Version = first.Version,
                    PoolName = "集录祈愿",
                    BannerImageUrl = group.Select(entry => entry.Item.Avatar)
                        .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)) ?? string.Empty,
                    Start = startTime,
                    End = endTime,
                    Items = items
                }, "500"));
            }

            ApplyStorageVersionSuffixes(allPools);
            foreach (var poolType in StoragePoolTypes)
            {
                await SavePoolMetadataToDbAsync(
                    allPools.Where(entry => entry.poolType == poolType).Select(entry => entry.pool).ToList(),
                    poolType);
            }

            var count301 = allPools.Count(p => p.poolType == CharacterPoolType);
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

    private static GachaPoolMetadata CreatePoolMetadata(
        WishBannerItem item,
        string startTime,
        string endTime,
        string poolName,
        Dictionary<string, int> nameToIdMap,
        Dictionary<string, string> avatarList) => new()
    {
        Version = item.Version,
        PoolName = poolName,
        BannerImageUrl = item.Avatar ?? string.Empty,
        Start = startTime,
        End = endTime,
        Items = ConvertNamesToItems(item.Star5Role, item.Star4Role, nameToIdMap, avatarList)
    };

    private static string BuildPoolName(IEnumerable<string> featuredFiveStars, string fallback)
    {
        var names = featuredFiveStars.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        return names.Count == 0 ? fallback : string.Join(" / ", names);
    }

    private static void ApplyStorageVersionSuffixes(List<(GachaPoolMetadata pool, string poolType)> pools)
    {
        foreach (var group in pools.GroupBy(entry => (entry.poolType, entry.pool.Version)))
        {
            var index = 0;
            foreach (var entry in group.OrderBy(item => item.pool.Start, StringComparer.Ordinal))
            {
                index++;
                if (index > 1)
                {
                    entry.pool.Version = $"{entry.pool.Version}-{index}";
                }
            }
        }
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
            PoolName = pool.PoolName,
            BannerImageUrl = pool.BannerImageUrl,
            StartTime = pool.Start,
            EndTime = pool.End,
            UpItems = JsonSerializer.Serialize(pool.Items.Select(i => i.ItemId), jsonOptions),
            UpItemNames = JsonSerializer.Serialize(pool.Items.Select(i => i.Name), jsonOptions)
        }).ToList();

        _metadataRepo.ReplacePoolMetadata(poolType, entities);
        await Task.CompletedTask;
    }

    private List<GachaPoolMetadata> LoadPoolMetadataFromDb(params string[] poolTypes) =>
        _metadataRepo.GetPoolMetadataByType(poolTypes).Select(ToPoolMetadata).ToList();

    private List<GachaPoolMetadata> LoadCharacterPoolMetadata() => LoadPoolMetadataFromDb(CharacterPoolTypes);

    private List<CharacterPoolCandidate> LoadCharacterPoolCandidates() =>
        _metadataRepo.GetPoolMetadataByType(CharacterPoolTypes)
            .Select(entity => new CharacterPoolCandidate(ToPoolMetadata(entity), entity.PoolType))
            .ToList();

    private static GachaPoolMetadata ToPoolMetadata(GachaPoolMetadataEntity entity)
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

        return new GachaPoolMetadata
        {
            Version = entity.Version,
            PoolName = entity.PoolName,
            BannerImageUrl = entity.BannerImageUrl,
            Start = entity.StartTime,
            End = entity.EndTime,
            Items = upItems
        };
    }

    private bool HasPoolMetadataCache()
    {
        return _metadataRepo.HasPoolMetadata();
    }

    private sealed record CharacterPoolCandidate(GachaPoolMetadata Pool, string PoolType);

    #endregion
}
