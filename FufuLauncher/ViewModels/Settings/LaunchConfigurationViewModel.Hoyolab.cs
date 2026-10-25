/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class LaunchConfigurationViewModel
{
    #region 米游社账号

    partial void OnIsUsingHoyolabAccountChanged(bool value)
    {
        if (_isInitializing) return;

        if (value)
        {
            _ = ValidateAndEnableHoyolabAccountAsync();
        }
        else
        {
            _ = _localSettingsService.SaveSettingAsync("UsingHoyolabAccount", false);
        }
    }

    private async Task ValidateAndEnableHoyolabAccountAsync()
    {
        try
        {
            var activeId = _accountManager.ActiveAccountId;
            if (string.IsNullOrEmpty(activeId))
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    IsUsingHoyolabAccount = false;
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "HoyolabAccount_NoLoggedIn_Title".GetLocalized(),
                        "HoyolabAccount_NoLoggedIn_Message".GetLocalized(),
                        NotificationType.Warning));
                });
                return;
            }

            var cookies = await _accountManager.LoadCookiesAsync(activeId);
            if (cookies == null || !cookies.ContainsKey("stoken") || string.IsNullOrEmpty(cookies["stoken"]))
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    IsUsingHoyolabAccount = false;
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "HoyolabAccount_LoginExpired_Title".GetLocalized(),
                        "HoyolabAccount_LoginExpired_Message".GetLocalized(),
                        NotificationType.Warning));
                });
                return;
            }

            if (!_gameLauncherService.IsGamePathSelected())
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    IsUsingHoyolabAccount = false;
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "HoyolabAccount_NoGamePath_Title".GetLocalized(),
                        "HoyolabAccount_NoGamePath_Message".GetLocalized(),
                        NotificationType.Warning));
                });
                return;
            }

            var result = await _authTicketService.CreateAuthTicketAsync(activeId);
            if (result.Success)
            {
                await _localSettingsService.SaveSettingAsync("UsingHoyolabAccount", true);
                _dispatcherQueue.TryEnqueue(() =>
                {
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "HoyolabAccount_Enabled_Title".GetLocalized(),
                        "HoyolabAccount_Enabled_Message".GetLocalized(),
                        NotificationType.Success,
                        5000));
                });
            }
            else
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    IsUsingHoyolabAccount = false;
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "HoyolabAccount_EnableFailed_Title".GetLocalized(),
                        "HoyolabAccount_EnableFailed_Message".GetLocalized(),
                        NotificationType.Error));
                });
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("LaunchConfigurationViewModel.Hoyolab",
                $"[SettingsVM] Exception during validation: {ex.Message}");
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsUsingHoyolabAccount = false;
                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                    "HoyolabAccount_TempUnavailable_Title".GetLocalized(),
                    "HoyolabAccount_TempUnavailable_Message".GetLocalized(),
                    NotificationType.Error));
            });
        }
    }

    #endregion
}