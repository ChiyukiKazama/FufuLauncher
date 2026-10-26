/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class SettingsViewModel
{
    #region 导航项

    public async Task InitializeNavItemsAsync()
    {
        NavItems.Clear();

        var allItems = new List<NavItemConfig>
        {
            new()
            {
                ViewModelKey = "FufuLauncher.ViewModels.MainViewModel", DisplayNameKey = "NavHome",
                IconGlyph = "\uE80F", IsForceVisible = true
            },
            new()
            {
                ViewModelKey = "FufuLauncher.ViewModels.PluginSettingsViewModel",
                DisplayNameKey = "InjectionSettingsNav", IconGlyph = "\uEA86"
            },
            new()
            {
                ViewModelKey = "FufuLauncher.ViewModels.ControlPanelViewModel", DisplayNameKey = "Panel_Toolbox",
                IconGlyph = "\uE80A"
            },
            new()
            {
                ViewModelKey = "FufuLauncher.ViewModels.GameSettingsViewModel",
                DisplayNameKey = "PageTitle_GameSettings",
                IconGlyph = "\uE7FC"
            },
            new()
            {
                ViewModelKey = "FufuLauncher.ViewModels.AccountViewModel", DisplayNameKey = "NavAccountSettings",
                IconGlyph = "\uE77B"
            },
            new()
            {
                ViewModelKey = "FufuLauncher.ViewModels.OtherViewModel", DisplayNameKey = "NavOtherFeatures",
                IconGlyph = "\uE71D"
            },
            new()
            {
                ViewModelKey = "FufuLauncher.ViewModels.PluginViewModel", DisplayNameKey = "PluginMgmtTitle",
                IconGlyph = "\uE7B5"
            },
            new()
            {
                ViewModelKey = "FufuLauncher.ViewModels.PluginStoreViewModel", DisplayNameKey = "PluginStoreTitle",
                IconGlyph = "\uE90F"
            },
            new()
            {
                ViewModelKey = "FufuLauncher.ViewModels.HelpViewModel", DisplayNameKey = "NavHelpDocs",
                IconGlyph = "\uE82D"
            },
            new()
            {
                ViewModelKey = "FufuLauncher.ViewModels.SettingsViewModel", DisplayNameKey = "NavSettings",
                IconGlyph = "\uE713", IsForceVisible = true
            },
        };

        foreach (var item in allItems)
        {
            var val = await _localSettingsService.ReadSettingAsync($"NavVisible_{SanitizeKey(item.ViewModelKey)}");
            if (val is bool b)
                item.IsUserVisible = b;
            else if (val is string str && bool.TryParse(str, out var parsed))
                item.IsUserVisible = parsed;


            var captured = item;
            item.VisibilityChanged += async (_, _) =>
            {
                var key = $"NavVisible_{SanitizeKey(captured.ViewModelKey)}";
                await _localSettingsService.SaveSettingAsync(key, captured.IsUserVisible);
                WeakReferenceMessenger.Default.Send(new NavigationVisibilityChangedMessage(captured));
            };

            NavItems.Add(item);
        }
    }

    private static string SanitizeKey(string viewModelKey)
    {
        var parts = viewModelKey.Split('.');
        return parts[^1];
    }

    #endregion
}