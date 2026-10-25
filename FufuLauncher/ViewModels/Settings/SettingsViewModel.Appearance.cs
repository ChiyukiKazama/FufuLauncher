/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.ViewModels;

public partial class SettingsViewModel
{
    #region 外观与样式

    private async Task ResetLaunchButtonOverlayColorAsync()
    {
        LaunchButtonOverlayColor = "#0078D7";
        await _localSettingsService.SaveSettingAsync("LaunchButtonOverlayColor", "#0078D7");
        WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
    }

    private async Task ResetAppThemeColorAsync()
    {
        var systemColor = GetSystemAccentColor();

        AppThemeColor = string.Empty;

        if (_appThemeColorObj != systemColor)
        {
            _appThemeColorObj = systemColor;
            OnPropertyChanged(nameof(AppThemeColorObj));
        }

        ThemeHelper.ApplyThemeColor(ToHexColor(systemColor));
        WeakReferenceMessenger.Default.Send(new AcrylicSettingChangedMessage(true));
        await _localSettingsService.SaveSettingAsync("AppThemeColor", string.Empty);
    }

    private static Windows.UI.Color GetSystemAccentColor() => ThemeHelper.GetSystemAccentColor();

    partial void OnLaunchButtonOverlayColorChanged(string value)
    {
        if (TryParseColor(value, out var color) && _launchButtonOverlayColorObj != color)
        {
            _launchButtonOverlayColorObj = color;
            OnPropertyChanged(nameof(LaunchButtonOverlayColorObj));
        }

        if (_isInitializing) return;
        _localSettingsService.SaveSettingAsync("LaunchButtonOverlayColor", value);
        WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
    }

    private void OnLaunchButtonOverlayColorObjChanged(Windows.UI.Color value)
    {
        string hex = ToHexColor(value);
        if (LaunchButtonOverlayColor != hex)
        {
            LaunchButtonOverlayColor = hex;
        }
    }

    partial void OnIsAcrylicOverlayEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("IsAcrylicOverlayEnabled", value);
        WeakReferenceMessenger.Default.Send(new OverlayStyleChangedMessage(value));
    }

    partial void OnIsPageOverlaySemiTransparentEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("IsPageOverlaySemiTransparentEnabled", value);
        WeakReferenceMessenger.Default.Send(new PageOverlayOpacityModeChangedMessage(value));
    }

    partial void OnPageOverlayTargetOpacityChanged(double value)
    {
        if (_isInitializing) return;
        var clamped = Math.Clamp(value, 0.1, 1.0);
        if (Math.Abs(clamped - value) > 0.0001)
        {
            PageOverlayTargetOpacity = clamped;
            return;
        }

        _ = _localSettingsService.SaveSettingAsync("PageOverlayTargetOpacity", clamped);
        WeakReferenceMessenger.Default.Send(new PageOverlayTargetOpacityChangedMessage(clamped));
    }

    partial void OnAppThemeColorChanged(string value)
    {
        if (!_isInitializing)
            _localSettingsService.SaveSettingAsync("AppThemeColor", value);

        if (TryParseColor(value, out var color) && _appThemeColorObj != color)
        {
            _appThemeColorObj = color;
            OnPropertyChanged(nameof(AppThemeColorObj));
        }

        if (_isInitializing || IsAutoThemeColorEnabled) return;

        WeakReferenceMessenger.Default.Send(new AcrylicSettingChangedMessage(true));
        ThemeHelper.ApplyThemeColor(value);
    }

    partial void OnAppThemeColorObjChanged(Windows.UI.Color value)
    {
        string hex = ToHexColor(value);
        if (AppThemeColor != hex)
        {
            AppThemeColor = hex;
        }
    }

    partial void OnIsAutoThemeColorEnabledChanged(bool value)
    {
        if (_isInitializing) return;

        _ = SaveAndApplyAutoThemeColorAsync(value);
        ApplyAutoThemeColorPreview();
    }

    private async Task SaveAndApplyAutoThemeColorAsync(bool isEnabled)
    {
        await _localSettingsService.SaveSettingAsync(AutoThemeColorService.SettingKey, isEnabled);

        if (!isEnabled)
        {
            await _autoThemeColorService.RestoreManualColorsAsync();
            return;
        }

        await ApplyAutoThemeColorFromCurrentBackgroundAsync();
    }

    private async Task ApplyAutoThemeColorFromCurrentBackgroundAsync()
    {
        try
        {
            var applied = await _autoThemeColorService.ApplyForCurrentBackgroundAsync();
            if (!applied)
            {
                DebugLog.Info("SettingsViewModel", "Auto theme color could not be derived from the current background");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsViewModel", $"Auto theme color apply failed: {ex.Message}");
        }
    }

    private void ApplyAutoThemeColorPreview()
    {
        var hex = _autoThemeColorService.ThemeColorHex;

        if (!IsAutoThemeColorEnabled || string.IsNullOrWhiteSpace(hex) || !TryParseColor(hex, out var color))
        {
            AutoThemeColorPreviewText = string.Empty;
            AutoThemeColorPreviewVisibility = Visibility.Collapsed;
            return;
        }

        AutoThemeColorPreviewBrush = new SolidColorBrush(color);
        AutoThemeColorPreviewText = ToHexColor(color);
        AutoThemeColorPreviewVisibility = Visibility.Visible;
    }

    partial void OnGlobalBackgroundImageOpacityChanged(double value)
    {
        var clamped = Math.Clamp(value, 0.0, 1.0);
        if (Math.Abs(clamped - value) > 0.0001)
        {
            GlobalBackgroundImageOpacity = clamped;
            return;
        }

        if (_isInitializing) return;

        _ = _localSettingsService.SaveSettingAsync("GlobalBackgroundImageOpacity", clamped);
        WeakReferenceMessenger.Default.Send(new BackgroundImageOpacityChangedMessage(clamped));
    }

    partial void OnPanelBackgroundOpacityChanged(double value)
    {
        var clamped = Math.Clamp(value, 0.0, 1.0);

        if (Math.Abs(clamped - value) > 0.001)
        {
            PanelBackgroundOpacity = clamped;
            return;
        }

        if (_isInitializing) return;

        _localSettingsService.SaveSettingAsync("PanelBackgroundOpacity", clamped);

        WeakReferenceMessenger.Default.Send(new PanelOpacityChangedMessage(clamped));
    }

    partial void OnGameNewsCardTextColorChanged(string value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("GameNewsCardTextColor", value);
        WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
    }

    partial void OnGameNewsCardTextOpacityChanged(double value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("GameNewsCardTextOpacity", value);
        WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
    }

    partial void OnLaunchButtonTextColorChanged(string value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("LaunchButtonTextColor", value);
        WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
    }

    partial void OnLaunchButtonTextOpacityChanged(double value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("LaunchButtonTextOpacity", value);
        WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
    }

    partial void OnGameCheckinTextColorChanged(string value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("GameCheckinTextColor", value);
        WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
    }

    partial void OnGameCheckinTextOpacityChanged(double value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("GameCheckinTextOpacity", value);
        WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
    }

    partial void OnGlobalBackgroundOverlayOpacityChanged(double value)
    {
        var clamped = Math.Clamp(value, 0.0, 1.0);

        if (Math.Abs(clamped - value) > 0.0001)
        {
            GlobalBackgroundOverlayOpacity = clamped;
            return;
        }

        if (_isInitializing) return;

        _ = _localSettingsService.SaveSettingAsync("GlobalBackgroundOverlayOpacity", clamped);
        WeakReferenceMessenger.Default.Send(new BackgroundOverlayOpacityChangedMessage(clamped));
    }

    partial void OnContentFrameBackgroundOpacityChanged(double value)
    {
        var clamped = Math.Clamp(value, 0.0, 1.0);
        if (Math.Abs(clamped - value) > 0.0001)
        {
            ContentFrameBackgroundOpacity = clamped;
            return;
        }

        if (_isInitializing) return;

        _ = _localSettingsService.SaveSettingAsync("ContentFrameBackgroundOpacity", clamped);
        WeakReferenceMessenger.Default.Send(new FrameBackgroundOpacityChangedMessage(clamped));
    }

    private static bool TryParseColor(string? value, out Windows.UI.Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var hex = value.Trim();
        if (hex.StartsWith("#")) hex = hex[1..];
        if (hex.Length == 3)
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        if (hex.Length != 6 && hex.Length != 8) return false;

        try
        {
            var offset = 0;
            byte a = 255;
            if (hex.Length == 8)
            {
                a = Convert.ToByte(hex[..2], 16);
                offset = 2;
            }

            color = Windows.UI.Color.FromArgb(
                a,
                Convert.ToByte(hex.Substring(offset, 2), 16),
                Convert.ToByte(hex.Substring(offset + 2, 2), 16),
                Convert.ToByte(hex.Substring(offset + 4, 2), 16));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ToHexColor(Windows.UI.Color color) => ThemeHelper.ToHexColor(color);

    #endregion
}