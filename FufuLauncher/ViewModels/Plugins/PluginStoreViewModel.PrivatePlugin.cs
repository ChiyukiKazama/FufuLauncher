/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.ViewModels;

public partial class PluginStoreViewModel
{
    #region Private Plugin

    private async Task AddPrivatePluginAsync()
    {
        string? pluginId = null;
        string? accessKey = null;

        if (App.MainWindow?.DispatcherQueue is not { } dispatcherQueue)
        {
            DebugLog.Info("PluginStoreViewModel.PrivatePlugin",
                "[PluginStoreVM] Cannot add private plugin: MainWindow or DispatcherQueue is null");
            return;
        }

        var tcs = new TaskCompletionSource();
        dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (App.MainWindow?.Content?.XamlRoot is not { } xamlRoot)
                {
                    DebugLog.Info("PluginStoreViewModel.PrivatePlugin",
                        "[PluginStoreVM] Cannot add private plugin: XamlRoot is null");
                    tcs.TrySetResult();
                    return;
                }

                var idBox = new TextBox { PlaceholderText = "PluginStore_PluginId".GetLocalized(), Width = 300 };
                var keyBox = new TextBox { PlaceholderText = "PluginStore_AccessKey".GetLocalized(), Width = 300 };

                var panel = new StackPanel { Spacing = 12 };
                panel.Children.Add(new TextBlock { Text = "PluginStore_EnterPrivateIdAndKey".GetLocalized() });
                panel.Children.Add(new TextBlock
                    { Text = "PluginStore_PluginId".GetLocalized(), FontSize = 12, Opacity = 0.7 });
                panel.Children.Add(idBox);
                panel.Children.Add(new TextBlock
                    { Text = "PluginStore_AccessKey".GetLocalized(), FontSize = 12, Opacity = 0.7 });
                panel.Children.Add(keyBox);

                var dialog = new ContentDialog
                {
                    Title = "PluginStore_InstallPrivatePluginTitle".GetLocalized(),
                    Content = panel,
                    PrimaryButtonText = "Common_Confirm".GetLocalized(),
                    SecondaryButtonText = "Common_Cancel".GetLocalized(),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = xamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    pluginId = idBox.Text.Trim();
                    accessKey = keyBox.Text.Trim();
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("PluginStoreViewModel.PrivatePlugin",
                    $"[PluginStoreVM] Error in private plugin dialog: {ex}");
            }
            finally
            {
                tcs.TrySetResult();
            }
        });

        await tcs.Task;

        if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(accessKey))
            return;

        try
        {
            IsLoading = true;
            StatusMessage = "PluginStore_VerifyingPrivatePlugin".GetLocalized();

            var accessResult = await _storeService.GetPrivateAccessAsync(pluginId, accessKey);
            if (accessResult.Plugin != null)
            {
                accessResult.Plugin.AccessToken = accessResult.AccessToken;
                accessResult.Plugin.ArchivePassword = accessResult.ArchivePassword;
                UpdateLocalState(accessResult.Plugin);

                Plugins.Insert(0, accessResult.Plugin);
                TotalPlugins++;

                _hasContent = true;
                IsEmpty = false;
                StatusMessage = string.Format("PluginStore_InstallingPrivatePlugin_Format".GetLocalized(),
                    accessResult.Plugin.Name);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreViewModel.PrivatePlugin",
                $"[PluginStoreVM] AddPrivatePlugin error: {ex.Message}");
            StatusMessage = string.Format("PluginStore_InstallPrivatePluginFailed_Format".GetLocalized(), ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion
}