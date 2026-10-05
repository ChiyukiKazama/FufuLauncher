/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using FufuLauncher.Contracts.Services;

namespace FufuLauncher.Services.UID;

public class UidLookupService : IUidLookupService
{
    private const string BeyondLocalRelativePath = @"AppData\LocalLow\miHoYo\原神\BeyondLocal";

    private const string PluginFolderName = "FuFuPlugin";
    private const string JsonFileName = "uids.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<IReadOnlyList<string>> LoadAndWriteUidsAsync()
    {
        var entries = ReadUidsFromBeyondLocal();
        if (entries.Count == 0) return Array.Empty<string>();
        await WriteUidsToPluginJsonAsync(entries);
        var uids = new string[entries.Count];
        for (var i = 0; i < entries.Count; i++) uids[i] = entries[i].Uid;
        return uids;
    }

    public static IReadOnlyList<string> ReadLocalUids() =>
        ReadUidsFromBeyondLocal().Select(entry => entry.Uid).ToArray();

    private static List<UidEntry> ReadUidsFromBeyondLocal()
    {
        var result = new List<UidEntry>();

        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(userProfile))
            {
                Debug.WriteLine("[UidLookupService] 无法获取用户目录");
                return result;
            }

            var beyondLocalDir = Path.Combine(userProfile, BeyondLocalRelativePath);
            if (!Directory.Exists(beyondLocalDir))
            {
                Debug.WriteLine($"[UidLookupService] 目录不存在: {beyondLocalDir}");
                return result;
            }

            foreach (var dir in Directory.EnumerateDirectories(beyondLocalDir))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name)) continue;
                if (!IsAllDigits(name)) continue;
                if (result.Any(e => string.Equals(e.Uid, name, StringComparison.Ordinal))) continue;

                result.Add(new UidEntry
                {
                    Uid = name,
                    UpdatedAt = Directory.GetLastWriteTime(dir)
                });
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Uid, b.Uid));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UidLookupService] 读取 UID 失败 - {ex.Message}");
        }

        return result;
    }

    private sealed class UidEntry
    {
        public string Uid { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }

    private static bool IsAllDigits(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        for (int i = 0; i < s.Length; i++)
        {
            if (!char.IsDigit(s[i])) return false;
        }
        return true;
    }

    private async Task WriteUidsToPluginJsonAsync(List<UidEntry> entries)
    {
        try
        {
            var pluginsRoot = Path.Combine(AppContext.BaseDirectory, "Plugins");
            var pluginDir = Path.Combine(pluginsRoot, PluginFolderName);
            Directory.CreateDirectory(pluginDir);

            var jsonPath = Path.Combine(pluginDir, JsonFileName);

            var items = entries.Select(e => new
            {
                uid = e.Uid,
                updatedAt = e.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ss")
            }).ToArray();

            var payload = new { uids = items };

            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            await File.WriteAllTextAsync(jsonPath, json);

            Debug.WriteLine($"[UidLookupService] 已写入 {entries.Count} 个 UID 到 {jsonPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UidLookupService] 写入 uids.json 失败 - {ex.Message}");
        }
    }
}
