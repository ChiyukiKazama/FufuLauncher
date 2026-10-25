/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text;
using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public sealed class GamePlayTimeStore
{
    public const int CurrentSchemaVersion = 1;
    public const string StorageIdentifier = "fufu.game-play-time";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Dictionary<string, long> _dailyPlayTimeSeconds = new(StringComparer.Ordinal);
    private string? _lastPlayDate;
    private bool _hasVerifiedStorageIdentity;

    public IReadOnlyDictionary<string, long> GetDailyPlayTimeSnapshot()
    {
        lock (_sync)
        {
            return new Dictionary<string, long>(_dailyPlayTimeSeconds, StringComparer.Ordinal);
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _dailyPlayTimeSeconds.Clear();
            _lastPlayDate = null;
            _hasVerifiedStorageIdentity = false;
        }

        var storagePath = AppPaths.GamePlayTimeFile;
        if (File.Exists(storagePath))
        {
            if (await TryLoadAsync(storagePath, cancellationToken))
            {
                if (_hasVerifiedStorageIdentity)
                {
                    await RetireMigratedLegacyAsync(cancellationToken);
                }

                return;
            }

            BackupCorruptStorage(storagePath);
        }

        await MigrateLegacyAsync(cancellationToken);
    }

    public void AddPlayTime(DateTime localDate, long secondsToAdd)
    {
        if (secondsToAdd <= 0) return;

        var dateKey = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        lock (_sync)
        {
            _dailyPlayTimeSeconds.TryGetValue(dateKey, out var existingSeconds);
            _dailyPlayTimeSeconds[dateKey] = existingSeconds + secondsToAdd;
            _lastPlayDate = dateKey;
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            GamePlayTimeDocument document;
            lock (_sync)
            {
                document = new GamePlayTimeDocument
                {
                    SchemaVersion = CurrentSchemaVersion,
                    StorageKey = StorageIdentifier,
                    LastPlayDate = _lastPlayDate,
                    UpdatedAt = DateTimeOffset.Now,
                    DailyPlayTimeSeconds = new Dictionary<string, long>(_dailyPlayTimeSeconds,
                        StringComparer.Ordinal)
                };
            }

            var json = JsonSerializer.Serialize(document, JsonOptions);
            await WriteAtomicallyAsync(AppPaths.GamePlayTimeFile, json, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<bool> TryLoadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var document = JsonSerializer.Deserialize<GamePlayTimeDocument>(json, JsonOptions);
            if (document == null) return false;

            var dailySeconds = document.DailyPlayTimeSeconds;
            lock (_sync)
            {
                _dailyPlayTimeSeconds.Clear();
                if (dailySeconds != null)
                {
                    foreach (var entry in dailySeconds)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value < 0) continue;
                        _dailyPlayTimeSeconds[entry.Key] = entry.Value;
                    }
                }

                _lastPlayDate = document.LastPlayDate;
                _hasVerifiedStorageIdentity =
                    string.Equals(document.StorageKey, StorageIdentifier, StringComparison.Ordinal) &&
                    document.SchemaVersion == CurrentSchemaVersion;
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to load {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    private async Task MigrateLegacyAsync(CancellationToken cancellationToken)
    {
        var legacyPath = AppPaths.LegacyFufuConfigFile;
        if (!File.Exists(legacyPath)) return;

        LegacyGamePlayTimeConfig? legacy;
        try
        {
            var json = await File.ReadAllTextAsync(legacyPath, cancellationToken);
            legacy = JsonSerializer.Deserialize<LegacyGamePlayTimeConfig>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to read legacy {Path.GetFileName(legacyPath)}: {ex.Message}");
            return;
        }

        if (legacy == null) return;

        lock (_sync)
        {
            if (legacy.GamePlayTimeData != null)
            {
                foreach (var entry in legacy.GamePlayTimeData)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value < 0) continue;
                    _dailyPlayTimeSeconds[entry.Key] = entry.Value;
                }
            }

            _lastPlayDate = string.IsNullOrWhiteSpace(legacy.LastPlayDate) ? null : legacy.LastPlayDate;
        }

        try
        {
            await SaveAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to write migrated game time data; legacy config retained: {ex.Message}");
            return;
        }

        if (!await VerifyMigratedStorageAsync(cancellationToken))
        {
            DebugLog.Info("GamePlayTimeStore",
                "Migrated game time storage verification failed; legacy config retained");
            return;
        }

        TryDeleteLegacyConfig(legacyPath);
        DebugLog.Info("GamePlayTimeStore",
            $"Migrated legacy game time data to {Path.GetFileName(AppPaths.GamePlayTimeFile)}");
    }

    private async Task RetireMigratedLegacyAsync(CancellationToken cancellationToken)
    {
        var legacyPath = AppPaths.LegacyFufuConfigFile;
        if (!File.Exists(legacyPath)) return;

        LegacyGamePlayTimeConfig? legacy;
        try
        {
            var json = await File.ReadAllTextAsync(legacyPath, cancellationToken);
            legacy = JsonSerializer.Deserialize<LegacyGamePlayTimeConfig>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to read legacy {Path.GetFileName(legacyPath)}; legacy config retained: {ex.Message}");
            return;
        }

        if (legacy == null) return;

        lock (_sync)
        {
            if (legacy.GamePlayTimeData != null)
            {
                foreach (var entry in legacy.GamePlayTimeData)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value <= 0) continue;
                    if (!_dailyPlayTimeSeconds.TryGetValue(entry.Key, out var migratedSeconds) ||
                        migratedSeconds < entry.Value)
                    {
                        DebugLog.Info("GamePlayTimeStore",
                            "Legacy game time config retained because the new storage does not contain all legacy data");
                        return;
                    }
                }
            }
        }

        TryDeleteLegacyConfig(legacyPath);
        DebugLog.Info("GamePlayTimeStore",
            "Retired legacy game time config after confirming the new storage contains its data");
    }

    private async Task<bool> VerifyMigratedStorageAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(AppPaths.GamePlayTimeFile, cancellationToken);
            var document = JsonSerializer.Deserialize<GamePlayTimeDocument>(json, JsonOptions);
            if (document == null) return false;
            if (!string.Equals(document.StorageKey, StorageIdentifier, StringComparison.Ordinal)) return false;
            if (document.SchemaVersion != CurrentSchemaVersion) return false;

            var persistedDailySeconds = document.DailyPlayTimeSeconds;
            if (persistedDailySeconds == null) return false;

            lock (_sync)
            {
                if (!string.Equals(document.LastPlayDate, _lastPlayDate, StringComparison.Ordinal)) return false;
                if (persistedDailySeconds.Count != _dailyPlayTimeSeconds.Count) return false;

                foreach (var entry in _dailyPlayTimeSeconds)
                {
                    if (!persistedDailySeconds.TryGetValue(entry.Key, out var persistedSeconds) ||
                        persistedSeconds != entry.Value)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to verify migrated game time storage: {ex.Message}");
            return false;
        }
    }

    private static void TryDeleteLegacyConfig(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Delete(path);
            DebugLog.Info("GamePlayTimeStore",
                $"Deleted legacy game time config after successful migration: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to delete legacy game time config; file retained: {ex.Message}");
        }
    }

    private static async Task WriteAtomicallyAsync(string path, string json,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    private static void BackupCorruptStorage(string path)
    {
        try
        {
            var backupPath = $"{path}.corrupt.{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Copy(path, backupPath, overwrite: true);
            DebugLog.Info("GamePlayTimeStore", $"Backed up corrupt game time storage to {backupPath}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("GamePlayTimeStore", $"Failed to back up corrupt game time storage: {ex.Message}");
        }
    }
}

internal sealed class GamePlayTimeDocument
{
    public int SchemaVersion
    {
        get;
        set;
    }

    public string StorageKey
    {
        get;
        set;
    } = string.Empty;

    public string? LastPlayDate
    {
        get;
        set;
    }

    public DateTimeOffset UpdatedAt
    {
        get;
        set;
    }

    public Dictionary<string, long> DailyPlayTimeSeconds
    {
        get;
        set;
    } = new();
}

internal sealed class LegacyGamePlayTimeConfig
{
    public Dictionary<string, long>? GamePlayTimeData
    {
        get;
        set;
    }

    public string? LastPlayDate
    {
        get;
        set;
    }
}