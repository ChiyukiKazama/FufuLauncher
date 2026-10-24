/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Services.GameServer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class MainPage
{
    private bool _serverSwitchInProgress;
    #region 常规服/B服切换

    private async void SwitchToBilibili_Click(object sender, RoutedEventArgs e)
    {
        await PrepareAndSwitchServer(true);
    }

    private async void SwitchToOfficial_Click(object sender, RoutedEventArgs e)
    {
        await PrepareAndSwitchServer(false);
    }

    private async Task PrepareAndSwitchServer(bool toBilibili)
    {
        if (_serverSwitchInProgress) { return; }
        _serverSwitchInProgress = true;
        try
        {
            var localSettingsService = App.GetService<ILocalSettingsService>();
            var gamePathSetting = await localSettingsService.ReadSettingAsync("GameInstallationPath");

            var gameDir = gamePathSetting as string;
            if (!string.IsNullOrEmpty(gameDir))
            {
                gameDir = gameDir.Trim('"').Trim();
                if (File.Exists(gameDir)) { gameDir = Path.GetDirectoryName(gameDir); }
            }

            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                await ShowDialog("ErrorTitle".GetLocalized(), "Home_ErrNoGamePath".GetLocalized());
                return;
            }

            string configPath = Path.Combine(gameDir, "config.ini");
            if (!File.Exists(configPath))
            {
                await ShowDialog("ErrorTitle".GetLocalized(), "Home_ErrConfigNotFound".GetLocalized());
                return;
            }

            await PerformServerSwitch(gameDir, toBilibili);
        }
        catch (Exception ex)
        {
            await ShowDialog("ErrorTitle".GetLocalized(), $"{"Home_ErrSwitchException".GetLocalized()}: {ex.Message}");
        }
        finally
        {
            _serverSwitchInProgress = false;
        }
    }

    private async Task PerformServerSwitch(string gameDir, bool toBilibili)
    {
        using var cancellation = new CancellationTokenSource();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 12, MinWidth = 320 };
        content.Children.Add(status);
        content.Children.Add(new ProgressBar { IsIndeterminate = true });
        var progressDialog = new ContentDialog
        {
            Title = "SwitchServerTitle".GetLocalized(),
            Content = content,
            CloseButtonText = "CancelBtn".GetLocalized(),
            XamlRoot = XamlRoot,
        };
        progressDialog.CloseButtonClick += (_, args) =>
        {
            args.Cancel = true;
            progressDialog.CloseButtonText = string.Empty;
            cancellation.Cancel();
        };
        bool canClose = false;
        progressDialog.Closing += (_, args) =>
        {
            if (!canClose)
            {
                args.Cancel = true;
                cancellation.Cancel();
            }
        };
        var dialogOperation = progressDialog.ShowAsync();
        Exception? failure = null;
        try
        {
            var scheme = toBilibili ? GameServerScheme.FromPreset("Bili") : GameServerScheme.FromPreset("CN");
            await Task.Run(() => App.GetService<GameChannelSdkService>().SwitchSchemeAsync(gameDir, scheme,
                message => DispatcherQueue.TryEnqueue(() => status.Text = message), cancellation.Token));
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            canClose = true;
            progressDialog.Hide();
            await dialogOperation;
        }

        if (failure is OperationCanceledException) { return; }
        if (failure is not null)
        {
            await ShowDialog("Home_SwitchFailed".GetLocalized(), failure.Message);
            return;
        }

        var serverName = toBilibili ? "Home_BilibiliServer".GetLocalized() : "Home_OfficialServer".GetLocalized();
        var action = toBilibili ? "Home_Deployed".GetLocalized() : "Home_Cleaned".GetLocalized();
        await ShowDialog("Home_SwitchSuccess".GetLocalized(),
            string.Format("Home_SwitchedTo_Format".GetLocalized(), serverName, action));
    }

    private async Task ShowDialog(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "OkBtn".GetLocalized(),
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    #endregion
}
