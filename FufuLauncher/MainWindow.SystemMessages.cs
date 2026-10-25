/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace FufuLauncher;

public sealed partial class MainWindow
{
    #region Network & System Messages

    private void OnNetworkStatusChanged(object? sender, NetworkStatusChangedEventArgs e)
    {
        if (!_isMainUiLoaded) return;

        var msg = "";
        var icon = "";
        var color = Colors.White;

        if (e.IsNetworkLost)
        {
            msg = "NetDisconnected".GetLocalized();
            icon = "\uEB55";
            color = Colors.OrangeRed;
        }
        else if (e.IsProxyNewlyEnabled)
        {
            msg = "NetProxyWarning".GetLocalized();
            icon = "\uE12B";
            color = Colors.DodgerBlue;
        }

        ShowAutoDismissMessage(msg, icon, color);
    }

    private void ShowAutoDismissMessage(string message, string iconGlyph, Color iconColor)
    {
        if (!_isMainUiLoaded) return;

        if (SystemMessageBar.Visibility == Visibility.Collapsed)
            SystemMessageBar.Visibility = Visibility.Visible;

        SystemMessageText.Text = message;
        SystemMessageIcon.Glyph = iconGlyph;
        SystemMessageIcon.Foreground = new SolidColorBrush(iconColor);

        _messageDismissTimer.Stop();
        _messageDismissTimer.Start();

        if (_isSystemMessageVisible) return;

        _isSystemMessageVisible = true;

        var anim = new DoubleAnimation
        {
            From = 100,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, SystemMessageTranslate);
        Storyboard.SetTargetProperty(anim, "Y");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    private void HideSystemMessage()
    {
        _messageDismissTimer.Stop();

        if (!_isSystemMessageVisible) return;
        _isSystemMessageVisible = false;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 100,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(anim, SystemMessageTranslate);
        Storyboard.SetTargetProperty(anim, "Y");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    private async Task CheckPeriodicAnnouncementAsync()
    {
        try
        {
            var announcementUrl = await _announcementService.CheckForNewAnnouncementAsync();
            if (!string.IsNullOrEmpty(announcementUrl))
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    var announcementWindow = new Views.WebAnnouncementWindow(announcementUrl);
                    announcementWindow.Activate();
                });
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.SystemMessages",
                $"[Announcement] Scheduled announcement check failed: {ex.Message}");
        }
    }

    private async Task CheckPeriodicConstraintAsync()
    {
        try
        {
            await _constraintService.RefreshAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.SystemMessages", $"[Constraint] Scheduled check failed: {ex.Message}");
        }
    }

    #endregion

    #region Environment Checks

    private async Task CheckAndWarnVCRedistAsync()
    {
        var ignoreFilePath = Path.Combine(AppContext.BaseDirectory, ".no_vc_warning");
        if (File.Exists(ignoreFilePath)) return;

        if (!SystemEnvironmentHelper.IsVCRedistInstalled())
        {
            try
            {
                if (Content is FrameworkElement rootElement)
                {
                    if (rootElement.XamlRoot == null)
                    {
                        var tcs = new TaskCompletionSource<bool>();
                        RoutedEventHandler onLoaded = null!;
                        onLoaded = (_, _) =>
                        {
                            rootElement.Loaded -= onLoaded;
                            tcs.TrySetResult(true);
                        };
                        rootElement.Loaded += onLoaded;
                        await tcs.Task;
                    }

                    ContentDialog dialog = new()
                    {
                        XamlRoot = rootElement.XamlRoot,
                        Title = "VcRuntimeMissingTitle".GetLocalized(),
                        Content = "VcRuntimeMissingContent".GetLocalized(),
                        PrimaryButtonText = "GoToDownload".GetLocalized(),
                        SecondaryButtonText = "DontRemindAgain".GetLocalized(),
                        CloseButtonText = "IgnoreWarning".GetLocalized(),
                        DefaultButton = ContentDialogButton.Primary
                    };

                    var result = await dialog.ShowAsync();

                    if (result == ContentDialogResult.Primary)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = ApiEndpoints.VcRedistDownloadUrl,
                            UseShellExecute = true
                        });
                    }
                    else if (result == ContentDialogResult.Secondary)
                    {
                        File.Create(ignoreFilePath).Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("MainWindow.SystemMessages", $"Failed to show VC runtime warning dialog: {ex.Message}");
            }
        }
    }

    #endregion
}