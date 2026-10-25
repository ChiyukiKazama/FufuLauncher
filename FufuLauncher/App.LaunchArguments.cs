/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Services;
using FufuLauncher.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;

namespace FufuLauncher;

public partial class App
{
    private static LaunchTriggerAction ParseRedirectedTrigger(AppActivationArguments? args)
    {
        try
        {
            if (args?.Data is Microsoft.UI.Xaml.LaunchActivatedEventArgs launchArgs)
            {
                return LaunchArguments.ParseTrigger(launchArgs.Arguments);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.LaunchArguments", $"[App] Failed to parse redirect launch arguments: {ex.Message}");
        }

        return LaunchTriggerAction.None;
    }

    private static void ProcessStartupLaunchArguments()
    {
        try
        {
            var trigger = LaunchArguments.ParseTrigger(Environment.GetCommandLineArgs());
            if (trigger != LaunchTriggerAction.None)
            {
                _ = TriggerGameUpdateOperationAsync(trigger);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.LaunchArguments", $"[App] Failed to handle launch arguments: {ex.Message}");
        }
    }

    private static async Task TriggerGameUpdateOperationAsync(LaunchTriggerAction trigger)
    {
        try
        {
            if (AppPaths.IsFirstRun || MainWindow is not MainWindow mainWindow || mainWindow.IsAgreementShowing)
            {
                return;
            }

            var kind = trigger == LaunchTriggerAction.Predownload
                ? GameUpdateOperationKind.Predownload
                : GameUpdateOperationKind.Update;

            string? gameDir = null;
            var configService = GetService<IGameConfigService>();
            if (configService is not null)
            {
                gameDir = await configService.GetSavedGamePathAsync();
            }

            if (string.IsNullOrEmpty(gameDir))
            {
                await ShowLaunchTriggerErrorAsync("Err_GamePathNotFound".GetLocalized());
                return;
            }

            if (File.Exists(gameDir))
            {
                gameDir = Path.GetDirectoryName(gameDir) ?? gameDir;
            }

            var newWindow = new GameUpdateWindow(gameDir, kind, autoStart: true);
            newWindow.Activate();
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.LaunchArguments",
                $"[App] Launch-argument triggered game operation failed: {ex.Message}");
        }
    }

    private static async Task ShowLaunchTriggerErrorAsync(string message)
    {
        try
        {
            var root = MainWindow?.Content?.XamlRoot;
            if (root is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "ErrorTitle".GetLocalized(),
                Content = message,
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = root
            };
            await dialog.ShowAsync();
        }
        catch
        {
        }
    }
}