/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services.Mcp;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FufuLauncher.ViewModels;

public sealed class McpAuditViewModel : ObservableObject
{
    private readonly McpHostService _host;
    private readonly DispatcherQueue? _dispatcher;
    private readonly EventHandler _auditChangedHandler;

    private bool _initialized;
    private bool _suppressUpdates;
    private bool _auditEnabled;
    private string _connectionsText = "0";
    private string _callsText = "0";
    private string _failuresText = "0";
    private string _archiveText = string.Empty;
    private string _archiveHint = string.Empty;
    private Visibility _emptyVisibility = Visibility.Visible;

    public McpAuditViewModel(McpHostService host, DispatcherQueue? dispatcher)
    {
        _host = host;
        _dispatcher = dispatcher;
        _auditChangedHandler = (_, _) => DispatchRefresh();
    }

    public ObservableCollection<McpAuditEntryView> Entries
    {
        get;
    } = new();

    public string ArchiveDirectory => _host.Audit.ArchiveDirectory;

    public bool AuditEnabled
    {
        get => _auditEnabled;
        set
        {
            if (!SetProperty(ref _auditEnabled, value) || _suppressUpdates || !_initialized)
            {
                return;
            }

            _ = ApplyAuditAsync(value);
        }
    }

    public string ConnectionsText
    {
        get => _connectionsText;
        private set => SetProperty(ref _connectionsText, value);
    }

    public string CallsText
    {
        get => _callsText;
        private set => SetProperty(ref _callsText, value);
    }

    public string FailuresText
    {
        get => _failuresText;
        private set => SetProperty(ref _failuresText, value);
    }

    public string ArchiveText
    {
        get => _archiveText;
        private set => SetProperty(ref _archiveText, value);
    }

    public string ArchiveHint
    {
        get => _archiveHint;
        private set => SetProperty(ref _archiveHint, value);
    }

    public Visibility EmptyVisibility
    {
        get => _emptyVisibility;
        private set => SetProperty(ref _emptyVisibility, value);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _host.InitializeAsync();
        _host.Audit.Changed += _auditChangedHandler;

        _suppressUpdates = true;
        try
        {
            AuditEnabled = _host.AuditEnabled;
        }
        finally
        {
            _suppressUpdates = false;
        }

        _initialized = true;
        Refresh();
    }

    public void ClearAudit()
    {
        _host.Audit.Clear();
        Refresh();
    }

    public void Detach()
    {
        _host.Audit.Changed -= _auditChangedHandler;
    }

    private async Task ApplyAuditAsync(bool enabled)
    {
        try
        {
            await _host.SetAuditEnabledAsync(enabled);
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpAuditViewModel", $"[Mcp] 应用审计设置失败：{ex.Message}");
        }
        finally
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var entries = _host.Audit.Snapshot(100);
        Entries.Clear();

        foreach (var entry in entries)
        {
            Entries.Add(BuildEntry(entry));
        }

        var counters = _host.Audit.GetCounters();
        ConnectionsText = counters.Connections.ToString();
        CallsText = counters.Calls.ToString();
        FailuresText = counters.Failures.ToString();
        ArchiveText = string.Format("Mcp_AuditArchivedCount".GetLocalized(), _host.Audit.ArchivedCount);
        ArchiveHint = string.Format("Mcp_AuditArchiveHint".GetLocalized(), McpAuditLog.MaxEntries);
        EmptyVisibility = Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _suppressUpdates = true;
        try
        {
            if (_auditEnabled != _host.AuditEnabled)
            {
                AuditEnabled = _host.AuditEnabled;
            }
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    private static McpAuditEntryView BuildEntry(McpAuditEntry entry)
    {
        var title = entry.Kind == McpAuditKind.Connection
            ? entry.Action switch
            {
                McpAuditActions.Connect => "Mcp_AuditEventConnect".GetLocalized(),
                McpAuditActions.AuthFailed => "Mcp_AuditEventAuthFailed".GetLocalized(),
                McpAuditActions.IpBlocked => "Mcp_AuditEventIpBlocked".GetLocalized(),
                _ => entry.Action
            }
            : McpToolCatalog.Find(entry.Action)?.TitleKey.GetLocalized() ?? entry.Action;

        var resultText = (entry.Success ? "Mcp_AuditResultSuccess" : "Mcp_AuditResultFailure").GetLocalized();
        var meta = $"{entry.Timestamp:HH:mm:ss} · {entry.RemoteAddress}";
        var detailMeta = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} · {entry.RemoteAddress} · {resultText}";

        if (entry.Kind == McpAuditKind.ToolCall)
        {
            meta += $" · {entry.DurationMs} ms";
            detailMeta += $" · {entry.DurationMs} ms";
        }

        var detail = entry.Detail ?? string.Empty;

        return new McpAuditEntryView
        {
            Title = title,
            Meta = meta,
            ResultText = resultText,
            DetailMeta = detailMeta,
            DetailMessage = detail,
            DetailMessageVisibility = string.IsNullOrWhiteSpace(detail)
                ? Visibility.Collapsed
                : Visibility.Visible,
            SuccessVisibility = entry.Success ? Visibility.Visible : Visibility.Collapsed,
            FailureVisibility = entry.Success ? Visibility.Collapsed : Visibility.Visible
        };
    }

    private void DispatchRefresh()
    {
        if (_dispatcher == null || _dispatcher.HasThreadAccess)
        {
            Refresh();
            return;
        }

        _dispatcher.TryEnqueue(Refresh);
    }
}