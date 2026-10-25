/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FufuLauncher.ViewModels;

public partial class MainViewModel
{
    #region 界面样式与卡片可见性

    [ObservableProperty] private Visibility _widgetCardVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility _widgetGachaVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _widgetAchievementVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _widgetInventoryVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _widgetPlayerRoleVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _widgetDailyNoteWindowVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _widgetVideoVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _widgetBBSVisibility = Visibility.Visible;

    [ObservableProperty] private Brush _panelBackgroundBrush;
    [ObservableProperty] private double _infoCardHeight = 285;
    [ObservableProperty] private string _infoExpandIcon = "\uE70E";
    private bool _isInfoCardExpanded = true;
    private double _panelOpacityValue = 0.5;

    [ObservableProperty] private bool _isPanelExpanded = true;
    [ObservableProperty] private Visibility _gameNewsCardVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _checkinCardVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _dailyNoteCardVisibility = Visibility.Visible;

    [ObservableProperty] private SolidColorBrush _launchButtonOverlayBrush = new(Microsoft.UI.Colors.Transparent);

    [ObservableProperty] private SolidColorBrush _gameNewsCardTextBrush = new(Microsoft.UI.Colors.White);
    [ObservableProperty] private SolidColorBrush _launchButtonTextBrush = new(Microsoft.UI.Colors.White);
    [ObservableProperty] private SolidColorBrush _gameCheckinTextBrush = new(Microsoft.UI.Colors.White);

    public IRelayCommand TogglePanelCommand
    {
        get;
    }

    public IRelayCommand ToggleInfoCardCommand
    {
        get;
    }

    public IAsyncRelayCommand OpenScreenshotFolderCommand
    {
        get;
    }

    public event Action<bool> InfoCardToggledRequested;

    private void ToggleInfoCard()
    {
        _isInfoCardExpanded = !_isInfoCardExpanded;
        if (_isInfoCardExpanded)
        {
            InfoExpandIcon = "\uE70E";
        }
        else
        {
            InfoExpandIcon = "\uE70D";
        }

        InfoCardToggledRequested?.Invoke(_isInfoCardExpanded);
    }

    private void UpdatePanelBackgroundBrush()
    {
        try
        {
            var themeService = App.GetService<IThemeSelectorService>();
            var currentTheme = themeService.Theme;

            if (currentTheme == ElementTheme.Default)
            {
                currentTheme = Application.Current.RequestedTheme == ApplicationTheme.Light
                    ? ElementTheme.Light
                    : ElementTheme.Dark;
            }

            Color baseColor;
            if (currentTheme == ElementTheme.Light)
            {
                baseColor = Microsoft.UI.Colors.White;
            }
            else
            {
                baseColor = Color.FromArgb(255, 32, 32, 32);
            }

            PanelBackgroundBrush = new SolidColorBrush(baseColor) { Opacity = _panelOpacityValue };
            DebugLog.Info("MainViewModel.Ui",
                $"[MainViewModel] Background updated - theme: {currentTheme}, opacity: {_panelOpacityValue}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainViewModel.Ui", $"[MainViewModel] Failed to update background: {ex.Message}");
        }
    }

