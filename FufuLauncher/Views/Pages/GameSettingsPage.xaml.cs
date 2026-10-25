/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Animation;

namespace FufuLauncher.Views;

public class StringToInitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string name && !string.IsNullOrEmpty(name))
        {
            return name.Substring(0, 1).ToUpper();
        }

        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed partial class GameSettingsPage : Page
{
    #region 字段与构造函数

    private GameConfigData? _currentConfig;
    private readonly string _accountsFilePath;
    private readonly ILocalSettingsService _localSettingsService;
    private Storyboard? _redeemExpandStoryboard;
    private bool _redeemCodesExpanded;

    private TextBox? _currentEditBox;
    private TextBlock? _currentTextBlock;
    private StackPanel? _currentStackPanel;
    private GameAccountData? _currentAccount;

    public GameSettingsPage()
    {
        InitializeComponent();
        _localSettingsService = App.GetService<ILocalSettingsService>();

        _accountsFilePath = Helpers.AppPaths.GameAccountsFile;

        Loaded += GameSettingsPage_Loaded;
    }

    #endregion

    #region 页面生命周期与共享辅助

    private async void GameSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();
        DebugLog.Info("GameSettingsPage", "[Debug] GameSettingsPage_Loaded started");

        try
        {
            var fpsSettingObj = await _localSettingsService.ReadSettingAsync("IsFpsOverlayEnabled");
            if (fpsSettingObj is bool isFpsEnabled)
            {
                FpsOverlayToggle.Toggled -= FpsOverlayToggle_Toggled;
                FpsOverlayToggle.IsOn = isFpsEnabled;
                FpsOverlayToggle.Toggled += FpsOverlayToggle_Toggled;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameSettingsPage", $"[Debug] Failed to read FPS display toggle: {ex.Message}");
        }

        try
        {
            var savedPathObj = await _localSettingsService.ReadSettingAsync("GameInstallationPath");
            var savedPath = savedPathObj as string;

            DebugLog.Info("GameSettingsPage", $"[Debug] Saved local path: '{savedPath}'");
            DebugLog.Info("GameSettingsPage",
                $"[Debug] IsNullOrWhiteSpace result: {string.IsNullOrWhiteSpace(savedPath)}");

            if (!string.IsNullOrWhiteSpace(savedPath))
            {
                DebugLog.Info("GameSettingsPage",
                    "[Debug] Path not empty, skipping auto-detect and loading existing path.");
                savedPath = savedPath.Trim('"').Trim();
                PathTextBox.Text = savedPath;
                await LoadGameInfoAsync(savedPath);
            }
            else
            {
                DebugLog.Info("GameSettingsPage", "[Debug] Path empty, calling GamePathFinder.FindGamePath()...");
                var foundPath = await GamePathFinder.FindGamePathAsync();
                DebugLog.Info("GameSettingsPage", $"[Debug] GamePathFinder returned path: '{foundPath}'");

                if (!string.IsNullOrEmpty(foundPath))
                {
                    DebugLog.Info("GameSettingsPage", "[Debug] Entering DispatcherQueue to call ShowAutoPathDialog");
                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        await ShowAutoPathDialog(foundPath);
                    });
                }
                else
                {
                    DebugLog.Info("GameSettingsPage", "[Debug] Path not found, skipping dialog.");
                }
            }

            await LoadAccountsAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameSettingsPage", $"[Debug] BlankPage_Loaded error: {ex.Message}\n{ex.StackTrace}");
        }

        await LoadRedeemCodesAsync();
        DebugLog.Info("GameSettingsPage", "[Debug] BlankPage_Loaded finished");
    }

    private async void FpsOverlayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (FpsOverlayToggle.IsOn)
        {
            if (!FpsOverlayService.Instance.IsAdministrator())
            {
                FpsOverlayToggle.IsOn = false;
                await ShowError("Err_FpsMonitorNeedsAdmin".GetLocalized());
                return;
            }

            await _localSettingsService.SaveSettingAsync("IsFpsOverlayEnabled", true);
        }
        else
        {
            FpsOverlayService.Instance.StopOverlay();
            await _localSettingsService.SaveSettingAsync("IsFpsOverlayEnabled", false);
        }
    }

    private async Task ShowError(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Title_OperationFailed".GetLocalized(),
            Content = message,
            CloseButtonText = "OkBtn".GetLocalized(),
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    #endregion
}