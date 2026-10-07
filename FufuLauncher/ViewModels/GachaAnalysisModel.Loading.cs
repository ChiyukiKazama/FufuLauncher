/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Data.Entities;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class GachaAnalysisModel
{
    #region Data Loading & UI Refresh

    public async Task LoadSavedGachaDataAsync()
    {
        Debug.WriteLine("[Gacha] LoadSavedGachaDataAsync: 开始加载数据");
        var (uids, metadataCount) = await Task.Run(() =>
        {
            InitializeDatabase();
            LoadMetadataFromDb();

            if (File.Exists(_gachaDataPath))
            {
                try
                {
                    var json = File.ReadAllText(_gachaDataPath);
                    var data = JsonSerializer.Deserialize<LocalGachaData>(json);
                    if (data != null)
                    {
                        GachaUrl = data.Url;
                        var allLogs = (data.CharacterLogs ?? new())
                            .Concat(data.WeaponLogs ?? new())
                            .Concat(data.StandardLogs ?? new()).ToList();
                        if (allLogs.Count > 0)
                        {
                            MigrateJsonToDb(allLogs);
                            File.Move(_gachaDataPath, _gachaDataPath + ".bak");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Gacha] JSON 迁移失败: {ex.Message}");
                }
            }

            var uids = QueryKnownUidsFromDb();
            Debug.WriteLine($"[Gacha] QueryKnownUids 返回 {uids.Count} 个 UID: [{string.Join(", ", uids)}]");

            string lastUid = "";
            try
            {
                var lastUidObj = _localSettingsService.ReadSettingAsync(LastSelectedUidKey).GetAwaiter().GetResult();
                lastUid = lastUidObj as string ?? "";
            }
            catch
            {
            }

            if (uids.Count > 0)
            {
                if (!string.IsNullOrEmpty(lastUid) && uids.Contains(lastUid))
                    _currentUid = lastUid;
                else
                    _currentUid = uids[0];
                LoadGachaLogsFromDb(_currentUid);
                _ = _localSettingsService.SaveSettingAsync(LastSelectedUidKey, _currentUid);
            }

            return (uids, _savedMetadata.Count);
        });

        // Choose the account default before presenting any archive, so reopening the
        // window does not briefly show the previous UID while bindings are refreshed.
        var account = _accountManager.GetActiveAccountEntry();
        if (account != null)
        {
            _currentUid = RoleSelection.Current(account)?.game_uid ?? "";
            LoadGachaLogsFromDb(_currentUid);
        }

        Debug.WriteLine($"[Gacha] 加载完成 - {uids.Count} UIDs, metadata {metadataCount} 条");
        if (uids.Count > 0 || account != null)
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                RefreshKnownUidsUI(uids);
                SelectedUid = _currentUid;
                HasGachaData = _cachedCharacterLogs.Count + _cachedWeaponLogs.Count +
                    _cachedChronicledLogs.Count + _cachedNoviceLogs.Count + _cachedStandardLogs.Count > 0;
                if (HasGachaData) RefreshUIFromCache();
                else ClearCollections();
                IsDataLoaded = true;
                CrawlerStatus = HasGachaData
                    ? metadataCount > 0 ? "已加载本地数据和图片资源缓存" : "已加载本地历史记录"
                    : "该角色暂无抽卡记录";
            });

            if (!HasPoolMetadataCache())
            {
                _ = FetchGachaPoolMetadataAsync();
            }
        }
        else
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                UidComboItems.Clear();
                UidComboItems.Add(AddNewUserItem);
                IsDataLoaded = true;
            });
        }
    }

    private void MigrateJsonToDb(List<GachaLogItem> logs)
    {
        var entities = logs.Select(item => new GachaLogEntity
        {
            Id = item.Id ?? "",
            Uid = item.Uid ?? "unknown",
            GachaType = item.GachaType ?? "",
            ItemId = item.ItemId,
            Count = item.Count,
            Time = item.Time,
            Name = item.Name,
            Lang = item.Lang,
            ItemType = item.ItemType,
            RankType = item.RankType
        }).ToList();

        _metadataRepo.InsertOrIgnoreGachaLogs(entities);
        Debug.WriteLine($"[Gacha] 已从 JSON 迁移 {logs.Count} 条记录到数据库");
    }

    private void SaveGachaDataAsync()
    {
        Debug.WriteLine($"[Gacha] SaveGachaDataAsync: 开始, _currentUid={_currentUid}");
        SaveGachaLogsToDb();
        RefreshKnownUids();
        Debug.WriteLine("[Gacha] SaveGachaDataAsync: 完成");

        if (RequestMetadataScrapeAction == null)
            _ = FetchGachaPoolMetadataAsync();
    }

    private List<GachaLogItem> MergeLogs(List<GachaLogItem> existing, List<GachaLogItem> incoming)
    {
        if (existing == null) existing = new List<GachaLogItem>();
        if (incoming == null || incoming.Count == 0) return existing;

        var dict = existing.ToDictionary(x => x.Id);
        foreach (var item in incoming)
        {
            if (!dict.ContainsKey(item.Id)) dict[item.Id] = item;
        }

        return dict.Values.OrderBy(x => x.Id).ToList();
    }

    private void RefreshUIFromCache()
    {
        var charLogs = _cachedCharacterLogs.OrderBy(x => x.Id).ToList();
        var weaponLogs = _cachedWeaponLogs.OrderBy(x => x.Id).ToList();
        var chronicledLogs = _cachedChronicledLogs.OrderBy(x => x.Id).ToList();
        var standardLogs = _cachedStandardLogs.OrderBy(x => x.Id).ToList();

        var version = ++_refreshVersion;

        _ = Task.Run(() =>
        {
            try
            {
                var charPools = LoadCharacterPoolMetadata();
                var weaponPools = LoadPoolMetadataFromDb("302");

                var charStats = _gachaService.AnalyzePool("301", charLogs);
                var weaponStats = _gachaService.AnalyzePool("302", weaponLogs);
                var chronicledStats = _gachaService.AnalyzePool("500", chronicledLogs);
                var standardStats = _gachaService.AnalyzePool("200", standardLogs);

                var charFive = BuildDisplayCollection(charStats.FiveStarRecords, "角色", charPools, "301");
                var charFour = BuildDisplayCollection(charStats.FourStarRecords, "角色", charPools, "301");
                var weaponFive = BuildDisplayCollection(weaponStats.FiveStarRecords, "武器", weaponPools, "302");
                var weaponFour = BuildDisplayCollection(weaponStats.FourStarRecords, "武器", weaponPools, "302");
                var chronicledFive = BuildDisplayCollection(chronicledStats.FiveStarRecords, "集录", null, "500");
                var chronicledFour = BuildDisplayCollection(chronicledStats.FourStarRecords, "集录", null, "500");
                var standardFive = BuildDisplayCollection(standardStats.FiveStarRecords, "常驻", null, "200");
                var standardFour = BuildDisplayCollection(standardStats.FourStarRecords, "常驻", null, "200");

                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_refreshVersion != version) return;

                    CharacterStats = charStats;
                    WeaponStats = weaponStats;
                    ChronicledStats = chronicledStats;
                    StandardStats = standardStats;
                    CharacterFiveStars = charFive;
                    CharacterFourStars = charFour;
                    WeaponFiveStars = weaponFive;
                    WeaponFourStars = weaponFour;
                    ChronicledFiveStars = chronicledFive;
                    ChronicledFourStars = chronicledFour;
                    StandardFiveStars = standardFive;
                    StandardFourStars = standardFour;
                    InvalidateAnalysisDashboard();
                    InvalidateHistory();

                    // 通知相关属性更新
                    OnPropertyChanged(nameof(ShowCharacterNoRecords));
                    OnPropertyChanged(nameof(ShowWeaponNoRecords));
                    OnPropertyChanged(nameof(ShowChronicledNoRecords));
                    OnPropertyChanged(nameof(ShowStandardNoRecords));
                    OnPropertyChanged(nameof(ShowCharacterFourDivider));
                    OnPropertyChanged(nameof(ShowWeaponFourDivider));
                    OnPropertyChanged(nameof(ShowChronicledFourDivider));
                    OnPropertyChanged(nameof(ShowStandardFourDivider));

                    if (_savedMetadata != null && _savedMetadata.Count > 0) _ = ApplyMetadataToUIAsync(_savedMetadata);
                    if (IsAnalysisSelected) _ = EnsureAnalysisDashboardAsync();
                    if (IsHistorySelected) _ = EnsureHistoryAsync();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Gacha] 刷新抽卡分析 UI 失败: {ex.Message}");
            }
        });
    }

    #endregion
}
