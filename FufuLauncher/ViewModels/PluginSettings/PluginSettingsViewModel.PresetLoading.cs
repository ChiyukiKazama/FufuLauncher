using System.Security.Cryptography;
using System.Text.Json;
using FufuLauncher.Messages;
using FufuLauncher.Helpers;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public partial class PluginSettingsViewModel
{
    private static readonly SemaphoreSlim ConfigurationPreparationGate = new(1, 1);

    private static PresetLoadResult PreparePresetState(ConfigurationLoadRequest request,
        Dictionary<string, Dictionary<string, string>> currentData, CancellationToken cancellationToken)
    {
        var presets = new List<PresetModel>();
        var notifications = new List<NotificationMessage>();
        var directory = request.PresetsDirectory;
        var includeLegacyMain = request.PluginIndex == 0 && !request.LightweightMode;
        var pluginFolder = request.PluginIndex == 1 ? "FPS" : request.LightweightMode
            ? LightweightPluginService.LitePluginFolderName : LightweightPluginService.MainPluginFolderName;
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (UnauthorizedAccessException)
        {
            directory = PluginPresetStorage.GetFallbackDirectory(pluginFolder);
            Directory.CreateDirectory(directory);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var hash = string.Empty;
        if (File.Exists(request.DllPath))
        {
            try
            {
                using var stream = File.OpenRead(request.DllPath);
                using var algorithm = SHA256.Create();
                var buffer = new byte[81920];
                int length;
                while ((length = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    algorithm.TransformBlock(buffer, 0, length, null, 0);
                }
                algorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                hash = Convert.ToHexString(algorithm.Hash!).ToLowerInvariant();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        var stateFile = Path.Combine(directory, "active_state.json");
        var activeId = PluginPresetStorage.ReadActivePresetId(directory, includeLegacyMain);
        var loadedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        PresetModel? active = null;
        foreach (var file in PluginPresetStorage.EnumerateFiles(directory, includeLegacyMain))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetFileName(file).Equals("active_state.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var preset = JsonSerializer.Deserialize<PresetModel>(File.ReadAllText(file));
                if (preset == null || string.IsNullOrWhiteSpace(preset.Id) || preset.ConfigData == null ||
                    !loadedIds.Add(preset.Id))
                {
                    continue;
                }

                preset.FilePath = file;
                preset.ConfigData = new Dictionary<string, Dictionary<string, string>>(
                    preset.ConfigData, StringComparer.OrdinalIgnoreCase);
                var modified = preset.ConfigData.Remove("General");
                foreach (var sectionKey in preset.ConfigData.Keys.ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (preset.ConfigData[sectionKey] == null)
                    {
                        preset.ConfigData.Remove(sectionKey);
                        modified = true;
                        continue;
                    }

                    if (currentData.TryGetValue(sectionKey, out var section) &&
                        preset.ConfigData[sectionKey].GetValueOrDefault("Name") != section.GetValueOrDefault("Name"))
                    {
                        preset.ConfigData[sectionKey] = new Dictionary<string, string>(section, StringComparer.OrdinalIgnoreCase);
                        modified = true;
                    }
                }

                if (preset.DllHash != hash)
                {
                    preset.IsLocked = request.AutoCreatePresets;
                    if (!preset.IsLocked)
                    {
                        preset.DllHash = hash;
                        modified = true;
                    }
                }
                else
                {
                    preset.IsLocked = false;
                }

                if (modified && !preset.IsLocked)
                {
                    WritePreparedPreset(preset, notifications, cancellationToken);
                }
                presets.Add(preset);
                if (preset.Id == activeId)
                {
                    active = preset;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PluginSettings] Preset read failed: {file}: {ex}");
            }
        }

        if (active?.IsLocked == true)
        {
            notifications.Add(new NotificationMessage("插件变更", "当前预设与最新插件版本不匹配，已自动生成新预设",
                NotificationType.Warning, 5000));
            active = null;
        }

        if (active == null)
        {
            var data = currentData.Where(pair => !pair.Key.Equals("General", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key,
                    pair => new Dictionary<string, string>(pair.Value, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            active = new PresetModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = "默认预设",
                DllHash = hash,
                ConfigData = data
            };
            active.FilePath = Path.Combine(directory, $"{active.Id}.json");
            WritePreparedPreset(active, notifications, cancellationToken);
            presets.Add(active);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            File.WriteAllText(stateFile, JsonSerializer.Serialize(new Dictionary<string, string> { ["ActiveId"] = active.Id }));
        }
        catch (Exception ex)
        {
            notifications.Add(new NotificationMessage("状态保存失败", $"无法保存激活状态记录，可能缺少写入权限\n详细信息: {ex.Message}",
                NotificationType.Error, 6000));
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var updates = new Dictionary<string, Dictionary<string, string>>(active.ConfigData, StringComparer.OrdinalIgnoreCase);
            updates.Remove("General");
            request.IniFile.UpdateMultiple(updates, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            notifications.Add(new NotificationMessage("配置应用失败", $"无法将预设写入配置文件，请检查权限\n详细信息: {ex.Message}",
                NotificationType.Error, 6000));
        }

        return new PresetLoadResult(directory, presets, active, notifications);
    }

    private static void WritePreparedPreset(PresetModel preset, List<NotificationMessage> notifications,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var content = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(preset.FilePath, content);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            notifications.Add(new NotificationMessage("预设保存失败", $"无法保存预设文件，可能缺少写入权限\n详细信息: {ex.Message}",
                NotificationType.Error, 6000));
        }
    }

    private sealed record PresetLoadResult(string Directory, IReadOnlyList<PresetModel> Presets,
        PresetModel CurrentPreset, IReadOnlyList<NotificationMessage> Notifications);
}