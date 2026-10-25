/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public sealed record PluginDllCandidate(string FilePath, DateTime LastWriteTime);

public sealed record PluginDllConflict(string DllName, IReadOnlyList<PluginDllCandidate> Candidates);

public sealed record PluginConflictCheckOptions(bool CheckEnabled, bool MainDllOnly);

public static class PluginInjectionGuard
{
    private static readonly string[] StrayFilePatterns = { "*.dll", "*.ini", "*.config" };

    public static IReadOnlyList<string> QuarantineRootStrayFiles()
    {
        var pluginsRoot = LightweightPluginService.PluginsDir;
        if (!Directory.Exists(pluginsRoot)) return Array.Empty<string>();

        var quarantined = new List<string>();
        var suffix = "." + Guid.NewGuid().ToString("N")[..8];

        foreach (var pattern in StrayFilePatterns)
        {
            foreach (var file in Directory.EnumerateFiles(pluginsRoot, pattern, SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(file), "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    var target = file + suffix;
                    File.Move(file, target);
                    quarantined.Add(target);
                }
                catch (Exception ex)
                {
                    DebugLog.Info("PluginInjectionGuard",
                        $"[PluginInjectionGuard] Failed to rename leftover file in plugin root directory {file}: {ex.Message}");
                }
            }
        }

        return quarantined;
    }

    public static IReadOnlyList<PluginDllConflict> FindDuplicateDllNames(PluginConflictCheckOptions options)
    {
        if (!options.CheckEnabled) return Array.Empty<PluginDllConflict>();

        var pluginsRoot = LightweightPluginService.PluginsDir;
        if (!Directory.Exists(pluginsRoot)) return Array.Empty<PluginDllConflict>();

        try
        {
            return Directory.EnumerateFiles(pluginsRoot, "*.dll", SearchOption.AllDirectories)
                .Where(IsActiveDll)
                .Where(path => !options.MainDllOnly || IsMainPluginDll(path))
                .Where(IsPluginDll)
                .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => new PluginDllConflict(
                    group.Key!,
                    group.Select(ToCandidate)
                        .OrderByDescending(candidate => candidate.LastWriteTime)
                        .ToList()))
                .OrderByDescending(conflict => conflict.Candidates[0].LastWriteTime)
                .ToList();
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginInjectionGuard",
                $"[PluginInjectionGuard] Failed to scan plugin directory: {ex.Message}");
            return Array.Empty<PluginDllConflict>();
        }
    }

    public static bool TryRemovePlugin(string dllPath, out string errorMessage)
    {
        errorMessage = string.Empty;

        var directory = Path.GetDirectoryName(dllPath);
        var configPath = string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, "config.ini");

        try
        {
            if (File.Exists(dllPath)) File.Delete(dllPath);
            if (configPath != null && File.Exists(configPath)) File.Delete(configPath);

            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory) &&
                !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }

            return true;
        }
        catch (Exception ex)
        {
            var lockedFile = FileLockHelper.FindLockedFile(dllPath, configPath);
            errorMessage = lockedFile != null ? FileLockHelper.GetLockedFileMessage(lockedFile) : ex.Message;
            DebugLog.Info("PluginInjectionGuard",
                $"[PluginInjectionGuard] Failed to delete plugin {dllPath}: {ex.Message}");
            return false;
        }
    }

    public static string BuildConflictReport(IReadOnlyList<PluginDllConflict> conflicts)
    {
        var report = new StringBuilder();
        report.AppendLine("PluginDllConflict_Message".GetLocalized());
        report.AppendLine();

        foreach (var conflict in conflicts)
        {
            report.AppendLine(string.Format("PluginDllConflict_DllNameFormat".GetLocalized(), conflict.DllName));

            for (var i = 0; i < conflict.Candidates.Count; i++)
            {
                var candidate = conflict.Candidates[i];
                var badge = (i == 0 ? "PluginDllConflict_NewestBadge" : "PluginDllConflict_OlderBadge").GetLocalized();
                report.AppendLine($"  [{badge}] {candidate.FilePath}    {candidate.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            }

            report.AppendLine();
        }

        report.Append("PluginDllConflict_Hint".GetLocalized());
        return report.ToString();
    }

    private static bool IsActiveDll(string path) =>
        string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase);

    private static bool IsMainPluginDll(string path) =>
        string.Equals(Path.GetFileName(path), LightweightPluginService.MainPluginDllName,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsPluginDll(string dllPath)
    {
        var directory = Path.GetDirectoryName(dllPath);
        if (string.IsNullOrEmpty(directory)) return false;

        var configPath = Path.Combine(directory, "config.ini");
        if (!File.Exists(configPath)) return false;

        try
        {
            var config = new IniFile(configPath).ReadAll();
            if (!config.TryGetValue("General", out var general)) return false;
            if (!general.TryGetValue("File", out var fileName) || string.IsNullOrWhiteSpace(fileName)) return false;

            return string.Equals(Path.GetFileName(fileName.Trim()), Path.GetFileName(dllPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginInjectionGuard",
                $"[PluginInjectionGuard] Failed to read plugin config {configPath}: {ex.Message}");
            return false;
        }
    }

    private static PluginDllCandidate ToCandidate(string path)
    {
        var info = new FileInfo(path);
        return new PluginDllCandidate(path, info.LastWriteTime);
    }
}