/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Services.Mcp;
using FufuLauncher.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace FufuLauncher.Views;

public sealed partial class McpAuditWindow : Window
{
    private static McpAuditWindow? _current;

    public McpAuditViewModel ViewModel
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

        var window = new McpAuditWindow();
        window.Closed += (_, _) => _current = null;

        _current = window;
        window.Activate();
    }

    public McpAuditWindow()
    {
        ViewModel = new McpAuditViewModel(App.GetService<McpHostService>(),
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

        InitializeComponent();

        Title = "Mcp_AuditWindowTitle".GetLocalized();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Closed += OnClosed;

        ConfigureWindow();
        ApplyThemeAndChrome();

        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            try
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
            }
            catch
            {
                var isDark = IsDarkTheme();
                RootGrid.Background = new SolidColorBrush(isDark
                    ? Color.FromArgb(255, 32, 32, 32)
                    : Color.FromArgb(255, 243, 243, 243));
            }
        }

        _ = ViewModel.InitializeAsync();
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

            var scale = WindowManagerHelper.GetScaleFactor(this);
            var minWidth = (int)Math.Round(620 * scale);
            var minHeight = (int)Math.Round(480 * scale);
            var width = (int)Math.Round(800 * scale);
            var height = (int)Math.Round(760 * scale);

            var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                width = Math.Min(width, Math.Max(minWidth, displayArea.WorkArea.Width - 24));
                height = Math.Min(height, Math.Max(minHeight, displayArea.WorkArea.Height - 24));
            }

            var size = new SizeInt32(width, height);
            appWindow.Resize(size);
            WindowManagerHelper.CenterWindowOnScreen(appWindow, size.Width, size.Height);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = minWidth;
                presenter.PreferredMinimumHeight = minHeight;
            }
        }
        catch
        {
        }
    }

    private void ApplyThemeAndChrome()
    {
        try
        {
            var theme = App.GetService<IThemeSelectorService>().Theme;
            RootGrid.RequestedTheme = theme;

            var isDark = IsDarkTheme();
            var titleBar = AppWindow?.TitleBar;
            if (titleBar == null)
            {
                return;
            }

            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = isDark ? Colors.White : Colors.Black;
            titleBar.ButtonInactiveForegroundColor = isDark ? Colors.White : Colors.Black;
            titleBar.ButtonHoverForegroundColor = isDark ? Colors.White : Colors.Black;
            titleBar.ButtonHoverBackgroundColor = isDark
                ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x33, 0x00, 0x00, 0x00);
            titleBar.ButtonPressedBackgroundColor = isDark
                ? Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x66, 0x00, 0x00, 0x00);
        }
        catch
        {
        }
    }

    private bool IsDarkTheme()
    {
        var requested = RootGrid.RequestedTheme;
        return requested == ElementTheme.Dark ||
               (requested == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
    }

    private void OnOpenArchiveFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = ViewModel.ArchiveDirectory;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearAudit();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        ViewModel.Detach();
    }
}