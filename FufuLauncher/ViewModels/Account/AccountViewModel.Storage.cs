/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class AccountViewModel
{
    private async Task LoadActiveAccountAsync(string accountId)
    {
        var cookies = await _accountManager.LoadCookiesAsync(accountId);
        var entry = _accountManager.GetActiveAccountEntry();

        if (cookies == null || entry == null)
        {
            RunOnUIThread(() => CurrentAccount = null);
            return;
        }

        var info = new AccountInfo
        {
            AccountId = entry.Id,
            Nickname = entry.Nickname ?? "Common_Unnamed".GetLocalized(),
            Stuid = entry.Stuid,
            AvatarUrl = entry.AvatarUrl ?? "ms-appx:///Assets/DefaultAvatar.png",
            GameUid = entry.GameUid ?? "",
            Server = entry.ServerType
        };

        RunOnUIThread(() =>
        {
            CurrentAccount = info;
            TryApplyCachedBoundRoles(accountId);
        });

        var loaded = await LoadUserInfoAsync();
        if (!loaded)
        {
        }
    }

    private void RefreshSavedAccountsList()
    {
        RunOnUIThread(() =>
        {
            SavedAccounts.Clear();
            var activeId = _accountManager.ActiveAccountId;
            foreach (var entry in _accountManager.GetAllAccounts())
            {
                if (entry.Id == activeId)
                    continue;
                SavedAccounts.Add(new AccountInfo
                {
                    AccountId = entry.Id,
                    Nickname = entry.Nickname ?? "Common_Unnamed".GetLocalized(),
                    Stuid = entry.Stuid,
                    AvatarUrl = entry.AvatarUrl ?? "ms-appx:///Assets/DefaultAvatar.png",
                    GameUid = entry.GameUid ?? ""
                });
            }

            OnPropertyChanged(nameof(HasSavedAccounts));
        });
    }

    private async Task LoadAccountInfo()
    {
        try
        {
            DebugLog.Info("AccountViewModel.Storage", "[LoadAccountInfo] Loading account info");

            var activeId = _accountManager.ActiveAccountId;
            if (activeId == null)
            {
                DebugLog.Info("AccountViewModel.Storage", "[LoadAccountInfo] No active account");
                IsBoundRoleLoading = false;
                RunOnUIThread(() =>
                {
                    CurrentAccount = null;
                    StatusMessage = "Account_NoLoginInfo".GetLocalized();
                    LoginButtonText = "Account_Login".GetLocalized();
                });
                return;
            }

            IsBoundRoleLoading = true;
            var cookies = await _accountManager.LoadCookiesAsync(activeId);
            var entry = _accountManager.GetActiveAccountEntry();
            if (cookies == null || entry == null)
            {
                DebugLog.Info("AccountViewModel.Storage", "[LoadAccountInfo] Failed to load active account data");
                IsBoundRoleLoading = false;
                RunOnUIThread(() =>
                {
                    CurrentAccount = null;
                    StatusMessage = "Account_DataReadFailed".GetLocalized();
                    LoginButtonText = "Account_Login".GetLocalized();
                });
                return;
            }

            RunOnUIThread(() =>
            {
                CurrentAccount = new AccountInfo
                {
                    AccountId = entry.Id,
                    Nickname = entry.Nickname ?? "Common_Unnamed".GetLocalized(),
                    Stuid = entry.Stuid,
                    AvatarUrl = entry.AvatarUrl ?? "ms-appx:///Assets/DefaultAvatar.png",
                    GameUid = entry.GameUid ?? entry.Stuid,
                    Server = entry.ServerType,
                    HasBoundRole = false
                };
                TryApplyCachedBoundRoles(entry.Id);
                LoginButtonText = "Account_ReLogin".GetLocalized();
                StatusMessage = "Account_LoggedIn".GetLocalized();
            });

            DebugLog.Info("AccountViewModel.Storage",
                $"[LoadAccountInfo] Loaded account: {entry.Nickname} ({entry.Id})");

            RefreshSavedAccountsList();
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountViewModel.Storage", $"[LoadAccountInfo] Exception: {ex.Message}");
            RunOnUIThread(() =>
            {
                IsBoundRoleLoading = false;
                StatusMessage = string.Format("Account_LoadAccountFailed_Format".GetLocalized(), ex.Message);
                CurrentAccount = null;
                LoginButtonText = "Account_Login".GetLocalized();
            });
        }
    }

    public async Task RefreshDataAsync()
    {
        await LoadAccountInfo();
        RefreshSavedAccountsList();
    }
}