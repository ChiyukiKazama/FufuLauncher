/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Data.Entities;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class GachaAnalysisViewModel
{
    #region Data Loading & UI Refresh

    public async Task LoadSavedGachaDataAsync()
    {
        DebugLog.Info("GachaAnalysisViewModel.Loading", "[Gacha] LoadSavedGachaDataAsync: loading data");
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
                    DebugLog.Info("GachaAnalysisViewModel.Loading", $"[Gacha] JSON migration failed: {ex.Message}");
                }
            }

            var uids = QueryKnownUidsFromDb();
            DebugLog.Info("GachaAnalysisViewModel.Loading",
                $"[Gacha] QueryKnownUids returned {uids.Count} UIDs: [{string.Join(", ", uids)}]");

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


        var account = _accountManager.GetActiveAccountEntry();
        if (account != null)
        {
            _currentUid = RoleSelection.Current(account)?.game_uid ?? "";
            LoadGachaLogsFromDb(_currentUid);
        }

        DebugLog.Info("GachaAnalysisViewModel.Loading",
            $"[Gacha] Load complete - {uids.Count} UIDs, metadata entries: {metadataCount}");
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
                    ? metadataCount > 0
                        ? "Gacha_LocalDataLoaded".GetLocalized()
                        : "Gacha_LocalHistoryLoaded".GetLocalized()
                    : "Gacha_RoleNoRecords".GetLocalized();
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
        DebugLog.Info("GachaAnalysisViewModel.Loading", $"[Gacha] Migrated {logs.Count} records from JSON to database");
    }

    private void SaveGachaDataAsync()
    {
        DebugLog.Info("GachaAnalysisViewModel.Loading",
            $"[Gacha] SaveGachaDataAsync: start, _currentUid={_currentUid}");
        SaveGachaLogsToDb();
        RefreshKnownUids();
        DebugLog.Info("GachaAnalysisViewModel.Loading", "[Gacha] SaveGachaDataAsync: done");

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
                DebugLog.Info("GachaAnalysisViewModel.Loading",
                    $"[Gacha] Failed to refresh gacha analysis UI: {ex.Message}");
            }
        });
    }

    #endregion
}