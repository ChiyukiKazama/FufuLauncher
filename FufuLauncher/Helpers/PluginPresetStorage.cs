using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Services;
using FufuLauncher.ViewModels;

namespace FufuLauncher.Helpers;

public static class PluginPresetStorage
{
    public static string GetDirectory(string pluginFolderName) =>
        ResolveDirectory(AppPaths.PluginPresetsDir, pluginFolderName);

    public static string GetFallbackDirectory(string pluginFolderName) =>
        ResolveDirectory(Path.Combine(AppPaths.RootDir, "Data", "PluginPresets"), pluginFolderName);

    private static string ResolveDirectory(string root, string pluginFolderName) =>
        pluginFolderName.Equals(LightweightPluginService.MainPluginFolderName, StringComparison.OrdinalIgnoreCase)
            ? root : Path.Combine(root, pluginFolderName);

    public static IEnumerable<string> EnumerateFiles(string directory, bool includeLegacyMain = true)
    {
        foreach (var candidate in GetReadDirectories(directory, includeLegacyMain))
        {
            if (!Directory.Exists(candidate)) continue;

            foreach (var file in Directory.EnumerateFiles(candidate, "*.json"))
            {
                if (!Path.GetFileName(file).Equals("active_state.json", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    public static IReadOnlyList<PresetModel> ReadPresets(string directory, bool includeLegacyMain = true)
    {
        var result = new List<PresetModel>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateFiles(directory, includeLegacyMain))
        {
            try
            {
                var preset = JsonSerializer.Deserialize<PresetModel>(File.ReadAllText(file));
                if (preset == null || string.IsNullOrWhiteSpace(preset.Id) || preset.ConfigData == null ||
                    !ids.Add(preset.Id)) continue;

                preset.FilePath = file;
                result.Add(preset);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginPresets] Preset read failed: {file}: {ex}");
            }
        }
        return result;
    }

    public static string ReadActivePresetId(string directory, bool includeLegacyMain = true)
    {
        foreach (var candidate in GetReadDirectories(directory, includeLegacyMain))
        {
            var file = Path.Combine(candidate, "active_state.json");
            if (!File.Exists(file)) continue;

            try
            {
                var state = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                if (state?.TryGetValue("ActiveId", out var id) == true && !string.IsNullOrWhiteSpace(id))
                {
                    return id;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginPresets] Active state read failed: {file}: {ex}");
            }
        }
        return string.Empty;
    }

    private static IEnumerable<string> GetReadDirectories(string directory, bool includeLegacyMain)
    {
        yield return directory;
        if (includeLegacyMain)
        {
            yield return Path.Combine(directory, LightweightPluginService.MainPluginFolderName);
        }
    }
}
