/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Views;

namespace FufuLauncher.ViewModels;

public partial class AccountViewModel
{
    public async Task DeleteAccountAsync(AccountInfo account)
    {
        if (account == null) return;
        var accountId = account.AccountId;
        bool isCurrentAccount = _accountManager.ActiveAccountId == accountId;

        try
        {
            await _accountManager.DeleteAccountAsync(accountId);

            RefreshSavedAccountsList();

            if (isCurrentAccount)
            {
                var activeId = _accountManager.ActiveAccountId;
                if (activeId != null)
                {
                    await LoadActiveAccountAsync(activeId);
                }
                else
                {
                    RunOnUIThread(() =>
                    {
                        IsBoundRoleLoading = false;
                        CurrentAccount = null;
                        GameRolesInfo = null;
                        UserFullInfo = null;
                        LoginButtonText = "Account_LoginMiyoushe".GetLocalized();
                        StatusMessage = "Account_NoAccount".GetLocalized();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            RunOnUIThread(() =>
                StatusMessage = string.Format("Account_DeleteFailed_Format".GetLocalized(), ex.Message));
        }
    }

    private async Task LoginAsync()
    {
        if (_accountManager.GetAllAccounts().Count >= MaxAccounts)
        {
            StatusMessage = string.Format("Account_MaxAccounts_Format".GetLocalized(), MaxAccounts);
            return;
        }

        try
        {
            RunOnUIThread(() => StatusMessage = "Account_OpeningLoginWindow".GetLocalized());
            var loginWindow = new LoginQrWindow();
            var (cookies, serverType) = await loginWindow.ShowAndWaitAsync();

            DebugLog.Info("AccountViewModel.Operations",
                $"[LoginAsync] Login succeeded, cookies: {cookies.Count}, server: {serverType}");

            var entry = await _accountManager.AddAccountAsync(cookies, serverType,
                nickname: "Account_AddAccount".GetLocalized());
            await _accountManager.SwitchAccountAsync(entry.Id);

            await LoadActiveAccountAsync(entry.Id);
            RefreshSavedAccountsList();

            RunOnUIThread(() => StatusMessage = "Account_LoginSuccess".GetLocalized());
        }
        catch (TaskCanceledException)
        {
            RunOnUIThread(() => StatusMessage = "Account_LoginCancelled".GetLocalized());
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountViewModel.Operations", $"[LoginAsync] Exception: {ex.Message}");
            RunOnUIThread(() => StatusMessage = string.Format("Account_LoginError_Format".GetLocalized(), ex.Message));
        }
    }

    private async Task LogoutAsync()
    {
        try
        {
            Interlocked.Increment(ref _loadVersion);
            await _accountManager.LogoutAsync();
            WeakReferenceMessenger.Default.Send(new AccountChangedMessage());

            RunOnUIThread(() =>
            {
                IsBoundRoleLoading = false;
                CurrentAccount = null;
                GameRolesInfo = null;
                UserFullInfo = null;
                LoginButtonText = "Account_LoginMiyoushe".GetLocalized();
                StatusMessage = "Account_Logout".GetLocalized();
            });
            _lastNotifiedAccountId = null;

            RefreshSavedAccountsList();
        }
        catch (Exception ex)
        {
            RunOnUIThread(() =>
                StatusMessage = string.Format("Account_LogoutFailed_Format".GetLocalized(), ex.Message));
        }
    }

    private async Task SwitchToAccountAsync(AccountInfo? targetAccount)
    {
        if (targetAccount == null) return;

        RunOnUIThread(() =>
        {
            IsBoundRoleLoading = true;
            GameRolesInfo = null;
            UserFullInfo = null;
        });
        await _accountManager.SwitchAccountAsync(targetAccount.AccountId);
        WeakReferenceMessenger.Default.Send(new AccountChangedMessage());
        await LoadActiveAccountAsync(targetAccount.AccountId);

        if (_accountManager.ActiveAccountId != targetAccount.AccountId)
            return;

        if (_lastNotifiedAccountId == targetAccount.AccountId)
            return;

        if (UserFullInfo?.data == null && GameRolesInfo?.data == null)
            return;

        _lastNotifiedAccountId = targetAccount.AccountId;
        RefreshSavedAccountsList();
        RunOnUIThread(() => StatusMessage = "Account_SwitchSuccess".GetLocalized());
        _notificationService.Show("Account_SwitchSuccess".GetLocalized(),
            string.Format("Account_LoggedInTo_Format".GetLocalized(), targetAccount.Nickname), NotificationType.Success,
            3000);
    }

    private async Task AddNewAccountAsync()
    {
        if (_accountManager.GetAllAccounts().Count >= MaxAccounts)
        {
            StatusMessage = string.Format("Account_MaxAccounts_Format".GetLocalized(), MaxAccounts);
            return;
        }

        try
        {
            var loginWindow = new LoginQrWindow();
            var (cookies, serverType) = await loginWindow.ShowAndWaitAsync();

            DebugLog.Info("AccountViewModel.Operations",
                $"[AddNewAccount] Login succeeded, cookies: {cookies.Count}, server: {serverType}");

            var entry = await _accountManager.AddAccountAsync(cookies, serverType,
                nickname: "Account_AddAccount".GetLocalized());
            await _accountManager.SwitchAccountAsync(entry.Id);

            await LoadActiveAccountAsync(entry.Id);
            RefreshSavedAccountsList();
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            RunOnUIThread(() =>
                StatusMessage = string.Format("Account_AddAccountFailed_Format".GetLocalized(), ex.Message));
        }
    }
}