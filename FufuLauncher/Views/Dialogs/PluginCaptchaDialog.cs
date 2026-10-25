/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace FufuLauncher.Views;

internal static class PluginCaptchaDialog
{
    private const int PollIntervalMs = 500;
    private const int PollAttempts = 120;

    public static async Task<string?> ShowAsync(string? verifyUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(verifyUrl) || cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (App.MainWindow?.DispatcherQueue is not { } dispatcherQueue)
        {
            DebugLog.Info("PluginCaptchaDialog", "[Captcha] Main window dispatcher is unavailable");
            return null;
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueued = dispatcherQueue.TryEnqueue(async () =>
            await RunAsync(verifyUrl, completion, cancellationToken).ConfigureAwait(true));

        if (!enqueued)
        {
            DebugLog.Info("PluginCaptchaDialog", "[Captcha] Failed to enqueue the captcha window");
            return null;
        }

        using var registration = cancellationToken.Register(() => completion.TrySetResult(null));
        return await completion.Task.ConfigureAwait(false);
    }

    private static async Task RunAsync(string verifyUrl, TaskCompletionSource<string?> completion,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetResult(null);
            return;
        }

        Window? window = null;
        WebView2? webView = null;
        CancellationTokenSource? pollCts = null;
        CancellationTokenRegistration cancelRegistration = default;
        var finished = false;

        void Finish(string? token)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            try
            {
                pollCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                cancelRegistration.Dispose();
            }
            catch
            {
            }

            completion.TrySetResult(token);

            try
            {
                window?.Close();
            }
            catch
            {
            }
        }

        try
        {
            window = new Window
            {
                Title = "PluginStore_CaptchaTitle".GetLocalized(),
                SystemBackdrop = new MicaBackdrop()
            };

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = new Grid { Height = 32 };
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var titleText = new TextBlock
            {
                Text = "PluginStore_DownloadVerification".GetLocalized(),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };
            Grid.SetColumn(titleText, 1);
            titleBar.Children.Add(titleText);

            Grid.SetRow(titleBar, 0);
            rootGrid.Children.Add(titleBar);

            webView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetRow(webView, 1);
            rootGrid.Children.Add(webView);

            window.Content = rootGrid;

            if (window.AppWindow is { } appWindow)
            {
                appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                appWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                appWindow.Resize(new SizeInt32(1280, 720));

                if (App.MainWindow?.AppWindow is { } mainAppWindow)
                {
                    var mainPos = mainAppWindow.Position;
                    var mainSize = mainAppWindow.Size;
                    appWindow.Move(new PointInt32(
                        mainPos.X + (mainSize.Width - 1280) / 2,
                        mainPos.Y + (mainSize.Height - 720) / 2));
                }
            }

            window.SetTitleBar(titleBar);

            await webView.EnsureCoreWebView2Async();
            if (webView.CoreWebView2 is not { } coreWebView)
            {
                Finish(null);
                return;
            }

            coreWebView.Settings.AreDefaultContextMenusEnabled = false;
            coreWebView.Settings.IsStatusBarEnabled = false;

            pollCts = new CancellationTokenSource();
            var pollToken = pollCts.Token;

            cancelRegistration = cancellationToken.Register(() =>
            {
                var dispatcher = window?.DispatcherQueue;
                if (dispatcher == null)
                {
                    Finish(null);
                    return;
                }

                dispatcher.TryEnqueue(() => Finish(null));
            });

            window.Closed += (_, _) =>
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                try
                {
                    pollCts?.Cancel();
                }
                catch
                {
                }

                try
                {
                    cancelRegistration.Dispose();
                }
                catch
                {
                }

                completion.TrySetResult(null);
            };

            coreWebView.NavigationCompleted += async (_, e) =>
            {
                if (!e.IsSuccess || finished)
                {
                    return;
                }

                try
                {
                    for (var attempt = 0;
                         attempt < PollAttempts && !pollToken.IsCancellationRequested && !finished;
                         attempt++)
                    {
                        await Task.Delay(PollIntervalMs, pollToken);

                        string raw;
                        try
                        {
                            raw = await coreWebView.ExecuteScriptAsync("document.body.textContent");
                        }
                        catch
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            continue;
                        }

                        var unescaped = raw.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
                        if (!unescaped.StartsWith("{"))
                        {
                            continue;
                        }

                        try
                        {
                            using var document = JsonDocument.Parse(unescaped);
                            var root = document.RootElement;
                            if (root.TryGetProperty("retcode", out var retcode) && retcode.GetInt32() == 0 &&
                                root.TryGetProperty("data", out var data) &&
                                data.TryGetProperty("dl_token", out var dlToken))
                            {
                                var token = dlToken.GetString();
                                if (!string.IsNullOrWhiteSpace(token))
                                {
                                    Finish(token);
                                    return;
                                }
                            }
                        }
                        catch (JsonException)
                        {
                        }
                    }

                    Finish(null);
                }
                catch (TaskCanceledException)
                {
                }
            };

            DebugLog.Info("PluginCaptchaDialog", $"[Captcha] Navigating to gate page: {verifyUrl}");
            coreWebView.Navigate(verifyUrl);
            window.Activate();
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginCaptchaDialog", $"[Captcha] Window failed: {ex}");
            Finish(null);
        }
    }
}