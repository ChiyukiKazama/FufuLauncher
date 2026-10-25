/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Diagnostics;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace FufuLauncher.Views;

public sealed partial class OfficialBackgroundWindow : Window
{
    private static OfficialBackgroundWindow? _current;

    public OfficialBackgroundViewModel ViewModel
    {
        get;
    }

    public static void ShowOrActivate()
    {
        if (_current != null)
        {
            _current.Activate();
            return;
        }

        var window = new OfficialBackgroundWindow();
        window.Closed += (_, _) => _current = null;

        _current = window;
        window.Activate();
    }

    public OfficialBackgroundWindow()
    {
        ViewModel = new OfficialBackgroundViewModel(this);

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnWindowClosed;

        ConfigureWindow();
        ApplyThemeAndTitleBar();
        _ = ApplyBackdropFromSettingsAsync();

        ViewModel.LoadCommand.Execute(null);
    }

    private void ConfigureWindow()
    {
        try
        {
            var appWindow = AppWindow;
            if (appWindow == null)
            {
                return;
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var size = new SizeInt32(1120, 760);
            appWindow.Resize(size);
            WindowManagerHelper.CenterWindowOnScreen(appWindow, size.Width, size.Height);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 940;
                presenter.PreferredMinimumHeight = 620;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("OfficialBackgroundWindow", $"[OfficialBgWindow] Window initialization failed: {ex.Message}");
        }
    }

    private void ApplyThemeAndTitleBar()
    {
        try
        {
            var theme = App.GetService<IThemeSelectorService>().Theme;
            RootGrid.RequestedTheme = theme;

            var isDark = theme == ElementTheme.Dark ||
                         (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

            var titleBar = AppWindow?.TitleBar;
            if (titleBar == null)
            {
                return;
            }

            titleBar.BackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonForegroundColor = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            titleBar.ButtonInactiveForegroundColor = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            titleBar.ButtonHoverForegroundColor = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            titleBar.ButtonHoverBackgroundColor = isDark
                ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x33, 0x00, 0x00, 0x00);
            titleBar.ButtonPressedBackgroundColor = isDark
                ? Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x66, 0x00, 0x00, 0x00);
        }
        catch (Exception ex)
        {
            DebugLog.Info("OfficialBackgroundWindow", $"[OfficialBgWindow] Failed to apply theme: {ex.Message}");
        }
    }

    private async Task ApplyBackdropFromSettingsAsync()
    {
        try
        {
            var settings = App.GetService<ILocalSettingsService>();
            var backdropJson = await settings.ReadSettingAsync("WindowBackdrop");
            var backdropType = backdropJson != null
                ? (WindowBackdropType)Convert.ToInt32(backdropJson)
                : WindowBackdropType.Acrylic;

            if (backdropType == WindowBackdropType.None)
            {
                var isDark = RootGrid.ActualTheme == ElementTheme.Dark;
                RootGrid.Background = new SolidColorBrush(isDark
                    ? Color.FromArgb(255, 32, 32, 32)
                    : Color.FromArgb(255, 243, 243, 243));
                return;
            }

            SystemBackdrop = backdropType == WindowBackdropType.Mica
                ? new MicaBackdrop()
                : new DesktopAcrylicBackdrop();
        }
        catch (Exception ex)
        {
            DebugLog.Info("OfficialBackgroundWindow",
                $"[OfficialBgWindow] Failed to apply background material: {ex.Message}");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OfficialBackgroundViewModel.PreviewPlayer))
        {
            return;
        }

        try
        {
            VideoPreviewElement.SetMediaPlayer(ViewModel.PreviewPlayer);
        }
        catch (Exception ex)
        {
            DebugLog.Info("OfficialBackgroundWindow",
                $"[OfficialBgWindow] Failed to attach preview player: {ex.Message}");
        }
    }

    private void BackgroundGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is OfficialBackgroundItem item)
        {
            ViewModel.SelectedItem = item;
        }
    }

    private async void QuickSave_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OfficialBackgroundItem item })
        {
            await ViewModel.SaveCommand.ExecuteAsync(item);
        }
    }

    private void BackgroundThumbnail_ImageOpened(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            image.Visibility = Visibility.Visible;
        }
    }

    private void BackgroundThumbnail_ImageFailed(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            image.Visibility = Visibility.Collapsed;
        }
    }

    private void PreviewImage_ImageFailed(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            image.Visibility = Visibility.Collapsed;
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        try
        {
            VideoPreviewElement.SetMediaPlayer(null);
        }
        catch (Exception ex)
        {
            DebugLog.Info("OfficialBackgroundWindow",
                $"[OfficialBgWindow] Failed to detach preview player: {ex.Message}");
        }

        ViewModel.Cleanup();
    }
}