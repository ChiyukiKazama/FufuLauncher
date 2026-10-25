/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using Microsoft.Win32;

namespace FufuLauncher.Helpers
{
    public static class GamePathFinder
    {
        public static async Task<string?> FindGamePathAsync()
        {
            DebugLog.Info("GamePathFinder", "[Debug] FindGamePath start");
            try
            {
                var exeNames = await GameExeManager.GetExeNamesAsync();

                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\miHoYo\HYP\1_1\hk4e_cn"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("GameInstallPath")?.ToString();
                        DebugLog.Info("GamePathFinder", $"[Debug] Read registry GameInstallPath, value: '{value}'");

                        if (!string.IsNullOrEmpty(value))
                        {
                            foreach (var exeName in exeNames)
                            {
                                var exePath = Path.Combine(value, exeName);
                                if (File.Exists(exePath))
                                {
                                    DebugLog.Info("GamePathFinder", $"[Debug] Validation passed: file found {exePath}");
                                    return Path.GetDirectoryName(exePath);
                                }

                                var subDirPath = Path.Combine(value, "Genshin Impact Game", exeName);
                                if (File.Exists(subDirPath))
                                {
                                    DebugLog.Info("GamePathFinder",
                                        $"[Debug] Validation passed: file found {subDirPath}");
                                    return Path.GetDirectoryName(subDirPath);
                                }
                            }

                            DebugLog.Info("GamePathFinder",
                                "[Debug] Registry path exists but game executable not found there!");
                        }
                    }
                    else
                    {
                        DebugLog.Info("GamePathFinder", "[Debug] Registry key hk4e_cn missing or null.");
                    }
                }

                DebugLog.Info("GamePathFinder", "[Debug] Checking common default paths...");
                string[] basePaths =
                {
                    @"C:\Program Files\Genshin Impact\Genshin Impact Game",
                    @"D:\Program Files\Genshin Impact\Genshin Impact Game",
                    @"E:\Program Files\Genshin Impact\Genshin Impact Game",
                    @"C:\Genshin Impact\Genshin Impact Game",
                    @"D:\Genshin Impact\Genshin Impact Game",
                    @"E:\Genshin Impact\Genshin Impact Game"
                };

                foreach (var basePath in basePaths)
                {
                    foreach (var exeName in exeNames)
                    {
                        var exePath = Path.Combine(basePath, exeName);
                        if (File.Exists(exePath))
                        {
                            DebugLog.Info("GamePathFinder", $"[Debug] Game found in common paths: {exePath}");
                            return Path.GetDirectoryName(exePath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("GamePathFinder", $"[Debug] FindGamePath exception: {ex.Message}");
            }

            DebugLog.Info("GamePathFinder", "[Debug] FindGamePath found no path, returning null.");
            return null;
        }
    }
}