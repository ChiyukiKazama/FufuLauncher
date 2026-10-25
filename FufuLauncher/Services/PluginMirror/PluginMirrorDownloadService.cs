/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Activation;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;
using FufuLauncher.Views;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.PluginMirror;

public class PluginMirrorDownloadService
{
    public const string SettingKey = "IsPluginMirrorAccelerationEnabled";

    private readonly ILocalSettingsService _localSettingsService;
    private readonly MirrorSiteProvider _mirrorProvider;
    private PluginMirrorDownloadWindow? _activeWindow;

    public PluginMirrorDownloadService(ILocalSettingsService localSettingsService, MirrorSiteProvider mirrorProvider)
    {
        _localSettingsService = localSettingsService;
        _mirrorProvider = mirrorProvider;
    }

    public async Task<bool> IsEnabledAsync()
    {
        var json = await _localSettingsService.ReadSettingAsync(SettingKey);
        return json == null || Convert.ToBoolean(json);
    }

    public async Task<bool> TryDownloadViaMirrorAsync(PluginStoreService storeService,
        string fileUrl, string destinationPath, IProgress<DownloadProgressInfo>? progress,
        string? expectedHash, CancellationToken cancellationToken)
    {
        if (!await IsEnabledAsync()) return false;
        if (_mirrorProvider.LoadConfig().Mirrors.Count == 0)
        {
            DebugLog.Info("PluginMirrorDownloadService",
                "[PluginMirrorDownloadService] No mirrors configured, use direct download");
            return false;
        }

        var targetUrl = fileUrl;
        if (!MirrorSiteProvider.IsGitHubUrl(fileUrl))
        {
            var resolved = await MirrorSiteProvider.ResolveRedirectUrlAsync(fileUrl);
            if (resolved == null || !MirrorSiteProvider.IsGitHubUrl(resolved))
                return false;
            targetUrl = resolved;
            DebugLog.Info("PluginMirrorDownloadService",
                $"[PluginMirrorDownloadService] Redirect to GitHub detected: {fileUrl} -> {targetUrl}");
        }

        if (App.MainWindow?.DispatcherQueue is not { } dispatcherQueue)
        {
            DebugLog.Info("PluginMirrorDownloadService",
                "[PluginMirrorDownloadService] Main window dispatcher unavailable, use direct download");
            return false;
        }

        if (_activeWindow != null)
        {
            _activeWindow.Activate();
            return await _activeWindow.CompletionTask;
        }

        PluginMirrorDownloadWindow? window = null;
        if (dispatcherQueue.HasThreadAccess)
        {
            window = CreateWindow(storeService, targetUrl, destinationPath, progress, expectedHash, cancellationToken);
        }
        else
        {
            await dispatcherQueue.EnqueueAsync(() =>
            {
                window = CreateWindow(storeService, targetUrl, destinationPath, progress, expectedHash,
                    cancellationToken);
            });
        }

        if (window == null) return false;

        var result = await window.CompletionTask;
        return result;
    }

    private PluginMirrorDownloadWindow CreateWindow(PluginStoreService storeService,
        string fileUrl, string destinationPath, IProgress<DownloadProgressInfo>? progress,
        string? expectedHash, CancellationToken cancellationToken)
    {
        var window = new PluginMirrorDownloadWindow(storeService, _mirrorProvider,
            fileUrl, destinationPath, progress, expectedHash, cancellationToken);
        window.Closed += (s, e) => _activeWindow = null;
        _activeWindow = window;
        window.Activate();
        return window;
    }
}