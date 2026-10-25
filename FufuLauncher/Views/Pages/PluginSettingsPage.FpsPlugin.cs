/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage
{
    private bool _hasShownFpsWarning = false;
    private bool _isEnforcingFpsDisable = false;

    private async Task CheckAndShowFpsWarningAsync()
    {
        if (ViewModel.SelectedPluginIndex == 1 && !_hasShownFpsWarning && XamlRoot != null)
        {
            _hasShownFpsWarning = true;

            var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
            if (localSettings != null)
            {
                var hasDismissedObj = await localSettings.ReadSettingAsync("HasDismissedFpsWarning");
                bool hasDismissed = hasDismissedObj != null && Convert.ToBoolean(hasDismissedObj);

                if (hasDismissed)
                {
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = "Fps_CompatWarning_Title".GetLocalized(),
                    Content = "Fps_CompatWarning_Content".GetLocalized(),
                    PrimaryButtonText = "GotItBtn".GetLocalized(),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot
                };

                var checkBox = new CheckBox
                {
                    Content = "Fps_CompatWarning_DontShow".GetLocalized(),
                    Margin = new Thickness(0, 16, 0, 0)
                };

                var stackPanel = new StackPanel();
                stackPanel.Children.Add(new TextBlock
                {
                    Text = dialog.Content.ToString(),
                    TextWrapping = TextWrapping.Wrap
                });
                stackPanel.Children.Add(checkBox);

                dialog.Content = stackPanel;

                await dialog.ShowAsync();

                if (checkBox.IsChecked == true)
                {
                    await localSettings.SaveSettingAsync("HasDismissedFpsWarning", true);
                }
            }
        }
    }

    private async Task EnforceFpsPluginDisableAsync()
    {
        _isEnforcingFpsDisable = true;
        try
        {
            await Task.Delay(500);

            string fpsPluginPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "FPS", "FPS.dll");

            if (File.Exists(fpsPluginPath))
            {
                try
                {
                    File.Delete(fpsPluginPath);
                }
                catch (Exception ex)
                {
                    DebugLog.Info("PluginSettingsPage.FpsPlugin",
                        $"Failed to force-remove FPS plugin files: {ex.Message}");
                }

                await PerformFpsPluginRepairAsync(showUI: false);

                ViewModel.IsFpsPluginEnabled = true;
                await Task.Delay(100);
                ViewModel.IsFpsPluginEnabled = false;
            }
        }
        finally
        {
            _isEnforcingFpsDisable = false;
        }
    }

    private async void OnRepairFpsPluginClick(object sender, RoutedEventArgs e)
    {
        await PerformFpsPluginRepairAsync(showUI: true);
    }

    private async Task PerformFpsPluginRepairAsync(bool showUI)
    {
        string zipFilePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Launcher", "FPS.zip");
        string pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        string extractPath = Path.Combine(Path.GetTempPath(), "FPS_Extract_" + Guid.NewGuid());
        string finalDestDir = Path.Combine(pluginsDir, "FPS");

        if (!File.Exists(zipFilePath))
        {
            if (showUI)
                WeakReferenceMessenger.Default.Send(new NotificationMessage("ErrorTitle".GetLocalized(),
                    "Plugin_FileNotFound".GetLocalized(), NotificationType.Error));
            return;
        }

        var preLockedFile = FileLockHelper.FindLockedFileInDirectory(finalDestDir);
        if (preLockedFile != null)
        {
            if (showUI) await ShowLockedFileDialogAsync(preLockedFile);
            return;
        }

        ContentDialog progressDialog = null;
        if (showUI)
        {
            progressDialog = new ContentDialog
            {
                Title = "Fps_Repair_Title".GetLocalized(),
                Content = new ProgressBar { IsIndeterminate = true, Height = 20, Margin = new Thickness(0, 10, 0, 0) },
                XamlRoot = XamlRoot
            };
            _ = progressDialog.ShowAsync();
        }

        try
        {
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            Directory.CreateDirectory(extractPath);

            await Task.Run(() => ZipFile.ExtractToDirectory(zipFilePath, extractPath));

            var subDirs = Directory.GetDirectories(extractPath);
            string sourceDirToMove = (subDirs.Length == 1 && Directory.GetFiles(extractPath).Length == 0)
                ? subDirs[0]
                : extractPath;

            if (Directory.Exists(finalDestDir)) Directory.Delete(finalDestDir, true);

            await Task.Run(() => MoveDirectorySafe(sourceDirToMove, finalDestDir));

            if (progressDialog != null) progressDialog.Hide();
            ViewModel.LoadConfiguration();

            await VerifyFpsPluginHashAsync();

            if (showUI)
                WeakReferenceMessenger.Default.Send(new NotificationMessage("Success".GetLocalized(),
                    "Fps_Repair_Success".GetLocalized(), NotificationType.Success));

            ViewModel.RefreshPluginStates();
        }
        catch (Exception ex)
        {
            if (progressDialog != null) progressDialog.Hide();
            if (showUI)
            {
                var lockedFile = FileLockHelper.FindLockedFileInDirectory(finalDestDir);
                if (lockedFile != null)
                {
                    await ShowLockedFileDialogAsync(lockedFile);
                    return;
                }

                var failDialog = new ContentDialog
                {
                    Title = "Fps_Repair_Fail_Title".GetLocalized(),
                    Content = ex.Message,
                    CloseButtonText = "CloseBtn".GetLocalized(),
                    XamlRoot = XamlRoot
                };
                await failDialog.ShowAsync();
            }
        }
        finally
        {
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
        }
    }

    private async Task VerifyFpsPluginHashAsync()
    {
        try
        {
            string hashFilePath = Path.Combine(AppContext.BaseDirectory, "Assets", "hash.txt");
            string fpsPluginPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "FPS", "FPS.dll");

            if (!File.Exists(hashFilePath) || !File.Exists(fpsPluginPath))
            {
                return;
            }

            string expectedHash = string.Empty;
            using (var reader = new StreamReader(hashFilePath))
            {
                expectedHash = (await reader.ReadLineAsync())?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrEmpty(expectedHash))
            {
                return;
            }

            string actualHash = string.Empty;
            using (var sha512 = SHA512.Create())
            {
                using (var stream = File.OpenRead(fpsPluginPath))
                {
                    byte[] hashBytes = await sha512.ComputeHashAsync(stream);
                    actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }

            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                    "AdminWarningTitle".GetLocalized(),
                    "Fps_HashMismatch_Content".GetLocalized(),
                    NotificationType.Error));
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginSettingsPage.FpsPlugin", $"FPS plugin hash verification failed: {ex.Message}");
        }
    }
}