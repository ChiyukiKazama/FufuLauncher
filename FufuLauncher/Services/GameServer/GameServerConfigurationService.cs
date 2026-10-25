/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;

namespace FufuLauncher.Services.GameServer;

public sealed class GameServerConfigurationService
{
    public GameServerScheme? TryDetectCurrentScheme(string gameDir)
    {
        bool hasCnExe = File.Exists(Path.Combine(gameDir, GameConstants.CN_EXE));
        bool hasOsExe = File.Exists(Path.Combine(gameDir, GameConstants.OS_EXE));
        if (!hasCnExe && !hasOsExe)
        {
            return null;
        }

        bool isOversea = hasOsExe;
        if (hasCnExe && hasOsExe)
        {
            isOversea = !Directory.Exists(Path.Combine(gameDir, GameConstants.CN_DATA_DIR));
        }

        if (!TryReadChannelOptions(gameDir, out ChannelType channel, out SubChannelType subChannel))
        {
            return isOversea ? GameServerScheme.OverseaOfficialDefault : GameServerScheme.ChineseOfficialOfficial;
        }

        GameServerScheme? matched = GameServerScheme.Known.FirstOrDefault(scheme =>
            scheme.IsOversea == isOversea && scheme.Channel == channel && scheme.SubChannel == subChannel);
        if (matched is not null)
        {
            return matched;
        }

        if (channel == ChannelType.Bili)
        {
            return GameServerScheme.BilibiliDefault;
        }

        return isOversea ? GameServerScheme.OverseaOfficialDefault : GameServerScheme.ChineseOfficialOfficial;
    }

    public string? TryGetGameVersion(string gameDir)
    {
        var ini = new IniFile(Path.Combine(gameDir, GameConstants.CONFIG_FILE_NAME));
        return ini.ReadAll().TryGetValue("General", out var general)
               && general.TryGetValue("game_version", out var version)
            ? version
            : null;
    }

    public void ApplyScheme(string gameDir, GameServerScheme scheme)
    {
        string configPath = Path.Combine(gameDir, GameConstants.CONFIG_FILE_NAME);
        string temporary = configPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            if (File.Exists(configPath))
            {
                File.Copy(configPath, temporary);
                File.SetAttributes(temporary, FileAttributes.Normal);
            }
            else
            {
                string backupPath = Path.Combine(AppPaths.ServerCacheDir,
                    scheme.IsOversea ? "config_oversea.ini" : "config_cn.ini");
                if (!File.Exists(backupPath) || !TryRestoreConfigBackup(backupPath, temporary))
                {
                    File.WriteAllText(temporary, BuildNewConfigContent(scheme, TryGetGameVersion(gameDir)));
                }
            }

            UpdateScheme(temporary, scheme);
            File.Move(temporary, configPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void UpdateScheme(string configPath, GameServerScheme scheme)
    {
        var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["channel"] = ((int)scheme.Channel).ToString(),
            ["sub_channel"] = ((int)scheme.SubChannel).ToString(),
            ["cps"] = scheme.Cps,
        };
        var lines = File.ReadAllLines(configPath).ToList();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool inGeneral = false;
        int generalEnd = -1;
        for (int index = 0; index < lines.Count; index++)
        {
            string trimmed = lines[index].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (inGeneral)
                {
                    generalEnd = index;
                }

                inGeneral = trimmed[1..^1].Trim().Equals("General", StringComparison.OrdinalIgnoreCase);
                if (inGeneral)
                {
                    generalEnd = lines.Count;
                }

                continue;
            }

            int separator = trimmed.IndexOf('=');
            if (inGeneral && separator > 0 && updates.TryGetValue(trimmed[..separator].Trim(), out string? value))
            {
                string key = trimmed[..separator].Trim();
                lines[index] = $"{key}={value}";
                found.Add(key);
            }
        }

        if (generalEnd < 0)
        {
            lines.Add("[General]");
            generalEnd = lines.Count;
        }

        lines.InsertRange(generalEnd, updates.Where(item => !found.Contains(item.Key))
            .Select(item => $"{item.Key}={item.Value}"));
        File.WriteAllLines(configPath, lines, Encoding.UTF8);
    }

    public void BackupConfig(string gameDir, bool isOversea)
    {
        string configPath = Path.Combine(gameDir, GameConstants.CONFIG_FILE_NAME);
        if (!File.Exists(configPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.ServerCacheDir);
            File.Copy(configPath,
                Path.Combine(AppPaths.ServerCacheDir, isOversea ? "config_oversea.ini" : "config_cn.ini"), true);
        }
        catch
        {
        }
    }

    private bool TryReadChannelOptions(string gameDir, out ChannelType channel, out SubChannelType subChannel)
    {
        channel = ChannelType.Default;
        subChannel = SubChannelType.Default;

        var ini = new IniFile(Path.Combine(gameDir, GameConstants.CONFIG_FILE_NAME));
        if (!ini.ReadAll().TryGetValue("General", out var general))
        {
            return false;
        }

        bool hasChannel = general.TryGetValue("channel", out var channelValue) &&
                          Enum.TryParse(channelValue, out channel);
        bool hasSubChannel = general.TryGetValue("sub_channel", out var subChannelValue) &&
                             Enum.TryParse(subChannelValue, out subChannel);
        return hasChannel || hasSubChannel;
    }

    private static bool TryRestoreConfigBackup(string backupPath, string configPath)
    {
        try
        {
            File.Copy(backupPath, configPath, true);
            File.SetAttributes(configPath, FileAttributes.Normal);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildNewConfigContent(GameServerScheme scheme, string? gameVersion)
    {
        string gameBiz = scheme.IsOversea ? "hk4e_global" : "hk4e_cn";

        var sb = new StringBuilder();
        sb.AppendLine("[General]");
        sb.AppendLine($"uapc={{\"{gameBiz}\":{{\"uapc\":\"\"}},\"hyp\":{{\"uapc\":\"\"}}}}");
        sb.AppendLine($"channel={(int)scheme.Channel}");
        sb.AppendLine($"sub_channel={(int)scheme.SubChannel}");
        sb.AppendLine($"cps={scheme.Cps}");
        if (!string.IsNullOrEmpty(gameVersion))
        {
            sb.AppendLine($"game_version={gameVersion}");
        }

        return sb.ToString();
    }
}