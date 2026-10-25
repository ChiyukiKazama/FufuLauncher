/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Data.Entities;
using FufuLauncher.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FufuLauncher.Data.Repositories;

public class LocalSettingsRepository
{
    private string DbPath => AppPaths.LocalSettingsDb;

    public LocalSettingsRepository()
    {
    }

    private static readonly object _migrateLock = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _migratedPaths
        = new(StringComparer.OrdinalIgnoreCase);

    private LocalSettingsDbContext CreateContext()
    {
        var dbPath = DbPath;
        if (!_migratedPaths.ContainsKey(dbPath))
        {
            lock (_migrateLock)
            {
                if (!_migratedPaths.ContainsKey(dbPath))
                {
                    PerformMigration(dbPath);
                    _migratedPaths[dbPath] = true;
                }
            }
        }

        return new LocalSettingsDbContext(dbPath);
    }

    private static void EnsureSettingsTable(string dbPath)
    {
        using var conn = new SqliteConnection(SqlitePaths.BuildConnectionString(dbPath));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE IF NOT EXISTS \"Settings\" (\"Key\" TEXT NOT NULL PRIMARY KEY, \"Value\" TEXT);";
        cmd.ExecuteNonQuery();
    }

    private void PerformMigration(string dbPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);


            EnsureSettingsTable(dbPath);

            using var context = new LocalSettingsDbContext(dbPath);
            context.Database.ExecuteSqlRaw(
                "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
            context.Database.ExecuteSqlRaw(
                "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
            DebugLog.Info("LocalSettingsRepository", "LocalSettingsRepository: Database migration complete");
        }
        catch (Exception ex)
        {
            DebugLog.Info("LocalSettingsRepository",
                $"LocalSettingsRepository: Database migration exception - {ex.Message}");

            try
            {
                EnsureSettingsTable(dbPath);
                using var context = new LocalSettingsDbContext(dbPath);
                context.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
            }
            catch (Exception ex2)
            {
                DebugLog.Info("LocalSettingsRepository",
                    $"LocalSettingsRepository: Failed to create migration history fallback - {ex2.Message}");
            }
        }
    }

    public Dictionary<string, string> GetAllSettings()
    {
        try
        {
            using var context = CreateContext();
            return context.Settings.ToDictionary(s => s.Key, s => s.Value ?? string.Empty);
        }
        catch (Exception ex)
        {
            DebugLog.Info("LocalSettingsRepository",
                $"LocalSettingsRepository: Failed to load settings - {ex.Message}");
            return new Dictionary<string, string>();
        }
    }


    public async Task<(bool Success, bool Found, string Value)> TryGetSettingAsync(string key)
    {
        try
        {
            using var context = CreateContext();
            var entity = await context.Settings.FindAsync(key);
            return entity is null ? (true, false, string.Empty) : (true, true, entity.Value ?? string.Empty);
        }
        catch (Exception ex)
        {
            DebugLog.Info("LocalSettingsRepository", $"LocalSettingsRepository: Failed to read '{key}' - {ex.Message}");
            return (false, false, string.Empty);
        }
    }


    public async Task<(bool Success, Dictionary<string, string> Values)> TryGetAllSettingsAsync()
    {
        try
        {
            using var context = CreateContext();
            var settings = await context.Settings.ToListAsync();
            return (true, settings.ToDictionary(s => s.Key, s => s.Value ?? string.Empty));
        }
        catch (Exception ex)
        {
            DebugLog.Info("LocalSettingsRepository",
                $"LocalSettingsRepository: Failed to load all settings - {ex.Message}");
            return (false, new Dictionary<string, string>());
        }
    }

    public async Task UpsertSettingAsync(string key, string value)
    {
        using var context = CreateContext();
        var existing = await context.Settings.FindAsync(key);
        if (existing != null)
        {
            existing.Value = value;
        }
        else
        {
            context.Settings.Add(new SettingEntity { Key = key, Value = value });
        }

        await context.SaveChangesAsync();
        SettingsLog.Write($"LocalSettingsRepository: Saved '{key}'");
    }


    public async Task<bool> DeleteSettingAsync(string key)
    {
        try
        {
            using var context = CreateContext();
            var entity = await context.Settings.FindAsync(key);
            if (entity != null)
            {
                context.Settings.Remove(entity);
                await context.SaveChangesAsync();
                SettingsLog.Write($"LocalSettingsRepository: Deleted '{key}'");
            }

            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("LocalSettingsRepository",
                $"LocalSettingsRepository: Failed to delete setting - {ex.Message}");
            return false;
        }
    }

    public async Task<List<SettingEntity>> GetAllSettingEntitiesAsync()
    {
        try
        {
            using var context = CreateContext();
            return await context.Settings.ToListAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("LocalSettingsRepository",
                $"LocalSettingsRepository: Failed to load entities - {ex.Message}");
            return new List<SettingEntity>();
        }
    }

    public async Task ReplaceAllSettingsAsync(List<SettingEntity> settings)
    {
        using var context = CreateContext();
        context.Settings.RemoveRange(context.Settings);
        foreach (var setting in settings)
        {
            if (!string.IsNullOrWhiteSpace(setting.Key))
                context.Settings.Add(setting);
        }

        await context.SaveChangesAsync();
    }
}