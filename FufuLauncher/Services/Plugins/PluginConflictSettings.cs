/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public static class PluginConflictSettings
{
    public const string CheckEnabledKey = "PluginConflictCheckEnabled";
    public const string MainDllOnlyKey = "PluginConflictMainDllOnly";

    public static PluginConflictCheckOptions Read()
    {
        var checkEnabled = true;
        var mainDllOnly = true;

        try
        {
            var dbPath = GetSettingsDbPath();
            if (File.Exists(dbPath))
            {
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString());

                connection.Open();

                checkEnabled = ReadBool(connection, CheckEnabledKey, true);
                mainDllOnly = ReadBool(connection, MainDllOnlyKey, true);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginConflictSettings",
                $"[PluginConflictSettings] Failed to read plugin conflict detection settings: {ex.Message}");
        }

        return new PluginConflictCheckOptions(checkEnabled, mainDllOnly);
    }

    private static bool ReadBool(SqliteConnection connection, string key, bool defaultValue)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT \"Value\" FROM \"Settings\" WHERE \"Key\" = $key LIMIT 1;";
            command.Parameters.AddWithValue("$key", key);

            if (command.ExecuteScalar() is not string raw) return defaultValue;

            var value = raw.Trim();

            if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1") return true;
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0") return false;

            return defaultValue;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginConflictSettings",
                $"[PluginConflictSettings] Failed to read setting {key}: {ex.Message}");
            return defaultValue;
        }
    }

    private static string GetSettingsDbPath()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FufuLauncher");
        var dataDir = Path.Combine(root, "Data");

        try
        {
            var pathsFile = Path.Combine(root, "Settings", "paths.json");
            if (File.Exists(pathsFile))
            {
                var config = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(pathsFile));
                if (config != null && config.TryGetValue("DataDir", out var custom) &&
                    !string.IsNullOrWhiteSpace(custom))
                {
                    dataDir = custom;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginConflictSettings",
                $"[PluginConflictSettings] Failed to read custom data directory: {ex.Message}");
        }

        return Path.Combine(dataDir, "LocalSettings.db");
    }
}