/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FufuLauncher.Helpers
{
    public readonly record struct GpuInfo(string Name, string Vendor, string Family, string Series);

    public static class SystemEnvironmentHelper
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass, IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private enum TOKEN_INFORMATION_CLASS
        {
            TokenElevationType = 18
        }

        private enum TOKEN_ELEVATION_TYPE
        {
            TokenElevationTypeFull = 2
        }

        public static bool IsRunningAsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        public static bool IsUacElevatedWithConsent()
        {
            try
            {
                if (!IsRunningAsAdministrator()) return false;
                if (OpenProcessToken(GetCurrentProcess(), 0x0008, out var tokenHandle))
                {
                    var size = Marshal.SizeOf(typeof(int));
                    var ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (GetTokenInformation(tokenHandle, TOKEN_INFORMATION_CLASS.TokenElevationType, ptr, (uint)size, out _))
                        {
                            var type = (TOKEN_ELEVATION_TYPE)Marshal.ReadInt32(ptr);
                            return type == TOKEN_ELEVATION_TYPE.TokenElevationTypeFull;
                        }
                    }
                    finally 
                    { 
                        Marshal.FreeHGlobal(ptr); 
                        if (tokenHandle != IntPtr.Zero) CloseHandle(tokenHandle); 
                    }
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        public static bool IsVCRedistInstalled()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"))
                {
                    if (key != null && key.GetValue("Installed") is int installed && installed == 1) return true;
                }
                
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x86"))
                {
                    if (key != null && key.GetValue("Installed") is int installed && installed == 1) return true;
                }
            }
            catch
            {
                // ignored
            }
            
            return false;
        }

        private static readonly string[] InvalidValues = {
            "To Be Filled By O.E.M.",
            "System Serial Number",
            "Default string",
            "None",
            "N/A"
        };

        public static string GetMotherboardId()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                using var boards = searcher.Get();
                var serials = new List<string?>();
                foreach (ManagementObject board in boards)
                {
                    using (board) serials.Add(board["SerialNumber"]?.ToString());
                }
                return MotherboardIdentity.FromSerialNumbers(serials);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string GetHwid()
        {
            try
            {
                string cpuId = WmiQuery("Win32_Processor", "ProcessorId");
                string boardSn = WmiQuery("Win32_BaseBoard", "SerialNumber");
                string biosSn = WmiQuery("Win32_BIOS", "SerialNumber");
                string diskSn = GetSystemDiskSerial();

                var parts = new[] { cpuId, boardSn, biosSn, diskSn }
                    .Where(s => !string.IsNullOrWhiteSpace(s) && !InvalidValues.Contains(s, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (parts.Count == 0) return "Unknown";

                string raw = string.Join("|", parts);
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                byte[] hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
                string hex = BitConverter.ToString(hash).Replace("-", "");
                return hex.Substring(0, 32);
            }
            catch
            {
                return "Unknown";
            }
        }

        private static string WmiQuery(string wmiClass, string property)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
                foreach (var obj in searcher.Get())
                {
                    var val = obj[property]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
            }
            catch { }
            return string.Empty;
        }

        private static string GetSystemDiskSerial()
        {
            try
            {
                string sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
                using var partSearcher = new System.Management.ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{sysDrive}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                foreach (var part in partSearcher.Get())
                {
                    string deviceId = part["DeviceID"]?.ToString() ?? "";
                    var match = System.Text.RegularExpressions.Regex.Match(deviceId, @"Disk #(\d+)");
                    if (match.Success)
                    {
                        string diskIndex = match.Groups[1].Value;
                        using var diskSearcher = new System.Management.ManagementObjectSearcher(
                            $"SELECT SerialNumber FROM Win32_DiskDrive WHERE Index={diskIndex}");
                        foreach (var disk in diskSearcher.Get())
                        {
                            var sn = disk["SerialNumber"]?.ToString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(sn)) return sn;
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
        }
        
        public static string GetGpuName()
        {
            try
            {
                string fallback = string.Empty;
                using (var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController"))
                {
                    foreach (var item in searcher.Get())
                    {
                        var name = item["Name"]?.ToString()?.Trim() ?? string.Empty;
                        if (string.IsNullOrEmpty(name)) continue;

                        if (string.IsNullOrEmpty(fallback))
                            fallback = name;

                        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                        {
                            return name;
                        }
                    }
                }
                return string.IsNullOrEmpty(fallback) ? "Unknown" : fallback;
            }
            catch
            {
                return "Unknown";
            }
        }
        
        public static string GetGpuVendor()
        {
            var name = GetGpuName();
            if (string.IsNullOrEmpty(name) || name == "Unknown")
                return "Unknown";

            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                return "NVIDIA";
            if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                return "AMD";
            if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                return "Intel";

            return "Unknown";
        }
        
        private static readonly string[] KnownFamilies =
        {
            // NVIDIA
            "RTX", "GTX", "Quadro", "Tesla",
            // AMD
            "Radeon Pro", "RX", "Pro",
            // Intel
            "Arc", "Iris Xe", "Iris", "UHD", "HD", "Xe"
        };
        
        private static readonly Regex NvidiaSeriesRegex = new(
            @"(?:RTX|GTX|Quadro|Tesla)\s+(\d{4})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AmdRxSeriesRegex = new(
            @"RX\s+(\d{4})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IntelArcSeriesRegex = new(
            @"Arc\s+(A\d{3})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IntelUhdSeriesRegex = new(
            @"(?:UHD|HD)\s*(?:Graphics\s*)?(\d{3,4})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        public static GpuInfo GetGpuInfo()
        {
            var name = GetGpuName();
            var vendor = GetGpuVendor();
            return new GpuInfo(name, vendor, ParseFamily(name), ParseSeries(name));
        }

        private static string ParseFamily(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "Unknown")
                return "Unknown";

            foreach (var family in KnownFamilies)
            {
                if (name.Contains(family, StringComparison.OrdinalIgnoreCase))
                {
                    if (family.Equals("Radeon Pro", StringComparison.OrdinalIgnoreCase))
                        return "Pro";
                    if (family.Equals("Iris Xe", StringComparison.OrdinalIgnoreCase))
                        return "Iris";
                    return family;
                }
            }
            return "Unknown";
        }

        private static string ParseSeries(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;
            
            var m = NvidiaSeriesRegex.Match(name);
            if (m.Success)
            {
                var digits = m.Groups[1].Value;
                return digits.Substring(0, 2);
            }
            
            m = AmdRxSeriesRegex.Match(name);
            if (m.Success)
            {
                var digits = m.Groups[1].Value;
                return digits.Substring(0, 1) + "000";
            }
            
            m = IntelArcSeriesRegex.Match(name);
            if (m.Success)
            {
                return m.Groups[1].Value;
            }
            
            m = IntelUhdSeriesRegex.Match(name);
            if (m.Success)
            {
                return m.Groups[1].Value;
            }

            return string.Empty;
        }
        
        public static string GetCpuName()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("select Name from Win32_Processor"))
                {
                    foreach (var item in searcher.Get())
                    {
                        var name = item["Name"]?.ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                            return name;
                    }
                }
            }
            catch { }
            return "Unknown";
        }
        
        public static long GetTotalMemoryGB()
        {
            try
            {
                long totalBytes = 0;
                using (var searcher = new ManagementObjectSearcher("select Capacity from Win32_PhysicalMemory"))
                {
                    foreach (var item in searcher.Get())
                    {
                        if (long.TryParse(item["Capacity"]?.ToString(), out long capacity))
                            totalBytes += capacity;
                    }
                }
                return totalBytes > 0 ? totalBytes / (1024 * 1024 * 1024) : 0;
            }
            catch
            {
                return 0;
            }
        }
        
        public static string GetOsVersion()
        {
            try
            {
                return RuntimeInformation.OSDescription;
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}
