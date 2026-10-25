/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.ViewModels;

public partial class SettingsViewModel
{
    #region 语言切换

    private async Task ApplyLanguageChangeAsync(AppLanguage language)
    {
        try
        {
            DebugLog.Info("SettingsViewModel.Language",
                $"[SettingsVM] ApplyLanguageChangeAsync: language={language}, enumValue={(int)language}");

            await _localSettingsService.SaveSettingAsync("AppLanguage", (int)language);
            var culture = LanguagePreferenceResolver.Resolve(
                language,
                Windows.System.UserProfile.GlobalizationPreferences.Languages);

            DebugLog.Info("SettingsViewModel.Language", $"[SettingsVM] ApplyLanguageChangeAsync: culture='{culture}'");
            ResourceExtensions.SetLanguage(culture);

            if (language == AppLanguage.zhCN || language == AppLanguage.Default)
            {
                SelectedServer = ServerType.CN;
            }
            else
            {
                SelectedServer = ServerType.OS;
            }

            var dialog = new ContentDialog
            {
                Title = "LanguageChangedTitle".GetLocalized(),
                Content = "LanguageChangedMessage".GetLocalized(),
                PrimaryButtonText = "RestartNowBtn".GetLocalized(),
                CloseButtonText = "RestartLaterBtn".GetLocalized(),
                XamlRoot = App.MainWindow.Content.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                RestartApp();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsViewModel.Language", $"Failed to apply language: {ex.Message}");
        }
    }

    private static void RestartApp()
    {
        if (AppRestartHelper.TryRestart())
            return;


        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            "Restart_FailedTitle".GetLocalized(),
            "Restart_FailedMessage".GetLocalized(),
            NotificationType.Warning,
            6000));
    }

    #endregion
}