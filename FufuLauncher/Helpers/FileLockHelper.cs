/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FufuLauncher.Helpers;

public sealed record FileLockOwner(int ProcessId, string ProcessName);

public static class FileLockHelper
{
    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string? AppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string? ServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint SessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, string sessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint sessionHandle, uint fileCount, string[] fileNames,
        uint applicationCount, [In] RmUniqueProcess[]? applications, uint serviceCount, string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint sessionHandle, out uint processInfoNeeded,
        ref uint processInfoCount, [In, Out] RmProcessInfo[]? processInfo, out uint rebootReasons);

    public static bool IsFileLocked(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("FileLockHelper", $"[FileLock] Detection failed {filePath}: {ex.Message}");
            return false;
        }
    }

    public static string? FindLockedFile(params string?[] filePaths)
    {
        foreach (var path in filePaths)
        {
            if (!string.IsNullOrEmpty(path) && IsFileLocked(path)) return path;
        }

        return null;
    }

    public static string? FindLockedFileInDirectory(string? directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) return null;

        try
        {
            foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                if (IsFileLocked(file)) return file;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("FileLockHelper", $"[FileLock] Directory detection failed {directoryPath}: {ex.Message}");
        }

        return null;
    }

    public static IReadOnlyList<FileLockOwner> FindFileUsers(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return Array.Empty<FileLockOwner>();

        var result = RmStartSession(out var session, 0, Guid.NewGuid().ToString("N")[..24]);
        if (result != 0) throw new Win32Exception(result);

        try
        {
            result = RmRegisterResources(session, 1, new[] { filePath }, 0, null, 0, null);
            if (result != 0) throw new Win32Exception(result);

            var owners = new List<FileLockOwner>();
            uint needed = 0;
            while (true)
            {
                var processInfo = needed == 0 ? null : new RmProcessInfo[needed];
                var count = needed;
                result = RmGetList(session, out needed, ref count, processInfo, out _);
                if (result == ErrorMoreData)
                {
                    if (needed == 0) break;
                    continue;
                }

                if (result != 0) throw new Win32Exception(result);
                if (processInfo != null)
                {
                    for (var index = 0; index < count && index < processInfo.Length; index++)
                    {
                        var processId = processInfo[index].Process.ProcessId;
                        owners.Add(new FileLockOwner(processId,
                            ResolveProcessName(processId, processInfo[index].AppName)));
                    }
                }

                break;
            }

            return owners.DistinctBy(owner => owner.ProcessId).ToArray();
        }
        finally
        {
            RmEndSession(session);
        }
    }

    public static string? DescribeFileUsers(string? filePath)
    {
        try
        {
            var owners = FindFileUsers(filePath);
            return owners.Count == 0
                ? null
                : string.Join(", ", owners.Select(owner => $"{owner.ProcessName} (PID {owner.ProcessId})"));
        }
        catch (Exception ex)
        {
            DebugLog.Info("FileLockHelper", $"[FileLock] Occupant probe failed {filePath}: {ex.Message}");
            return null;
        }
    }

    private static string ResolveProcessName(int processId, string? appName)
    {
        if (!string.IsNullOrWhiteSpace(appName)) return appName;

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return $"PID {processId}";
        }
    }

    public static string GetLockedFileTitle() => "Plugin_FileLocked_Title".GetLocalized();

    public static string GetLockedFileMessage(string lockedFilePath) =>
        string.Format("Plugin_FileLocked_Content".GetLocalized(), Path.GetFileName(lockedFilePath));
}