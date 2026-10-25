/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Background;

public static class BackgroundImageAntialiasSettings
{
    public const string EnabledSettingKey = "IsBackgroundImageAntialiasEnabled";
    public const string StrengthSettingKey = "BackgroundImageAntialiasStrength";
    public const int DefaultStrength = 50;

    private static readonly SemaphoreSlim LoadLock = new(1, 1);
    private static readonly SemaphoreSlim SaveLock = new(1, 1);
    private static bool? _cachedEnabled;
    private static int? _cachedStrength;

    public static async Task<(bool Enabled, int Strength)> GetAsync()
    {
        if (_cachedEnabled.HasValue && _cachedStrength.HasValue)
            return (_cachedEnabled.Value, _cachedStrength.Value);

        await LoadLock.WaitAsync();
        try
        {
            if (_cachedEnabled.HasValue && _cachedStrength.HasValue)
                return (_cachedEnabled.Value, _cachedStrength.Value);

            try
            {
                var settings = App.GetService<ILocalSettingsService>();
                var enabledObj = await settings.ReadSettingAsync(EnabledSettingKey);
                var enabled = false;
                if (enabledObj != null)
                {
                    if (!bool.TryParse(enabledObj.ToString(), out enabled) &&
                        int.TryParse(enabledObj.ToString(), out var numericEnabled))
                    {
                        enabled = numericEnabled != 0;
                    }
                }

                var strengthObj = await settings.ReadSettingAsync(StrengthSettingKey);
                var strength = int.TryParse(strengthObj?.ToString(), out var parsedStrength)
                    ? Math.Clamp(parsedStrength, 0, 100)
                    : DefaultStrength;

                _cachedEnabled = enabled;
                _cachedStrength = strength;
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundImageAntialiasSettings", $"Failed to load antialias settings: {ex.Message}");
                _cachedEnabled = false;
                _cachedStrength = DefaultStrength;
            }

            return (_cachedEnabled.Value, _cachedStrength.Value);
        }
        finally
        {
            LoadLock.Release();
        }
    }

    public static async Task SetEnabledAsync(bool enabled)
    {
        _cachedEnabled = enabled;
        await SaveLock.WaitAsync();
        try
        {
            var settings = App.GetService<ILocalSettingsService>();
            await settings.SaveSettingAsync(EnabledSettingKey, enabled);
        }
        finally
        {
            SaveLock.Release();
        }
    }

    public static async Task SetStrengthAsync(int strength)
    {
        var normalized = Math.Clamp(strength, 0, 100);
        _cachedStrength = normalized;
        await SaveLock.WaitAsync();
        try
        {
            var settings = App.GetService<ILocalSettingsService>();
            await settings.SaveSettingAsync(StrengthSettingKey, normalized);
        }
        finally
        {
            SaveLock.Release();
        }
    }

    public static float GetBlurSigma(int strength)
    {
        var normalized = Math.Clamp(strength, 0, 100);
        return normalized <= 0 ? 0f : 0.15f + normalized / 100f * 1.85f;
    }
}