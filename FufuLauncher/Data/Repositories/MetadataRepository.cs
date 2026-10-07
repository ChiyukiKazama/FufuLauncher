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

public class MetadataRepository
{
    private string DbPath => AppPaths.MetadataDb;

    public MetadataRepository()
    {
    }

    private static readonly object _migrateLock = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _migratedPaths
        = new(StringComparer.OrdinalIgnoreCase);

    private MetadataDbContext CreateContext()
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

        return new MetadataDbContext(dbPath);
    }

    private void PerformMigration(string dbPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            bool tableExists = false;
            try
            {
                using var checkConn = new SqliteConnection(SqlitePaths.BuildConnectionString(dbPath));
                checkConn.Open();
                using var checkCmd = checkConn.CreateCommand();
                checkCmd.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Metadata';";
                tableExists = (long)checkCmd.ExecuteScalar()! > 0;
            }
            catch
            {
            }

            if (tableExists)
            {
                using var context = new MetadataDbContext(dbPath);
                context.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
                Debug.WriteLine("MetadataRepository: 检测到现有数据库，已跳过迁移");
            }
            else
            {
                using var context = new MetadataDbContext(dbPath);
                context.Database.EnsureCreated();
                Debug.WriteLine("MetadataRepository: 已创建新数据库");
            }

            EnsureGachaPoolMetadataSchema(dbPath);
            using (var context = new MetadataDbContext(dbPath))
            {
                context.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20261008000000_AddGachaPoolPresentation', '8.0.28');");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MetadataRepository: 数据库迁移处理异常 - {ex.Message}");

            try
            {
                EnsureGachaPoolMetadataSchema(dbPath);
                using var context = new MetadataDbContext(dbPath);
                context.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20261008000000_AddGachaPoolPresentation', '8.0.28');");
            }
            catch (Exception ex2)
            {
                Debug.WriteLine($"MetadataRepository: 迁移历史回退创建失败 - {ex2.Message}");
            }
        }
    }

    private static void EnsureGachaPoolMetadataSchema(string dbPath)
    {
        using var connection = new SqliteConnection(SqlitePaths.BuildConnectionString(dbPath));
        connection.Open();

        using (var createCommand = connection.CreateCommand())
        {
            createCommand.CommandText =
                """
                CREATE TABLE IF NOT EXISTS "GachaPoolMetadata" (
                    "Version" TEXT NOT NULL,
                    "PoolType" TEXT NOT NULL,
                    "PoolName" TEXT NOT NULL DEFAULT '',
                    "BannerImageUrl" TEXT NOT NULL DEFAULT '',
                    "StartTime" TEXT NOT NULL,
                    "EndTime" TEXT NOT NULL,
                    "UpItems" TEXT NOT NULL,
                    "UpItemNames" TEXT NOT NULL DEFAULT '[]',
                    CONSTRAINT "PK_GachaPoolMetadata" PRIMARY KEY ("Version", "PoolType")
                );
                """;
            createCommand.ExecuteNonQuery();
        }

        EnsureColumn(connection, "PoolName");
        EnsureColumn(connection, "BannerImageUrl");
    }

    private static void EnsureColumn(SqliteConnection connection, string columnName)
    {
        var exists = false;
        using (var inspectCommand = connection.CreateCommand())
        {
            inspectCommand.CommandText = "PRAGMA table_info(\"GachaPoolMetadata\");";
            using var reader = inspectCommand.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists) return;

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText =
            $"ALTER TABLE \"GachaPoolMetadata\" ADD COLUMN \"{columnName}\" TEXT NOT NULL DEFAULT '';";
        alterCommand.ExecuteNonQuery();
    }

    // ---- Metadata (scraped items) ----

    public List<MetadataEntity> GetAllMetadata()
    {
        try
        {
            using var context = CreateContext();
            return context.Metadata.ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MetadataRepo] 加载元数据失败: {ex.Message}");
            return new List<MetadataEntity>();
        }
    }

    public void UpsertMetadata(List<MetadataEntity> items)
    {
        using var context = CreateContext();
        foreach (var item in items)
        {
            var existing = context.Metadata.Find(item.Name);
            if (existing != null)
            {
                existing.ImgSrc = item.ImgSrc;
                existing.ElementSrc = item.ElementSrc;
                existing.Type = item.Type;
                existing.Rank = item.Rank;
                existing.ItemId = item.ItemId;
            }
            else
            {
                context.Metadata.Add(item);
            }
        }

        context.SaveChanges();
    }

    // ---- GachaLogs ----

    public List<string> GetDistinctUids()
    {
        try
        {
            using var context = CreateContext();
            return context.GachaLogs
                .Select(l => l.Uid)
                .Distinct()
                .OrderBy(u => u)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public List<GachaLogEntity> GetGachaLogsByUid(string uid)
    {
        try
        {
            using var context = CreateContext();
            return context.GachaLogs
                .Where(l => l.Uid == uid)
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MetadataRepo] 加载抽卡日志失败: {ex.Message}");
            return new List<GachaLogEntity>();
        }
    }

    public void ReplaceGachaLogs(string uid, List<GachaLogEntity> logs)
    {
        using var context = CreateContext();
        var existing = context.GachaLogs.Where(l => l.Uid == uid);
        context.GachaLogs.RemoveRange(existing);
        foreach (var log in logs)
        {
            log.Uid = uid;
            context.GachaLogs.Add(log);
        }

        context.SaveChanges();
    }

    public void InsertOrIgnoreGachaLogs(List<GachaLogEntity> logs)
    {
        using var context = CreateContext();
        var existingIds = context.GachaLogs
            .Where(l => logs.Select(x => x.Id).Contains(l.Id))
            .Select(l => l.Id)
            .ToHashSet();

        foreach (var log in logs)
        {
            if (!existingIds.Contains(log.Id))
            {
                context.GachaLogs.Add(log);
            }
        }

        context.SaveChanges();
    }

    public void DeleteGachaLogsByUid(string uid)
    {
        using var context = CreateContext();
        var logs = context.GachaLogs.Where(l => l.Uid == uid);
        context.GachaLogs.RemoveRange(logs);
        context.SaveChanges();
    }

    // ---- GachaPoolMetadata ----

    public bool HasPoolMetadata()
    {
        try
        {
            using var context = CreateContext();
            return context.GachaPoolMetadata.Any(pool => pool.BannerImageUrl != string.Empty);
        }
        catch
        {
            return false;
        }
    }

    public List<GachaPoolMetadataEntity> GetPoolMetadataByType(string poolType)
    {
        try
        {
            using var context = CreateContext();
            return context.GachaPoolMetadata
                .Where(p => p.PoolType == poolType)
                .OrderByDescending(p => p.StartTime)
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MetadataRepo] 加载卡池元数据失败: {ex.Message}");
            return new List<GachaPoolMetadataEntity>();
        }
    }

    public void ReplacePoolMetadata(string poolType, List<GachaPoolMetadataEntity> pools)
    {
        using var context = CreateContext();
        using var transaction = context.Database.BeginTransaction();
        context.GachaPoolMetadata
            .Where(pool => pool.PoolType == poolType)
            .ExecuteDelete();

        foreach (var pool in pools)
        {
            pool.PoolType = poolType;
            context.GachaPoolMetadata.Add(pool);
        }

        context.SaveChanges();
        transaction.Commit();
    }
}
