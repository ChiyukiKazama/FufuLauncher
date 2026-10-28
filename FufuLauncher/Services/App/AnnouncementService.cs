/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public class AnnouncementService : IAnnouncementService
{
    private readonly HttpClient _httpClient;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    public AnnouncementService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FufuLauncher");
    }

    public async Task<string?> GetCurrentAnnouncementUrlAsync()
    {
        try
        {
            var json = await GetAnnouncementJsonAsync();
            var data = JsonSerializer.Deserialize<AnnouncementData>(json);

            if (data != null && !string.IsNullOrEmpty(data.Info))
            {
                return data.Info;
            }

            return null;
        }
        catch (Exception ex)
        {
            DebugLog.Info("AnnouncementService",
                $"[AnnouncementService] Failed to get current announcement URL: {ex.Message}");
            return null;
        }
    }

    private async Task<string> GetAnnouncementJsonAsync()
    {
        try
        {
            return await _httpClient.GetStringAsync(ApiEndpoints.AnnouncementUrl);
        }
        catch
        {
            return await _httpClient.GetStringAsync(ApiEndpoints.AnnouncementFallbackUrl);
        }
    }

    public async Task<string?> CheckForNewAnnouncementAsync()
    {
        await _checkGate.WaitAsync();
        try
        {
            var remoteUrl = await GetCurrentAnnouncementUrlAsync();

            if (string.IsNullOrEmpty(remoteUrl))
            {
                return null;
            }

            string localUrl = string.Empty;
            var cachedUrlObj =
                await _localSettingsService.ReadSettingAsync(LocalSettingsService.LastAnnouncementUrlKey);
            if (cachedUrlObj is string cachedUrl)
            {
                localUrl = cachedUrl;
            }

            if (string.Equals(remoteUrl, localUrl, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (await ShouldSuppressDuringGameAsync())
            {
                return null;
            }

            await _localSettingsService.SaveSettingAsync(LocalSettingsService.LastAnnouncementUrlKey, remoteUrl);
            return remoteUrl;
        }
        catch (Exception ex)
        {
            DebugLog.Info("AnnouncementService",
                $"[AnnouncementService] Error in announcement update check: {ex.Message}");
            return null;
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private async Task<bool> ShouldSuppressDuringGameAsync()
    {
        try
        {
            var settingObj =
                await _localSettingsService.ReadSettingAsync(LocalSettingsService.SuppressAnnouncementInGameKey);
            bool isEnabled = settingObj == null || Convert.ToBoolean(settingObj);

            if (!isEnabled)
            {
                return false;
            }

            return await GameProcessHelper.IsGameRunningAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("AnnouncementService",
                $"[AnnouncementService] Error checking game running state: {ex.Message}");
            return false;
        }
    }
}