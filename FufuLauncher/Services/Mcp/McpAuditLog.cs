/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Mcp;

public enum McpAuditKind
{
    Connection,
    ToolCall
}

public static class McpAuditActions
{
    public const string Connect = "connect";
    public const string AuthFailed = "auth_failed";
    public const string IpBlocked = "ip_blocked";
}

public sealed record McpAuditEntry(
    DateTimeOffset Timestamp,
    string RemoteAddress,
    McpAuditKind Kind,
    string Action,
    bool Success,
    int DurationMs,
    string? Detail);

public sealed class McpAuditLog
{
    public const int MaxEntries = 200;
    public const int ArchiveBatch = 100;

    private const long MaxArchiveFileBytes = 2 * 1024 * 1024;
    private const string ArchiveFileName = "mcp-audit.log";

    private const string ArchiveHeader =
        "# timestamp | result | kind | action | remote | duration | detail";

    private readonly object _gate = new();
    private readonly List<McpAuditEntry> _entries = new();
    private readonly string _archiveDirectory;
    private bool _enabled = true;
    private int _connections;
    private int _calls;
    private int _failures;
    private int _archivedCount;

    public McpAuditLog()
    {
        _archiveDirectory = Path.Combine(AppPaths.RootDir, "Logs");
    }

    public event EventHandler? Changed;

    public string ArchiveDirectory => _archiveDirectory;

    public bool IsEnabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
    }

    public int ArchivedCount
    {
        get
        {
            lock (_gate)
            {
                return _archivedCount;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        bool changed;
        lock (_gate)
        {
            changed = _enabled != enabled;
            _enabled = enabled;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Record(McpAuditKind kind, string remoteAddress, string action, bool success, int durationMs,
        string? detail = null)
    {
        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            _entries.Add(new McpAuditEntry(DateTimeOffset.Now, remoteAddress, kind, action, success, durationMs,
                detail));

            if (kind == McpAuditKind.Connection)
            {
                if (success)
                {
                    _connections++;
                }
            }
            else
            {
                _calls++;
            }

            if (!success)
            {
                _failures++;
            }

            if (_entries.Count > MaxEntries + ArchiveBatch)
            {
                var flushCount = _entries.Count - MaxEntries;
                var batch = _entries.GetRange(0, flushCount);
                _entries.RemoveRange(0, flushCount);
                ArchiveLocked(batch);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<McpAuditEntry> Snapshot(int maximum = 100)
    {
        lock (_gate)
        {
            var start = Math.Max(0, _entries.Count - maximum);
            var result = new List<McpAuditEntry>(_entries.Count - start);
            for (var index = _entries.Count - 1; index >= start; index--)
            {
                result.Add(_entries[index]);
            }

            return result;
        }
    }

    public (int Connections, int Calls, int Failures) GetCounters()
    {
        lock (_gate)
        {
            return (_connections, _calls, _failures);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_entries.Count == 0 && _connections == 0 && _calls == 0 && _failures == 0)
            {
                return;
            }

            _entries.Clear();
            _connections = 0;
            _calls = 0;
            _failures = 0;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ArchiveLocked(IReadOnlyList<McpAuditEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_archiveDirectory);
            var path = Path.Combine(_archiveDirectory, ArchiveFileName);

            if (File.Exists(path) && new FileInfo(path).Length >= MaxArchiveFileBytes)
            {
                var rotated = Path.Combine(_archiveDirectory,
                    $"mcp-audit-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                File.Move(path, rotated, true);
            }

            var isNewFile = !File.Exists(path);
            using var writer = new StreamWriter(path, true, new UTF8Encoding(false));
            if (isNewFile)
            {
                writer.WriteLine(ArchiveHeader);
            }

            foreach (var entry in entries)
            {
                writer.WriteLine(FormatLine(entry));
            }

            _archivedCount += entries.Count;
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpAuditLog", $"[Mcp] 写入审计归档失败：{ex.Message}");
        }
    }

    private static string FormatLine(McpAuditEntry entry)
    {
        var time = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        var result = entry.Success ? "OK" : "FAIL";
        var kind = entry.Kind == McpAuditKind.Connection ? "conn" : "tool";
        var duration = entry.Kind == McpAuditKind.ToolCall ? $"{entry.DurationMs}ms" : "-";
        var detail = string.IsNullOrWhiteSpace(entry.Detail)
            ? string.Empty
            : entry.Detail.Replace('\r', ' ').Replace('\n', ' ');

        return
            $"{time} | {result,-4} | {kind,-4} | {entry.Action,-20} | {entry.RemoteAddress,-15} | {duration,-8} | {detail}";
    }
}