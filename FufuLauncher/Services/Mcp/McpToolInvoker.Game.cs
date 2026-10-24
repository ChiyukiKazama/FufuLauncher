/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Services.GameServer;

namespace FufuLauncher.Services.Mcp;

public sealed partial class McpToolInvoker
{
    private async Task<McpToolResult> GetGameStatusAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _gameSession.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var exeNames = await GameExeManager.GetExeNamesAsync().ConfigureAwait(false);

        return McpToolResult.Success(new
        {
            configured = snapshot.Configured,
            gamePath = snapshot.GamePath,
            installed = snapshot.Installed,
            running = snapshot.Running,
            gameVersion = snapshot.GameVersion,
            server = snapshot.Server == null ? null : DescribeScheme(snapshot.Server, true),
            exeNames,
            launcherVersion = AppVersionHelper.FullVersion
        });
    }

    private async Task<McpToolResult> StartGameAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _gameSession.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Installed)
        {
            throw new McpToolException("No valid game path is configured. Set the game path in the launcher first.");
        }

        if (snapshot.Running)
        {
            throw new McpToolException("The game is already running.");
        }

        LaunchResult result;
        try
        {
            result = await _gameLauncher.LaunchGameAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException($"Failed to start the game: {ex.Message}");
        }

        if (!result.Success)
        {
            throw new McpToolException(string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "Failed to start the game."
                : result.ErrorMessage);
        }

        return McpToolResult.Success(new
        {
            started = true,
            gamePath = snapshot.GamePath,
            detailLog = Tail(result.DetailLog, 4000)
        });
    }

    private async Task<McpToolResult> StopGameAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _gameSession.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Running)
        {
            return McpToolResult.Success(new
            {
                stopped = false,
                terminatedProcesses = 0,
                message = "The game is not running."
            });
        }

        var stopped = await _gameSession.StopGameAsync(cancellationToken).ConfigureAwait(false);
        return McpToolResult.Success(new
        {
            stopped = stopped > 0,
            terminatedProcesses = stopped
        });
    }

    private async Task<McpToolResult> ListServersAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _gameSession.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var clientIsOversea = ResolveClientIsOversea(snapshot);

        return McpToolResult.Success(new
        {
            installed = snapshot.Installed,
            running = snapshot.Running,
            current = snapshot.Server == null ? null : DescribeScheme(snapshot.Server, true),
            client = snapshot.Installed
                ? clientIsOversea switch
                {
                    true => "os",
                    false => "cn",
                    null => "unknown"
                }
                : null,
            servers = GameServerScheme.Selectable
                .Select(scheme => DescribeScheme(scheme,
                    snapshot.Server != null && snapshot.Server.Equals(scheme),
                    clientIsOversea))
                .ToList()
        });
    }

    private async Task<McpToolResult> SwitchServerAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var serverId = RequiredString(arguments, "serverId");
        var snapshot = await _gameSession.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (!snapshot.Installed)
        {
            throw new McpToolException("No valid game path is configured; cannot switch servers.");
        }

        if (snapshot.Running)
        {
            throw new McpToolException("The game is running; stop it before switching servers.");
        }

        var scheme = GameServerScheme.Selectable.FirstOrDefault(candidate =>
            string.Equals(BuildServerId(candidate), serverId, StringComparison.OrdinalIgnoreCase));

        if (scheme == null)
        {
            throw new McpToolException(
                $"Unknown server scheme: {serverId}. Call list_servers to get valid server ids.");
        }

        var clientIsOversea = ResolveClientIsOversea(snapshot);
        if (clientIsOversea == null)
        {
            throw new McpToolException("Cannot determine whether the installed client is Chinese or oversea; switch cancelled.");
        }

        if (clientIsOversea != scheme.IsOversea)
        {
            throw new McpToolException(
                $"The target server does not match the installed client (current: {(clientIsOversea.Value ? "oversea" : "Chinese")}). Use the launcher advanced conversion feature to switch between Chinese and oversea clients.");
        }

        var previous = snapshot.Server == null ? null : DescribeScheme(snapshot.Server, true);
        try
        {
            await _channelSdk.SwitchSchemeAsync(snapshot.GamePath, scheme, null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException($"Server switch failed: {ex.Message}");
        }

        var updated = await _gameSession.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return McpToolResult.Success(new
        {
            switched = true,
            serverId = BuildServerId(scheme),
            previous,
            current = updated.Server == null ? null : DescribeScheme(updated.Server, true)
        });
    }

    private static object DescribeScheme(GameServerScheme scheme, bool isCurrent, bool? clientIsOversea = null) =>
        new
        {
            id = BuildServerId(scheme),
            displayName = scheme.DisplayName,
            channel = scheme.Channel.ToString(),
            channelValue = (int)scheme.Channel,
            subChannel = scheme.SubChannel.ToString(),
            subChannelValue = (int)scheme.SubChannel,
            isOversea = scheme.IsOversea,
            cps = scheme.Cps,
            current = isCurrent,
            compatibleWithClient = clientIsOversea == null || clientIsOversea == scheme.IsOversea
        };

    private static string BuildServerId(GameServerScheme scheme) =>
        $"{(int)scheme.Channel}-{(int)scheme.SubChannel}-{(scheme.IsOversea ? "os" : "cn")}";

    private static bool? ResolveClientIsOversea(GameSessionSnapshot snapshot)
    {
        if (!snapshot.Installed)
        {
            return null;
        }

        if (snapshot.Server != null)
        {
            return snapshot.Server.IsOversea;
        }

        var hasChinese = File.Exists(Path.Combine(snapshot.GamePath, GameConstants.CN_EXE));
        var hasOversea = File.Exists(Path.Combine(snapshot.GamePath, GameConstants.OS_EXE));
        if (hasChinese == hasOversea)
        {
            return null;
        }

        return hasOversea;
    }

    private static string Tail(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text ?? string.Empty;
        }

        return "..." + text[^maxLength..];
    }
}
