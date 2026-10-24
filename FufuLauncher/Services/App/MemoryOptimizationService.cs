/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services
{
    public static class MemoryOptimizationService
    {
        public const string IsEnabledSettingKey = "IsMemoryReleaseEnabled";

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, int minimumWorkingSetSize,
            int maximumWorkingSetSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        public static void FlushMemory()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return;
            }

            try
            {
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(0, GCCollectionMode.Forced, true);
                SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
            }
            catch (Exception ex)
            {
                DebugLog.Info("MemoryOptimizationService", $"Memory cleanup error: {ex.Message}");
            }
        }
    }
}