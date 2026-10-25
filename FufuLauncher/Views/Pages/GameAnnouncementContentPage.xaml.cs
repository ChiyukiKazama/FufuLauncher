/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models.GameAnnouncement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Windows.System;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class GameAnnouncementContentPage : Page
{
    private const string MihoyoSDKDefinition = """
                                               window.miHoYoGameJSSDK = {
                                                   openInBrowser: function(url){ window.chrome.webview.postMessage(url); },
                                                   openInWebview: function(url){ location.href = url }
                                               }
                                               """;

    private static readonly IReadOnlyDictionary<string, string> DarkLightReverts = new Dictionary<string, string>
    {
        ["color:rgba(0,0,0,1)"] = "color:rgba(255,255,255,1)",
        ["color:rgba(17,17,17,1)"] = "color:rgba(238,238,238,1)",
        ["color:rgba(51,51,51,1)"] = "color:rgba(204,204,204,1)",
        ["color:rgba(57,59,64,1)"] = "color:rgba(198,196,191,1)",
        ["color:rgba(73,73,73,1)"] = "color:rgba(182,182,182,1)",
        ["color:rgba(85,85,85,1)"] = "color:rgba(170,170,170,1)",
        ["background-color: rgb(255, 215, 185)"] = "background-color: rgb(0,40,70)",
        ["background-color: rgb(254, 245, 231)"] = "background-color: rgb(1,40,70)",
        ["background-color:rgb(244, 244, 245)"] = "background-color:rgba(11, 11, 10)",
    };

    private static readonly Regex VerticalAlignStyleRegex = new(" style=\"(?!\")*?vertical-align:middle;\"");
    private static readonly Regex RemRegex = new("[0-9]+\\.[0-9]+rem");

    private GameAnnouncement? _announcement;

    public GameAnnouncementContentPage()
    {
        InitializeComponent();

        Loaded += GameAnnouncementContentPage_Loaded;
        Unloaded += GameAnnouncementContentPage_Unloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _announcement = e.Parameter as GameAnnouncement;
        if (_announcement is not null)
        {
            ContentTitleText.Text = _announcement.Title;
        }
    }

    private async void GameAnnouncementContentPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string? html = _announcement is null ? null : GenerateHtml(_announcement, IsDarkMode());
            if (string.IsNullOrEmpty(html))
            {
                LoadingBar.Visibility = Visibility.Collapsed;
                NoContentPanel.Visibility = Visibility.Visible;
                return;
            }

            await ContentWebView.EnsureCoreWebView2Async();
            ContentWebView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

            CoreWebView2 core = ContentWebView.CoreWebView2;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(MihoyoSDKDefinition);
            core.WebMessageReceived += Core_WebMessageReceived;
            core.NavigationStarting += Core_NavigationStarting;
            core.NewWindowRequested += Core_NewWindowRequested;

            ContentWebView.NavigationCompleted += ContentWebView_NavigationCompleted;
            ContentWebView.NavigateToString(html);
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameAnnouncementContentPage",
                $"[GameAnnouncementContentPage] WebView2 initialization failed: {ex.Message}");
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private void GameAnnouncementContentPage_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            CoreWebView2? core = ContentWebView.CoreWebView2;
            if (core is not null)
            {
                core.WebMessageReceived -= Core_WebMessageReceived;
                core.NavigationStarting -= Core_NavigationStarting;
                core.NewWindowRequested -= Core_NewWindowRequested;
            }

            ContentWebView.NavigationCompleted -= ContentWebView_NavigationCompleted;
            ContentWebView.Close();
        }
        catch
        {
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void ContentWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        LoadingBar.Visibility = Visibility.Collapsed;
    }

    private void Core_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (Uri.TryCreate(args.TryGetWebMessageAsString(), UriKind.RelativeOrAbsolute, out Uri? uri))
        {
            _ = Launcher.LaunchUriAsync(uri);
        }
    }

    private void Core_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (args.Uri == "about:blank" || args.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        args.Cancel = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? uri))
        {
            _ = Launcher.LaunchUriAsync(uri);
        }
    }

    private void Core_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? uri))
        {
            _ = Launcher.LaunchUriAsync(uri);
        }
    }

    private static bool IsDarkMode()
    {
        IThemeSelectorService themeService = App.GetService<IThemeSelectorService>();
        ElementTheme theme = themeService.Theme;
        if (theme == ElementTheme.Default)
        {
            theme = Application.Current.RequestedTheme == ApplicationTheme.Light
                ? ElementTheme.Light
                : ElementTheme.Dark;
        }

        return theme == ElementTheme.Dark;
    }

    private static string? GenerateHtml(GameAnnouncement announcement, bool isDarkMode)
    {
        string content = announcement.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        content = VerticalAlignStyleRegex.Replace(content, string.Empty);
        content = RemRegex.Replace(content, "calc($0 * 10)");

        if (isDarkMode)
        {
            StringBuilder contentBuilder = new(content);
            foreach ((string dark, string light) in DarkLightReverts)
            {
                contentBuilder.Replace(dark, light);
            }

            content = contentBuilder.ToString();
        }

        string title = WebUtility.HtmlEncode(announcement.Title);
        string subtitle = WebUtility.HtmlEncode(announcement.Subtitle);
        string banner = WebUtility.HtmlEncode(announcement.Banner);
        string bodyColor = isDarkMode ? "rgba(255,255,255,1)" : "rgba(0,0,0,1)";

        return "<!DOCTYPE html><html><head>" +
               $"<title>{subtitle} - {title}</title>" +
               "<style>body::-webkit-scrollbar{display:none}img{border:none;vertical-align:middle;width:100%}</style>" +
               "</head>" +
               $"<body style=\"color:{bodyColor}; background-color: transparent;\">" +
               $"<h3>{title}</h3>" +
               $"<img src=\"{banner}\"/><br>" +
               content +
               "</body></html>";
    }
}