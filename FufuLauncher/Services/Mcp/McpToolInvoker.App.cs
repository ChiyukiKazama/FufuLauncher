/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Services.Mcp;

public sealed partial class McpToolInvoker
{
    private static readonly IReadOnlyDictionary<string, string> PageTags =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] = "FufuLauncher.ViewModels.MainViewModel",
            ["control_panel"] = "FufuLauncher.ViewModels.ControlPanelViewModel",
            ["game_settings"] = "FufuLauncher.ViewModels.GameSettingsViewModel",
            ["account"] = "FufuLauncher.ViewModels.AccountViewModel",
            ["other"] = "FufuLauncher.ViewModels.OtherViewModel",
            ["plugins"] = "FufuLauncher.ViewModels.PluginViewModel",
            ["plugin_store"] = "FufuLauncher.ViewModels.PluginStoreViewModel",
            ["plugin_settings"] = "FufuLauncher.ViewModels.PluginSettingsViewModel",
            ["help"] = "FufuLauncher.ViewModels.HelpViewModel",
            ["settings"] = "FufuLauncher.ViewModels.SettingsViewModel"
        };

    private static readonly IReadOnlyDictionary<string, Func<Window?>> ToolWindowFactories =
        new Dictionary<string, Func<Window?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["gacha_analysis"] = () => new GachaAnalysisWindow(),
            ["data_center"] = () => new DataCenterWindow(),
            ["calculator"] = () => new CalculatorWindow(),
            ["cycle_challenge"] = () => new CycleChallengeWindow(),
            ["achievements"] = () => new AchievementWindow(),
            ["inventory"] = () => new InventoryWindow(),
            ["player_info"] = () => new PlayerInfoWindow(),
            ["daily_note"] = () => new DailyNoteWindow(),
            ["bbs"] = () => new BBSWindow(),
            ["video_resources"] = () => new VideoResourcesWindow(),
            ["community"] = () => new CommunityWindow(),
            ["miyoushe"] = () =>
            {
                MiyousheWindow.Show();
                return null;
            }
        };

    private async Task<McpToolResult> OpenPageAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = RequiredString(arguments, "page").ToLowerInvariant();

        if (!PageTags.TryGetValue(page, out var tag))
        {
            var availablePages = string.Join(", ", PageTags.Keys);
            throw new McpToolException($"Unknown page: {page}. Available pages: {availablePages}");
        }

        await McpUi.InvokeAsync(async () =>
        {
            if (App.MainWindow is not MainWindow mainWindow)
            {
                throw new McpToolException("Launcher main window is not ready yet.");
            }

            if (!mainWindow.CanNavigateToPage(tag))
            {
                throw new McpToolException($"Page \"{page}\" is hidden in the launcher navigation.");
            }

            try
            {
                mainWindow.Activate();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }

            mainWindow.NavigateToPage(tag);
            await Task.Delay(400);
            return true;
        }, 15000);

        return McpToolResult.Success(new
        {
            page,
            viewModel = tag,
            navigated = true
        });
    }

    private async Task<McpToolResult> OpenToolWindowAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tool = RequiredString(arguments, "tool").ToLowerInvariant();

        if (!ToolWindowFactories.TryGetValue(tool, out var factory))
        {
            var availableTools = string.Join(", ", ToolWindowFactories.Keys);
            throw new McpToolException($"Unknown tool window: {tool}. Available tools: {availableTools}");
        }

        string title;
        try
        {
            title = await McpUi.InvokeAsync(async () =>
            {
                var window = factory();
                if (window == null)
                {
                    await Task.Delay(300);
                    return "Miyoushe";
                }

                window.Activate();
                await Task.Delay(200);
                return string.IsNullOrWhiteSpace(window.Title) ? tool : window.Title;
            }, 20000);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException($"Failed to open tool window \"{tool}\": {ex.Message}");
        }

        return McpToolResult.Success(new
        {
            tool,
            title,
            opened = true
        });
    }

    private async Task<McpToolResult> SetWindowStateAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = RequiredString(arguments, "state").ToLowerInvariant();

        if (state is not ("maximize" or "minimize" or "restore"))
        {
            throw new McpToolException("state must be maximize, minimize or restore.");
        }

        var presenterState = await McpUi.InvokeAsync(async () =>
        {
            var window = App.MainWindow ?? throw new McpToolException("Launcher main window is not ready yet.");
            var appWindow = window.AppWindow;
            if (appWindow.Presenter is not OverlappedPresenter presenter)
            {
                throw new McpToolException("The current window does not support maximize or minimize.");
            }

            switch (state)
            {
                case "maximize":
                    if (!presenter.IsMaximizable)
                    {
                        throw new McpToolException("The launcher window cannot be maximized right now.");
                    }

                    presenter.Maximize();
                    break;

                case "minimize":
                    if (!presenter.IsMinimizable)
                    {
                        throw new McpToolException("The launcher window cannot be minimized right now.");
                    }

                    presenter.Minimize();
                    break;

                default:
                    presenter.Restore();
                    break;
            }

            await Task.Delay(150);
            return presenter.State.ToString();
        }, 10000);

        return McpToolResult.Success(new
        {
            state,
            presenterState
        });
    }

    private static Task<McpToolResult> ReadCrashLogAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var maxLines = OptionalInt(arguments, "maxLines", 120, 1, 500);
        var path = Path.Combine(AppPaths.RootDir, "CrashLog.txt");

        if (!File.Exists(path))
        {
            return Task.FromResult(McpToolResult.Success(new
            {
                exists = false,
                path,
                lines = Array.Empty<string>(),
                truncated = false,
                message = "No crash log has been created yet."
            }));
        }

        try
        {
            var info = new FileInfo(path);
            var (lines, truncated) = ReadTailLines(path, maxLines, 512 * 1024);
            return Task.FromResult(McpToolResult.Success(new
            {
                exists = true,
                path,
                sizeBytes = info.Length,
                lastModified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                truncated,
                lines
            }));
        }
        catch (Exception ex)
        {
            throw new McpToolException($"Failed to read the crash log: {ex.Message}");
        }
    }

    private async Task<McpToolResult> CheckUpdateAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var probe = await _updateService.ProbeUpdateAsync(cancellationToken).ConfigureAwait(false);

        return McpToolResult.Success(new
        {
            succeeded = probe.Succeeded,
            currentVersion = probe.CurrentVersion,
            serverVersion = probe.ServerVersion,
            previewVersion = probe.PreviewVersion,
            hasUpdate = probe.HasUpdate,
            hasPreviewUpdate = probe.HasPreviewUpdate,
            isDevBuild = probe.IsDevBuild,
            isPreviewBuild = probe.IsPreviewBuild,
            updateInfoUrl = probe.UpdateInfoUrl,
            previewUpdateInfoUrl = probe.PreviewUpdateInfoUrl,
            updateStarted = false,
            message = probe.Message
        });
    }

    private static (List<string> Lines, bool Truncated) ReadTailLines(string path, int maxLines, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var start = Math.Max(0, length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);

        var buffer = new byte[length - start];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer, read, buffer.Length - read);
            if (count <= 0)
            {
                break;
            }

            read += count;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        var truncated = start > 0;

        if (start > 0)
        {
            var firstNewLine = text.IndexOf('\n');
            if (firstNewLine >= 0 && firstNewLine + 1 < text.Length)
            {
                text = text[(firstNewLine + 1)..];
            }
        }

        var lines = text.Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count > maxLines)
        {
            lines = lines[^maxLines..];
            truncated = true;
        }

        return (lines, truncated);
    }
}