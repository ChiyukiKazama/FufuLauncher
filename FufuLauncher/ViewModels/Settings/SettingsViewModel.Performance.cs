/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using FufuLauncher.Helpers;

namespace FufuLauncher.ViewModels;

public partial class SettingsViewModel
{
    #region 性能与进程

    partial void OnIsCpuUsageWarningEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync(ProcessCpuUsageMonitor.IsEnabledSettingKey, value);
    }

    partial void OnIsMemoryReleaseEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync(MemoryOptimizationService.IsEnabledSettingKey, value);
        WeakReferenceMessenger.Default.Send(new MemoryReleaseEnabledChangedMessage(value));
    }

    partial void OnCpuUsageWarningThresholdChanged(double value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync(ProcessCpuUsageMonitor.ThresholdSettingKey,
            Math.Clamp(value, 5.0, 100.0));
    }

    private async Task ResetCpuUsageWarningSettingsAsync()
    {
        IsCpuUsageWarningEnabled = true;
        CpuUsageWarningThreshold = ProcessCpuUsageMonitor.DefaultCpuThreshold;
        await _localSettingsService.SaveSettingAsync(ProcessCpuUsageMonitor.IsEnabledSettingKey, true);
        await _localSettingsService.SaveSettingAsync(ProcessCpuUsageMonitor.ThresholdSettingKey,
            ProcessCpuUsageMonitor.DefaultCpuThreshold);
    }

    partial void OnAppProcessPriorityChanged(AppProcessPriority value)
    {
        if (!_isInitializing)
            _localSettingsService.SaveSettingAsync("AppProcessPriority", (int)value);

        ApplyProcessPriority(value);
    }

    private void ApplyProcessPriority(AppProcessPriority priority)
    {
        try
        {
            var process = Process.GetCurrentProcess();
            switch (priority)
            {
                case AppProcessPriority.Normal:
                    process.PriorityClass = ProcessPriorityClass.Normal;
                    break;
                case AppProcessPriority.AboveNormal:
                    process.PriorityClass = ProcessPriorityClass.AboveNormal;
                    break;
                case AppProcessPriority.High:
                    process.PriorityClass = ProcessPriorityClass.High;
                    break;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsViewModel.Performance", $"Failed to set process priority: {ex.Message}");
        }
    }

    partial void OnIsBetterGIIntegrationEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        DebugLog.Info("SettingsViewModel.Performance",
            $"SettingsViewModel: BetterGI integration setting changed to {value}");
        _ = _localSettingsService.SaveSettingAsync("IsBetterGIIntegrationEnabled", value);
    }

    partial void OnIsBetterGICloseOnExitEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        DebugLog.Info("SettingsViewModel.Performance",
            $"SettingsViewModel: BetterGI exit-with-game setting changed to {value}");
        _ = _localSettingsService.SaveSettingAsync("IsBetterGICloseOnExitEnabled", value);
    }

    #endregion
}