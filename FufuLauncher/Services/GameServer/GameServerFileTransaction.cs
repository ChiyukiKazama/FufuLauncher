/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Services.GameServer;

internal sealed class GameServerFileTransaction : IDisposable
{
    private readonly string _gameDir;
    private readonly Dictionary<string, Snapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private bool _preserveBackup;

    public GameServerFileTransaction(string gameDir)
    {
        _gameDir = Path.GetFullPath(gameDir);
        WorkDir = ResolvePath(_gameDir, $".fufu-server-switch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(WorkDir);
    }

    public string WorkDir
    {
        get;
    }

    public static string ResolvePath(string root, string relativePath)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':') ||
            !fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Invalid game resource path: {relativePath}");
        }


        for (string? current = fullPath; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Game resource path contains a link: {current}");
            }

            if (current.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return fullPath;
    }

    public void Capture(string path)
    {
        ResolvePath(_gameDir, Path.GetRelativePath(_gameDir, path));
        if (_snapshots.ContainsKey(path))
        {
            return;
        }

        string? backup = null;
        FileAttributes attributes = FileAttributes.Normal;
        if (File.Exists(path))
        {
            attributes = File.GetAttributes(path);
            backup = Path.Combine(WorkDir, $"backup-{_snapshots.Count}");
            File.Copy(path, backup);
            File.SetAttributes(backup, FileAttributes.Normal);
        }

        _snapshots.Add(path, new Snapshot(backup, attributes));
    }

    public void Remove(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        Capture(path);
        MakeWritable(path);
        File.Delete(path);
        MarkChanged(path);
    }

    public void Install(string source, string destination)
    {
        Capture(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        MakeWritable(destination);
        File.Move(source, destination, true);
        MarkChanged(destination);
    }

    public void BackupDeprecated(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        Capture(path);
        Capture(path + ".backup");
        MakeWritable(path);
        MakeWritable(path + ".backup");
        File.Move(path, path + ".backup", true);
        MarkChanged(path);
        MarkChanged(path + ".backup");
    }

    public void MarkChanged(string path) => _snapshots[path].Changed = true;

    public void Rollback()
    {
        var failures = new List<Exception>();
        foreach (var (path, snapshot) in _snapshots.Reverse())
        {
            try
            {
                if (!snapshot.Changed)
                {
                    if (snapshot.Backup is not null && File.GetAttributes(path) != snapshot.Attributes)
                    {
                        File.SetAttributes(path, snapshot.Attributes);
                    }

                    continue;
                }

                if (snapshot.Backup is null)
                {
                    if (File.Exists(path))
                    {
                        MakeWritable(path);
                        File.Delete(path);
                    }
                }
                else
                {
                    MakeWritable(path);
                    File.Copy(snapshot.Backup, path, true);
                    File.SetAttributes(path, snapshot.Attributes);
                }
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        if (failures.Count > 0)
        {
            _preserveBackup = true;
            throw new AggregateException($"Could not restore all game files. Backups: {WorkDir}", failures);
        }
    }

    public void Dispose()
    {
        if (!_preserveBackup)
        {
            try
            {
                Directory.Delete(WorkDir, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void MakeWritable(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    private sealed record Snapshot(string? Backup, FileAttributes Attributes)
    {
        public bool Changed
        {
            get;
            set;
        }
    }
}