    private async Task LoadCardVisibilityAsync()
    {
        var hideNewsCardJson = await _localSettingsService.ReadSettingAsync("IsHideGameNewsCardEnabled");
        bool isNewsCardHidden = hideNewsCardJson != null && Convert.ToBoolean(hideNewsCardJson);
        GameNewsCardVisibility = isNewsCardHidden ? Visibility.Collapsed : Visibility.Visible;

        var hideCheckinCardJson = await _localSettingsService.ReadSettingAsync("IsHideCheckinCardEnabled");
        bool isCheckinCardHidden = hideCheckinCardJson != null && Convert.ToBoolean(hideCheckinCardJson);
        CheckinCardVisibility = isCheckinCardHidden ? Visibility.Collapsed : Visibility.Visible;

        var hideDailyNoteCardJson = await _localSettingsService.ReadSettingAsync("IsHideDailyNoteCardEnabled");
        bool isDailyNoteCardHidden = hideDailyNoteCardJson == null || Convert.ToBoolean(hideDailyNoteCardJson);
        DailyNoteCardVisibility = isDailyNoteCardHidden ? Visibility.Collapsed : Visibility.Visible;

        int activeCount = 0;

        var showResinJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteResin");
        bool showResin = showResinJson == null || Convert.ToBoolean(showResinJson);
        if (showResin) activeCount++;
        ShowResin = showResin ? Visibility.Visible : Visibility.Collapsed;

        var showDailyTasksJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteDailyTasks");
        bool showDailyTasks = (showDailyTasksJson == null || Convert.ToBoolean(showDailyTasksJson)) && activeCount < 3;
        if (showDailyTasks) activeCount++;
        ShowDailyTasks = showDailyTasks ? Visibility.Visible : Visibility.Collapsed;

        var showHomeCoinJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteHomeCoin");
        bool showHomeCoin = (showHomeCoinJson == null || Convert.ToBoolean(showHomeCoinJson)) && activeCount < 3;
        if (showHomeCoin) activeCount++;
        ShowHomeCoin = showHomeCoin ? Visibility.Visible : Visibility.Collapsed;

        var showExpeditionsJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteExpeditions");
        bool showExpeditions =
            (showExpeditionsJson == null || Convert.ToBoolean(showExpeditionsJson)) && activeCount < 3;
        if (showExpeditions) activeCount++;
        ShowExpeditions = showExpeditions ? Visibility.Visible : Visibility.Collapsed;

        var showTransformerJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteTransformer");
        bool showTransformer =
            (showTransformerJson == null || Convert.ToBoolean(showTransformerJson)) && activeCount < 3;
        ShowTransformer = showTransformer ? Visibility.Visible : Visibility.Collapsed;

        var showPresetCardJson = await _localSettingsService.ReadSettingAsync("IsShowPresetCardEnabled");
        bool isShowPresetCard = showPresetCardJson != null && Convert.ToBoolean(showPresetCardJson);
        PresetCardVisibility = isShowPresetCard ? Visibility.Visible : Visibility.Collapsed;

        var isShowWidgetCardJson = await _localSettingsService.ReadSettingAsync("IsShowWidgetCardEnabled");
        bool isShowWidgetCard = isShowWidgetCardJson != null && Convert.ToBoolean(isShowWidgetCardJson);
        WidgetCardVisibility = isShowWidgetCard ? Visibility.Visible : Visibility.Collapsed;

        var showWidgetGachaJson = await _localSettingsService.ReadSettingAsync("ShowWidgetGacha");
        WidgetGachaVisibility = (showWidgetGachaJson == null || Convert.ToBoolean(showWidgetGachaJson))
            ? Visibility.Visible
            : Visibility.Collapsed;

        var showWidgetAchievementJson = await _localSettingsService.ReadSettingAsync("ShowWidgetAchievement");
        WidgetAchievementVisibility =
            (showWidgetAchievementJson == null || Convert.ToBoolean(showWidgetAchievementJson))
                ? Visibility.Visible
                : Visibility.Collapsed;

        var showWidgetInventoryJson = await _localSettingsService.ReadSettingAsync("ShowWidgetInventory");
        WidgetInventoryVisibility = (showWidgetInventoryJson == null || Convert.ToBoolean(showWidgetInventoryJson))
            ? Visibility.Visible
            : Visibility.Collapsed;

        var showWidgetPlayerRoleJson = await _localSettingsService.ReadSettingAsync("ShowWidgetPlayerRole");
        WidgetPlayerRoleVisibility = (showWidgetPlayerRoleJson == null || Convert.ToBoolean(showWidgetPlayerRoleJson))
            ? Visibility.Visible
            : Visibility.Collapsed;

        var showWidgetDailyNoteWindowJson = await _localSettingsService.ReadSettingAsync("ShowWidgetDailyNoteWindow");
        WidgetDailyNoteWindowVisibility =
            (showWidgetDailyNoteWindowJson == null || Convert.ToBoolean(showWidgetDailyNoteWindowJson))
                ? Visibility.Visible
                : Visibility.Collapsed;

        var showWidgetVideoJson = await _localSettingsService.ReadSettingAsync("ShowWidgetVideo");
        WidgetVideoVisibility = (showWidgetVideoJson == null || Convert.ToBoolean(showWidgetVideoJson))
            ? Visibility.Visible
            : Visibility.Collapsed;

        var showWidgetBBSJson = await _localSettingsService.ReadSettingAsync("ShowWidgetBBS");
        WidgetBBSVisibility = (showWidgetBBSJson == null || Convert.ToBoolean(showWidgetBBSJson))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task LoadUserPreferencesAsync()
    {
        await LoadCardVisibilityAsync();
        var pref = await _localSettingsService.ReadSettingAsync("PreferVideoBackground");
        if (pref != null)
        {
            PreferVideoBackground = Convert.ToBoolean(pref);
        }

        if (_devBuildDetectionService.HasChecked && !_devBuildDetectionService.IsDevBuild && PreferVideoBackground)
        {
            PreferVideoBackground = false;
            await _localSettingsService.SaveSettingAsync("PreferVideoBackground", false);
            await _localSettingsService.SaveSettingAsync("UserPreferVideoBackground", false);
            DebugLog.Info("MainViewModel.Ui", "[MainViewModel] Disabling animated background");
        }

        var panelOpacityJson = await _localSettingsService.ReadSettingAsync("PanelBackgroundOpacity");
        try
        {
            _panelOpacityValue = panelOpacityJson != null ? Convert.ToDouble(panelOpacityJson) : 0.5;
        }
        catch
        {
            _panelOpacityValue = 0.5;
        }
    }

    private async Task LoadTextStylesAsync()
    {
        var newsColor = await _localSettingsService.ReadSettingAsync("GameNewsCardTextColor") as string ?? "#FFFFFF";
        var newsOpacity =
            Convert.ToDouble(await _localSettingsService.ReadSettingAsync("GameNewsCardTextOpacity") ?? 1.0);
        GameNewsCardTextBrush = CreateBrush(newsColor, newsOpacity);

        var launchColor = await _localSettingsService.ReadSettingAsync("LaunchButtonTextColor") as string ?? "#FFFFFF";
        var launchOpacity =
            Convert.ToDouble(await _localSettingsService.ReadSettingAsync("LaunchButtonTextOpacity") ?? 1.0);
        LaunchButtonTextBrush = CreateBrush(launchColor, launchOpacity);

        var checkinColor = await _localSettingsService.ReadSettingAsync("GameCheckinTextColor") as string ?? "#FFFFFF";
        var checkinOpacity =
            Convert.ToDouble(await _localSettingsService.ReadSettingAsync("GameCheckinTextOpacity") ?? 1.0);
        GameCheckinTextBrush = CreateBrush(checkinColor, checkinOpacity);

        var launchOverlayColor = await _localSettingsService.ReadSettingAsync("LaunchButtonOverlayColor") as string ??
                                 "#0078D7";
        var autoThemeColor = App.GetService<IAutoThemeColorService>().ThemeColorHex;
        if (!string.IsNullOrWhiteSpace(autoThemeColor))
            launchOverlayColor = autoThemeColor;
        LaunchButtonOverlayBrush = CreateBrush(launchOverlayColor, 0.4);
    }

    private SolidColorBrush CreateBrush(string hex, double opacity)
    {
        try
        {
            if (string.IsNullOrEmpty(hex)) hex = "#FFFFFF";
            if (!hex.StartsWith("#")) hex = "#" + hex;
            if (hex.Length == 4)
            {
                hex = "#" + hex[1] + hex[1] + hex[2] + hex[2] + hex[3] + hex[3];
            }

            if (hex.Length != 7 && hex.Length != 9) hex = "#FFFFFF";

            byte a = 255;
            byte r, g, b;

            if (hex.Length == 9)
            {
                a = Convert.ToByte(hex.Substring(1, 2), 16);
                r = Convert.ToByte(hex.Substring(3, 2), 16);
                g = Convert.ToByte(hex.Substring(5, 2), 16);
                b = Convert.ToByte(hex.Substring(7, 2), 16);
            }
            else
            {
                r = Convert.ToByte(hex.Substring(1, 2), 16);
                g = Convert.ToByte(hex.Substring(3, 2), 16);
                b = Convert.ToByte(hex.Substring(5, 2), 16);
            }

            a = (byte)(a * opacity);

            return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
        }
        catch
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb((byte)(255 * opacity), 255, 255, 255));
        }
    }

    private async Task OpenScreenshotFolderAsync()
    {
        var savedPath = await _localSettingsService.ReadSettingAsync("GameInstallationPath");
        var gamePath = savedPath?.ToString()?.Trim('"')?.Trim();

        var gameScreenshotPath = "";
        if (!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))
        {
            gameScreenshotPath = Path.Combine(gamePath, "ScreenShot");
        }

        var customPathObj = await _localSettingsService.ReadSettingAsync("ScreenshotSavePath");
        var customScreenshotPath = customPathObj?.ToString()?.Trim('"')?.Trim();
        if (string.IsNullOrEmpty(customScreenshotPath))
        {
            customScreenshotPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "FufuScreenshots");
        }

        bool gameExists = !string.IsNullOrEmpty(gameScreenshotPath) && Directory.Exists(gameScreenshotPath);
        bool customExists = Directory.Exists(customScreenshotPath);

        if (!gameExists && !customExists)
        {
            _notificationService.Show("Screenshot_FolderNotFound".GetLocalized(),
                "Screenshot_FolderNotFoundMsg".GetLocalized(), NotificationType.Error, 0);
            return;
        }

        try
        {
            var galleryWindow = new ScreenshotGalleryWindow(
                gameScreenshotPath ?? "",
                customScreenshotPath ?? "");
            galleryWindow.Activate();
        }
        catch (Exception ex)
        {
            _notificationService.Show("Common_OpenFailed".GetLocalized(),
                string.Format("Main_InitViewFailed_Format".GetLocalized(), ex.Message), NotificationType.Error, 0);
        }
    }

    #endregion
}