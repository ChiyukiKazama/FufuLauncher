/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Diagnostics;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Services.GameServer;

namespace FufuLauncher.Services;

public sealed record GameSessionSnapshot(
    bool Configured,
    string GamePath,
    bool Installed,
    bool Running,
    GameServerScheme? Server,
    string? GameVersion);

public sealed class GameSessionService
{
    private readonly IGameLauncherService _gameLauncher;
    private readonly GameServerConfigurationService _serverConfiguration;

    public GameSessionService(IGameLauncherService gameLauncher,
        GameServerConfigurationService serverConfiguration)
    {
        _gameLauncher = gameLauncher;
        _serverConfiguration = serverConfiguration;
    }

    public Task<string> GetGamePathAsync()
    {
        try
        {
            return Task.FromResult(_gameLauncher.GetGamePath().Trim().Trim('"').Trim());
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameSessionService", $"[GameSession] 读取游戏路径失败：{ex.Message}");
            return Task.FromResult(string.Empty);
        }
    }

    public async Task<GameSessionSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var gamePath = await GetGamePathAsync().ConfigureAwait(false);
        var installed = !string.IsNullOrWhiteSpace(gamePath) && Directory.Exists(gamePath);
        var running = await GameProcessHelper.IsGameRunningAsync().ConfigureAwait(false);

        GameServerScheme? scheme = null;
        string? version = null;
        if (installed)
        {
            try
            {
                scheme = _serverConfiguration.TryDetectCurrentScheme(gamePath);
                version = _serverConfiguration.TryGetGameVersion(gamePath);
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameSessionService", $"[GameSession] 读取服务器方案失败：{ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new GameSessionSnapshot(!string.IsNullOrWhiteSpace(gamePath), gamePath, installed, running, scheme,
            version);
    }

    public async Task<int> StopGameAsync(CancellationToken cancellationToken = default)
    {
        var gamePath = await GetGamePathAsync().ConfigureAwait(false);
        var exeNames = await GameExeManager.GetExeNamesAsync().ConfigureAwait(false);
        var processNames = exeNames
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var processes = new List<Process>();
        foreach (var name in processNames)
        {
            processes.AddRange(Process.GetProcessesByName(name!));
        }

        if (processes.Count == 0)
        {
            return 0;
        }

        var stopped = 0;
        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(gamePath))
                {
                    try
                    {
                        var processPath = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(processPath) &&
                            !processPath.StartsWith(gamePath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                    catch (Win32Exception)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }
                }

                process.Kill();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                stopped++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameSessionService", $"[GameSession] 结束游戏进程失败：{ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        try
        {
            await _gameLauncher.StopBetterGIAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameSessionService", $"[GameSession] 结束 BetterGI 失败：{ex.Message}");
        }

        return stopped;
    }
}