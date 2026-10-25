/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;

namespace FufuLauncher.ViewModels;

public partial class PluginSettingsViewModel
{
    #region HWID 授权与开发者功能

    private static bool _isHwidAuthorized = false;
    private static bool _hasCheckedHwid = false;

    private async Task<bool> CheckHwidAuthorizationAsync()
    {
        var authorizationService = App.GetService<Services.DeveloperAuthorizationService>();
        if (authorizationService is not null && authorizationService.IsTemporaryAuthorized)
        {
            return true;
        }

        if (_hasCheckedHwid && _isHwidAuthorized) return true;

        _isHwidAuthorized = authorizationService is not null && await authorizationService.IsAuthorizedAsync();
        _hasCheckedHwid = true;
        DebugLog.Info("PluginSettingsViewModel.Authorization",
            $"[HWID_DEBUG] Authorization result: {_isHwidAuthorized}");
        return _isHwidAuthorized;
    }

    public async Task TriggerBackgroundAuthCheckAsync()
    {
        if (_hasCheckedHwid && _isHwidAuthorized) return;

        bool isAuthorized = await CheckHwidAuthorizationAsync();
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        if (!isAuthorized)
        {
            return;
        }

        if (dispatcher != null)
        {
            dispatcher.TryEnqueue(() => LoadConfiguration());
        }
        else
        {
            LoadConfiguration();
        }
    }

    private async Task InitializeAuthAndReloadAsync()
    {
        if (!_hasCheckedHwid)
        {
            await CheckHwidAuthorizationAsync();

            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() => LoadConfiguration());
            }
            else
            {
                LoadConfiguration();
            }
        }
    }


    public async Task StartAsynchronousAuthAsync()
    {
        bool isAuthorized = await CheckHwidAuthorizationAsync();

        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher != null)
        {
            dispatcher.TryEnqueue(() => HandleAuthResult(isAuthorized));
        }
        else
        {
            HandleAuthResult(isAuthorized);
        }
    }

    private void HandleAuthResult(bool isAuthorized)
    {
        if (isAuthorized)
        {
            LoadConfiguration();
        }
    }

    private async Task VerifyAndApplyDevFeaturesAsync()
    {
        bool isAuthorized = await CheckHwidAuthorizationAsync();
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        Action action = () =>
        {
            if (isAuthorized)
            {
                SaveDevFeaturesSetting(true);
                LoadConfiguration();
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                    "PluginSettings_AuthFailedTitle".GetLocalized(),
                    "PluginSettings_DevPermissionRequired".GetLocalized(),
                    NotificationType.Error,
                    4000
                ));

                _isDevFeaturesEnabled = false;
                OnPropertyChanged(nameof(IsDevFeaturesEnabled));
                SaveDevFeaturesSetting(false);
                LoadConfiguration();
            }
        };

        if (dispatcher != null) dispatcher.TryEnqueue(() => action());
        else action();
    }

    private async Task VerifyAndApplyDevFeaturesOnStartupAsync()
    {
        bool isAuthorized = await CheckHwidAuthorizationAsync();
        if (!isAuthorized)
        {
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Action action = () =>
            {
                _isDevFeaturesEnabled = false;
                OnPropertyChanged(nameof(IsDevFeaturesEnabled));
                SaveDevFeaturesSetting(false);
                LoadConfiguration();
            };

            if (dispatcher != null) dispatcher.TryEnqueue(() => action());
            else action();
        }
    }

    private void SaveDevFeaturesSetting(bool isEnabled)
    {
        var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
        if (localSettings != null)
        {
            _ = localSettings.SaveSettingAsync("IsDevFeaturesEnabled", isEnabled);
        }
    }

    #endregion
}