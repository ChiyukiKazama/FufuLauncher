/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Data.Entities;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public partial class GachaAnalysisViewModel
{
    #region Persistence & UID Management

    private void InitializeDatabase()
    {
    }

    private void LoadMetadataFromDb()
    {
        _savedMetadata.Clear();
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            CharacterMetadataPreview.Clear();
            WeaponMetadataPreview.Clear();
        });

        var entities = _metadataRepo.GetAllMetadata();
        foreach (var entity in entities)
        {
            var item = new ScrapedMetadata
            {
                Name = entity.Name,
                ImgSrc = string.IsNullOrWhiteSpace(entity.ImgSrc) ? null : entity.ImgSrc,
                ElementSrc = string.IsNullOrWhiteSpace(entity.ElementSrc) ? null : entity.ElementSrc,
                Type = entity.Type,
                Rank = entity.Rank,
                ItemId = entity.ItemId
            };
            _savedMetadata.Add(item);
            var isChar = item.Type == "char";
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (isChar) CharacterMetadataPreview.Add(item);
                else WeaponMetadataPreview.Add(item);
            });
        }
    }

    private void SaveMetadataToDb(List<ScrapedMetadata> newItems)
    {
        var entities = newItems.Select(item => new MetadataEntity
        {
            Name = item.Name ?? "",
            ImgSrc = item.ImgSrc ?? "",
            ElementSrc = item.ElementSrc ?? "",
            Type = item.Type ?? "",
            Rank = item.Rank ?? "",
            ItemId = item.ItemId ?? ""
        }).ToList();
        _metadataRepo.UpsertMetadata(entities);
    }

    private List<string> QueryKnownUidsFromDb()
    {
        return _metadataRepo.GetDistinctUids();
    }

    private void RefreshKnownUids()
    {
        var uids = QueryKnownUidsFromDb();
        var current = _currentUid;
        DebugLog.Info("GachaAnalysisViewModel.Persistence",
            $"[Gacha] RefreshKnownUids: found {uids.Count} UIDs: [{string.Join(", ", uids)}], current={current}");
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            RefreshKnownUidsUI(uids);
            if (!string.IsNullOrEmpty(current) && UidComboItems.Contains(current))
                SelectedUid = current;
        });
    }

    private void RefreshKnownUidsUI(List<string> uids)
    {
        IsUpdatingRoleList = true;
        var selected = SelectedUid;
        KnownUids.Clear();
        UidComboItems.Clear();
        foreach (var uid in uids)
        {
            KnownUids.Add(uid);
        }

        foreach (var uid in (_accountManager.GetActiveAccountEntry()?.GameRoles?.Select(r => r.game_uid) ?? [])
                 .Concat(uids).Distinct()) UidComboItems.Add(uid);
        UidComboItems.Add(AddNewUserItem);
        SelectedUid = selected;
        IsUpdatingRoleList = false;
    }

    private void LoadGachaLogsFromDb(string uid)
    {
        _cachedCharacterLogs.Clear();
        _cachedWeaponLogs.Clear();
        _cachedChronicledLogs.Clear();
        _cachedNoviceLogs.Clear();
        _cachedStandardLogs.Clear();

        if (string.IsNullOrEmpty(uid)) return;

        try
        {
            var entities = _metadataRepo.GetGachaLogsByUid(uid);
            foreach (var entity in entities)
            {
                var item = new GachaLogItem
                {
                    Id = entity.Id,
                    Uid = uid,
                    GachaType = entity.GachaType,
                    ItemId = entity.ItemId,
                    Count = entity.Count,
                    Time = entity.Time,
                    Name = entity.Name,
                    Lang = entity.Lang,
                    ItemType = entity.ItemType,
                    RankType = entity.RankType
                };
                var gt = GetNormalizedGachaType(item.GachaType);
                if (gt == "301") _cachedCharacterLogs.Add(item);
                else if (gt == "302") _cachedWeaponLogs.Add(item);
                else if (gt == "500") _cachedChronicledLogs.Add(item);
                else if (gt == "100") _cachedNoviceLogs.Add(item);
                else _cachedStandardLogs.Add(item);
            }

            DebugLog.Info("GachaAnalysisViewModel.Persistence",
                $"[Gacha] Load complete UID={uid}: character {_cachedCharacterLogs.Count}, weapon {_cachedWeaponLogs.Count}, chronicled {_cachedChronicledLogs.Count}, novice {_cachedNoviceLogs.Count}, standard {_cachedStandardLogs.Count}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("GachaAnalysisViewModel.Persistence", $"[Gacha] Failed to load gacha data: {ex.Message}");
        }
    }

    private void SaveGachaLogsToDb()
    {
        if (string.IsNullOrEmpty(_currentUid))
        {
            DebugLog.Info("GachaAnalysisViewModel.Persistence",
                "[Gacha] SaveGachaLogsToDb: _currentUid is empty, skipping save");
            return;
        }

        try
        {
            var totalBefore = _cachedCharacterLogs.Count + _cachedWeaponLogs.Count + _cachedChronicledLogs.Count +
                              _cachedNoviceLogs.Count + _cachedStandardLogs.Count;
            DebugLog.Info("GachaAnalysisViewModel.Persistence",
                $"[Gacha] SaveGachaLogsToDb: saving UID={_currentUid}, {totalBefore} records total");

            var allLogs = new List<GachaLogEntity>();
            AddLogItems(allLogs, _cachedCharacterLogs);
            AddLogItems(allLogs, _cachedWeaponLogs);
            AddLogItems(allLogs, _cachedChronicledLogs);
            AddLogItems(allLogs, _cachedNoviceLogs);
            AddLogItems(allLogs, _cachedStandardLogs);

            _metadataRepo.ReplaceGachaLogs(_currentUid, allLogs);
            DebugLog.Info("GachaAnalysisViewModel.Persistence",
                $"[Gacha] Save complete UID={_currentUid}: character {_cachedCharacterLogs.Count}, weapon {_cachedWeaponLogs.Count}, chronicled {_cachedChronicledLogs.Count}, novice {_cachedNoviceLogs.Count}, standard {_cachedStandardLogs.Count}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("GachaAnalysisViewModel.Persistence", $"[Gacha] Failed to save gacha data: {ex.Message}");
        }
    }

    private static void AddLogItems(List<GachaLogEntity> target, List<GachaLogItem> source)
    {
        foreach (var item in source)
        {
            target.Add(new GachaLogEntity
            {
                Id = item.Id ?? "",
                GachaType = item.GachaType ?? "",
                ItemId = item.ItemId,
                Count = item.Count,
                Time = item.Time,
                Name = item.Name,
                Lang = item.Lang,
                ItemType = item.ItemType,
                RankType = item.RankType
            });
        }
    }

    private async Task SwitchToUidAsync(string uid)
    {
        if (_currentUid == uid) return;
        if (!string.IsNullOrEmpty(_currentUid))
            SaveGachaLogsToDb();

        _currentUid = uid;
        ++_refreshVersion;
        LoadGachaLogsFromDb(uid);

        _ = _localSettingsService.SaveSettingAsync(LastSelectedUidKey, uid);

        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (_currentUid != uid) return;
            SelectedUid = uid;
            if (_cachedCharacterLogs.Count + _cachedWeaponLogs.Count + _cachedChronicledLogs.Count +
                _cachedStandardLogs.Count > 0)
            {
                RefreshUIFromCache();
                HasGachaData = true;
                CrawlerStatus = string.Format("Gacha_SwitchedToUid_Format".GetLocalized(), uid);
            }
            else
            {
                ClearCollections();
                CharacterStats = new GachaStatistic { PoolName = "Gacha_Pool_Character".GetLocalized() };
                WeaponStats = new GachaStatistic { PoolName = "Gacha_Pool_Weapon".GetLocalized() };
                ChronicledStats = new GachaStatistic { PoolName = "Gacha_Pool_Chronicled".GetLocalized() };
                StandardStats = new GachaStatistic { PoolName = "Gacha_Pool_Standard".GetLocalized() };
                InvalidateAnalysisDashboard();
                HasGachaData = false;
                CrawlerStatus = "Gacha_AccountNoRecords".GetLocalized();
            }
        });
    }

    private async Task<bool> HandleUidMismatchAsync(string incomingUid)
    {
        if (string.IsNullOrEmpty(incomingUid)) return true;
        if (string.IsNullOrEmpty(_currentUid)) return true;
        if (_currentUid == incomingUid) return true;

        if (OnUidMismatchAsync != null)
        {
            var accepted = await OnUidMismatchAsync(_currentUid, incomingUid);
            if (accepted)
            {
                await SwitchToUidAsync(incomingUid);
                return true;
            }

            return false;
        }

        return false;
    }

    public async Task ClearGachaDataAsync(string? expectedUid = null)
    {
        if (IsFetching || IsScraping) return;
        if (expectedUid != null && _currentUid != expectedUid) return;
        try
        {
            if (string.IsNullOrEmpty(_currentUid))
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage("Common_DeleteFailed".GetLocalized(),
                    "Gacha_NoAccountSelected".GetLocalized(),
                    NotificationType.Error, 3000));
                return;
            }

            var deletedUid = _currentUid;

            _metadataRepo.DeleteGachaLogsByUid(deletedUid);

            var remainingUids = QueryKnownUidsFromDb();

            var account = _accountManager.GetActiveAccountEntry();
            var selectedRole = account == null ? null : RoleSelection.Current(account);
            var nextUid = selectedRole?.game_uid ?? remainingUids.FirstOrDefault() ?? "";
            _currentUid = "";
            ++_refreshVersion;
            _cachedCharacterLogs.Clear();
            _cachedWeaponLogs.Clear();
            _cachedChronicledLogs.Clear();
            _cachedNoviceLogs.Clear();
            _cachedStandardLogs.Clear();
            RefreshKnownUidsUI(remainingUids);
            ClearCollections();
            CharacterStats = new GachaStatistic { PoolName = "Gacha_Pool_Character".GetLocalized() };
            WeaponStats = new GachaStatistic { PoolName = "Gacha_Pool_Weapon".GetLocalized() };
            ChronicledStats = new GachaStatistic { PoolName = "Gacha_Pool_Chronicled".GetLocalized() };
            StandardStats = new GachaStatistic { PoolName = "Gacha_Pool_Standard".GetLocalized() };
            HasGachaData = false;
            SelectedUid = "";
            GachaUrl = "";
            await _localSettingsService.SaveSettingAsync(LastSelectedUidKey, nextUid);
            if (!string.IsNullOrEmpty(nextUid)) await SwitchToUidAsync(nextUid);
            CrawlerStatus = string.Format("Gacha_DeletedUidRecords_Format".GetLocalized(), deletedUid);

            WeakReferenceMessenger.Default.Send(new NotificationMessage("Common_DeleteSuccess".GetLocalized(),
                string.Format("Gacha_DeletedUidRecordsDetail_Format".GetLocalized(), deletedUid),
                NotificationType.Success, 3000));
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage("Common_DeleteFailed".GetLocalized(),
                string.Format("Common_DeleteFailed_Format".GetLocalized(), ex.Message),
                NotificationType.Error, 5000));
        }
    }

    #endregion
}