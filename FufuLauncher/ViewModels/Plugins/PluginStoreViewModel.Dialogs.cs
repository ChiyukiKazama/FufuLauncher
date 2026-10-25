/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.ViewModels;

public partial class PluginStoreViewModel
{
    #region Shared UI Dialogs

    private static Task<string?> ShowGeetestCaptchaAsync(string verifyUrl) =>
        PluginCaptchaDialog.ShowAsync(verifyUrl);

    private static async Task ShowMinVersionWarningAsync(PluginStoreItem item)
    {
        if (App.MainWindow?.DispatcherQueue is not { } dispatcherQueue)
        {
            DebugLog.Info("PluginStoreViewModel.Dialogs",
                "[PluginStoreVM] Cannot show version warning: MainWindow or DispatcherQueue is null");
            return;
        }

        dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (App.MainWindow?.Content?.XamlRoot is not { } xamlRoot)
                {
                    DebugLog.Info("PluginStoreViewModel.Dialogs",
                        "[PluginStoreVM] Cannot show version warning: XamlRoot is null");
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = "PluginStore_VersionTooLowTitle".GetLocalized(),
                    Content = string.Format("PluginStore_VersionTooLowMessage_Format".GetLocalized(), item.Name,
                        item.MinAppVersion, CurrentAppVersion),
                    CloseButtonText = "Common_GotIt".GetLocalized(),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = xamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                DebugLog.Info("PluginStoreViewModel.Dialogs", $"[PluginStoreVM] Error showing version warning: {ex}");
            }
        });
    }

    private static async Task<string?> ShowPrivateAccessDialogAsync(PluginStoreItem item)
    {
        var tcs = new TaskCompletionSource<string?>();

        if (App.MainWindow?.DispatcherQueue is not { } dispatcherQueue)
        {
            DebugLog.Info("PluginStoreViewModel.Dialogs",
                "[PluginStoreVM] Cannot show private access dialog: MainWindow or DispatcherQueue is null");
            return null;
        }

        var enqueued = dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var inputBox = new TextBox
                {
                    PlaceholderText = "PluginStore_AccessKey".GetLocalized(),
                    Width = 300
                };

                var stackPanel = new StackPanel { Spacing = 12 };
                stackPanel.Children.Add(new TextBlock
                {
                    Text = string.Format("PluginStore_PrivatePluginKeyPrompt_Format".GetLocalized(), item.Name,
                        item.Id),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                });
                stackPanel.Children.Add(inputBox);

                if (App.MainWindow?.Content?.XamlRoot is not { } xamlRoot)
                {
                    DebugLog.Info("PluginStoreViewModel.Dialogs",
                        "[PluginStoreVM] Cannot show private access dialog: XamlRoot is null");
                    tcs.TrySetResult(null);
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = "PluginStore_PrivatePluginTitle".GetLocalized(),
                    Content = stackPanel,
                    PrimaryButtonText = "Common_Confirm".GetLocalized(),
                    SecondaryButtonText = "Common_Cancel".GetLocalized(),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = xamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputBox.Text))
                {
                    tcs.TrySetResult(inputBox.Text.Trim());
                }
                else
                {
                    tcs.TrySetResult(null);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("PluginStoreViewModel.Dialogs", $"[PluginStoreVM] Error in private access dialog: {ex}");
                tcs.TrySetResult(null);
            }
        });

        if (!enqueued)
        {
            DebugLog.Info("PluginStoreViewModel.Dialogs",
                "[PluginStoreVM] Failed to enqueue private access dialog to DispatcherQueue");
            return null;
        }

        return await tcs.Task;
    }

    #endregion
}