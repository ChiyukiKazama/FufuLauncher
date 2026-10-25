/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

internal static class PluginDllConflictDialog
{
    internal static async Task<bool> ShowAsync(IReadOnlyList<PluginDllConflict> conflicts)
    {
        if (App.MainWindow?.Content?.XamlRoot is not { } xamlRoot) return false;

        var remaining = conflicts
            .Select(conflict => new PluginDllConflict(conflict.DllName, conflict.Candidates.ToList()))
            .ToList();

        var panel = new StackPanel { Spacing = 12, MinWidth = 520 };
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = GetBrush("SystemFillColorCriticalBrush", Microsoft.UI.Colors.Red)
        };

        void Rebuild()
        {
            panel.Children.Clear();

            panel.Children.Add(new TextBlock
            {
                Text = "PluginDllConflict_Message".GetLocalized(),
                TextWrapping = TextWrapping.Wrap
            });

            if (remaining.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "PluginDllConflict_Resolved".GetLocalized(),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = GetBrush("SystemFillColorSuccessBrush", Microsoft.UI.Colors.Green)
                });
            }
            else
            {
                foreach (var conflict in remaining)
                {
                    panel.Children.Add(BuildConflictCard(conflict, DeleteCandidate));
                }

                panel.Children.Add(new TextBlock
                {
                    Text = "PluginDllConflict_Hint".GetLocalized(),
                    FontSize = 12,
                    Opacity = 0.75,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            panel.Children.Add(status);
        }

        void DeleteCandidate(PluginDllConflict conflict, PluginDllCandidate candidate)
        {
            if (!PluginInjectionGuard.TryRemovePlugin(candidate.FilePath, out var error))
            {
                status.Text = string.Format("PluginDllConflict_DeleteFailedFormat".GetLocalized(), error);
                status.Visibility = Visibility.Visible;
                return;
            }

            status.Visibility = Visibility.Collapsed;

            var index = remaining.IndexOf(conflict);
            if (index < 0) return;

            var candidates = remaining[index].Candidates
                .Where(item => !ReferenceEquals(item, candidate))
                .ToList();

            if (candidates.Count < 2)
            {
                remaining.RemoveAt(index);
            }
            else
            {
                remaining[index] = new PluginDllConflict(conflict.DllName, candidates);
            }

            Rebuild();
        }

        Rebuild();

        var dialog = new ContentDialog
        {
            Title = "PluginDllConflict_Title".GetLocalized(),
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 460,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            PrimaryButtonText = "PluginPage_OpenPluginFolderBtn".GetLocalized(),
            CloseButtonText = "CloseBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            Resources = { ["ContentDialogMaxWidth"] = 760.0 }
        };

        var (shown, result) = await ShowWithRetryAsync(dialog);

        if (shown && result == ContentDialogResult.Primary)
        {
            OpenFolder(LightweightPluginService.PluginsDir);
        }

        return shown;
    }

    private static Border BuildConflictCard(PluginDllConflict conflict,
        Action<PluginDllConflict, PluginDllCandidate> onDelete)
    {
        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = string.Format("PluginDllConflict_DllNameFormat".GetLocalized(), conflict.DllName),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        for (var i = 0; i < conflict.Candidates.Count; i++)
        {
            panel.Children.Add(BuildCandidateRow(conflict, conflict.Candidates[i], i == 0, onDelete));
        }

        return new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = GetBrush("CardBackgroundFillColorDefaultBrush", Microsoft.UI.Colors.Transparent),
            Child = panel
        };
    }

    private static Grid BuildCandidateRow(
        PluginDllConflict conflict,
        PluginDllCandidate candidate,
        bool isNewest,
        Action<PluginDllConflict, PluginDllCandidate> onDelete)
    {
        var accent = GetBrush(
            isNewest ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush",
            isNewest ? Microsoft.UI.Colors.Green : Microsoft.UI.Colors.Red);

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = new TextBlock
        {
            Text = (isNewest ? "PluginDllConflict_NewestBadge" : "PluginDllConflict_OlderBadge").GetLocalized(),
            Foreground = accent,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(badge);

        var info = new StackPanel { Spacing = 2 };
        info.Children.Add(new TextBlock
        {
            Text = candidate.FilePath,
            Foreground = accent,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        });
        info.Children.Add(new TextBlock
        {
            Text = candidate.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
            FontSize = 12,
            Opacity = 0.7
        });
        Grid.SetColumn(info, 1);
        row.Children.Add(info);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(BuildLocateButton(candidate));
        actions.Children.Add(BuildDeleteButton(conflict, candidate, onDelete));
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);

        return row;
    }

    private static Button BuildLocateButton(PluginDllCandidate candidate)
    {
        var button = new Button
        {
            Content = "PluginDllConflict_LocateBtn".GetLocalized(),
            VerticalAlignment = VerticalAlignment.Center
        };

        button.Click += (_, _) => RevealInExplorer(candidate.FilePath);
        return button;
    }

    private static Button BuildDeleteButton(
        PluginDllConflict conflict,
        PluginDllCandidate candidate,
        Action<PluginDllConflict, PluginDllCandidate> onDelete)
    {
        var deleteButton = new Button
        {
            Content = "PluginDllConflict_DeleteBtn".GetLocalized(),
            VerticalAlignment = VerticalAlignment.Center
        };

        var confirmPanel = new StackPanel { Spacing = 10, Padding = new Thickness(10), MaxWidth = 320 };
        confirmPanel.Children.Add(new TextBlock
        {
            Text = "PluginDllConflict_DeleteConfirm".GetLocalized(),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });

        var confirmButton = new Button
        {
            Content = "PluginDllConflict_ConfirmDeleteBtn".GetLocalized(),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 50, 50)),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12
        };
        confirmPanel.Children.Add(confirmButton);

        var flyout = new Flyout { Content = confirmPanel };
        deleteButton.Flyout = flyout;

        confirmButton.Click += (_, _) =>
        {
            flyout.Hide();
            onDelete(conflict, candidate);
        };

        return deleteButton;
    }

    private static async Task<(bool Shown, ContentDialogResult Result)> ShowWithRetryAsync(ContentDialog dialog)
    {
        try
        {
            return (true, await dialog.ShowAsync());
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            await Task.Delay(300);

            try
            {
                return (true, await dialog.ShowAsync());
            }
            catch (Exception ex)
            {
                DebugLog.Info("PluginDllConflictDialog",
                    $"[PluginDllConflictDialog] Failed to show dialog: {ex.Message}");
                return (false, ContentDialogResult.None);
            }
        }
    }

    private static void RevealInExplorer(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginDllConflictDialog", $"[PluginDllConflictDialog] Failed to locate file: {ex.Message}");
        }
    }

    private static void OpenFolder(string folderPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(folderPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginDllConflictDialog",
                $"[PluginDllConflictDialog] Failed to open plugin directory: {ex.Message}");
        }
    }

    private static Brush GetBrush(string key, Windows.UI.Color fallback)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var brush) && brush is Brush value)
            {
                return value;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginDllConflictDialog",
                $"[PluginDllConflictDialog] Failed to read theme brushes: {ex.Message}");
        }

        return new SolidColorBrush(fallback);
    }
}