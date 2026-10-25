/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Services.Background;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Services;

public sealed class AutoThemeColorService : IAutoThemeColorService
{
    public const string SettingKey = "IsAutoThemeColorEnabled";

    private readonly ILocalSettingsService _localSettingsService;
    private int _applyVersion;

    public AutoThemeColorService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public string? ThemeColorHex
    {
        get;
        private set;
    }

    public async Task<bool> IsEnabledAsync()
    {
        try
        {
            var value = await _localSettingsService.ReadSettingAsync(SettingKey).ConfigureAwait(false);
            return value != null && Convert.ToBoolean(value);
        }
        catch (Exception ex)
        {
            DebugLog.Info("AutoThemeColorService", $"Failed to read auto theme color setting: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ApplyForBackgroundAsync(ImageSource? backgroundImage)
    {
        var version = Interlocked.Increment(ref _applyVersion);

        if (backgroundImage == null)
            return false;

        if (!await IsEnabledAsync().ConfigureAwait(false))
            return false;

        if (version != Volatile.Read(ref _applyVersion))
            return false;

        var palette = BackgroundColorPaletteCache.Get(backgroundImage);
        if (palette == null)
        {
            DebugLog.Info("AutoThemeColorService",
                "No color palette available for the current background, keeping current colors");
            return false;
        }

        return ApplyPalette(palette, version);
    }

    public Task<bool> ApplyForCurrentBackgroundAsync()
    {
        var backgroundImage = App.MainWindow is MainWindow mainWindow
            ? mainWindow.CurrentGlobalBackground?.ImageSource
            : null;

        return ApplyForBackgroundAsync(backgroundImage);
    }

    public async Task RestoreManualColorsAsync()
    {
        Interlocked.Increment(ref _applyVersion);
        ThemeColorHex = null;

        string? storedThemeColor = null;
        try
        {
            var value = await _localSettingsService.ReadSettingAsync("AppThemeColor").ConfigureAwait(false);
            storedThemeColor = value?.ToString();
        }
        catch (Exception ex)
        {
            DebugLog.Info("AutoThemeColorService", $"Failed to read manual theme color: {ex.Message}");
        }

        TryRunOnUiThread(() =>
        {
            var themeHex = string.IsNullOrWhiteSpace(storedThemeColor)
                ? ThemeHelper.ToHexColor(ThemeHelper.GetSystemAccentColor())
                : storedThemeColor!;

            ThemeHelper.ApplyThemeColor(themeHex);
            WeakReferenceMessenger.Default.Send(new TextStyleChangedMessage());
        });
    }

    private bool ApplyPalette(BackgroundColorPalette palette, int version)
    {
        var themeHex = ThemeHelper.ToHexColor(palette.ThemeColor);

        return TryRunOnUiThread(() =>
        {
            if (version != Volatile.Read(ref _applyVersion))
                return;

            if (string.Equals(ThemeColorHex, themeHex, StringComparison.OrdinalIgnoreCase))
                return;

            ThemeColorHex = themeHex;
            ThemeHelper.ApplyThemeColor(themeHex);
            WeakReferenceMessenger.Default.Send(new TextStyleChangedMessage());

            DebugLog.Info("AutoThemeColorService",
                $"Auto theme color applied: theme={themeHex}, source={ThemeHelper.ToHexColor(palette.SourceColor)}, coverage={palette.Coverage:P0}, saturation={palette.Saturation:P0}, flatness={palette.Flatness:P0}, fill={palette.IsFillColor}");
        });
    }

    private static bool TryRunOnUiThread(Action action)
    {
        var dispatcherQueue = App.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue == null)
            return false;

        if (dispatcherQueue.HasThreadAccess)
        {
            action();
            return true;
        }

        return dispatcherQueue.TryEnqueue(() => action());
    }
}