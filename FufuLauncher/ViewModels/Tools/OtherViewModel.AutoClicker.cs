/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.Services;
using Windows.System;

namespace FufuLauncher.ViewModels;

public partial class OtherViewModel
{
    #region Auto Clicker

    private void StartRecordingTriggerKey()
    {
        IsRecordingTriggerKey = true;
        IsRecordingClickKey = false;
        IsRecordingStopKey = false;
        DebugLog.Info("OtherViewModel.AutoClicker", "[OtherViewModel] Start recording trigger key");
    }

    private void StartRecordingClickKey()
    {
        IsRecordingClickKey = true;
        IsRecordingTriggerKey = false;
        IsRecordingStopKey = false;
        DebugLog.Info("OtherViewModel.AutoClicker", "[OtherViewModel] Start recording click key");
    }

    private void StartRecordingStopKey()
    {
        IsRecordingStopKey = true;
        IsRecordingTriggerKey = false;
        IsRecordingClickKey = false;
        DebugLog.Info("OtherViewModel.AutoClicker", "[OtherViewModel] Start recording stop hotkey");
    }

    partial void OnIsAutoClickerEnabledChanged(bool value)
    {
        if (_isInitializing || _isReverting) return;

        if (value)
        {
            if (IsMouseModeEnabled() && !HasStopKey())
            {
                StatusMessage = "AutoClicker_SetStopKeyBeforeMouse".GetLocalized();
                RevertAutoClickerToggle(false);
                RevertMouseClickerToggles();
                return;
            }

            DebugLog.Info("OtherViewModel.AutoClicker",
                "[OtherViewModel] Enable request intercepted, showing risk warning");
            _ = HandleAutoClickerEnableRequestAsync();
        }
        else
        {
            _autoClickerService.IsEnabled = false;
            _ = SaveSettingsAsync();
            DebugLog.Info("OtherViewModel.AutoClicker",
                $"[OtherViewModel] Auto-clicker enabled state changed: {value}");
        }
    }

    partial void OnIsMouseLeftClickerEnabledChanged(bool value)
    {
        if (_isInitializing || _isReverting) return;

        if (value)
        {
            _isReverting = true;
            IsMouseRightClickerEnabled = false;
            _isReverting = false;
        }

        ApplyClickerModeFromSelection();
    }

    partial void OnIsMouseRightClickerEnabledChanged(bool value)
    {
        if (_isInitializing || _isReverting) return;

        if (value)
        {
            _isReverting = true;
            IsMouseLeftClickerEnabled = false;
            _isReverting = false;
        }

        ApplyClickerModeFromSelection();
    }

    private async Task HandleAutoClickerEnableRequestAsync()
    {
        bool confirmed = await ShowLatencyWarningDialogAsync();

        if (!confirmed)
        {
            DebugLog.Info("OtherViewModel.AutoClicker", "[OtherViewModel] User cancelled enabling auto-clicker");
            RevertAutoClickerToggle(false);
            return;
        }

        if (!IsAdministrator())
        {
            DebugLog.Info("OtherViewModel.AutoClicker",
                "[OtherViewModel] Attempt to enable auto-clicker blocked: no admin privileges");
            RevertAutoClickerToggle(false);
            _ = ShowAdminRequiredDialogAsync();
            return;
        }

        _autoClickerService.Mode = GetCurrentMode();
        _autoClickerService.IsEnabled = true;
        _ = SaveSettingsAsync();
        DebugLog.Info("OtherViewModel.AutoClicker", "[OtherViewModel] Auto-clicker enabled state changed: True");
    }

    public void UpdateKey(string keyType, VirtualKey key)
    {
        var keyStr = key.ToString();
        DebugLog.Info("OtherViewModel.AutoClicker", $"[OtherViewModel] Key updated - type: {keyType}, key: {keyStr}");

        if (keyType == "Trigger")
        {
            TriggerKey = keyStr;
            _autoClickerService.TriggerKey = key;
        }
        else if (keyType == "Click")
        {
            ClickKey = keyStr;
            _autoClickerService.ClickKey = key;
        }
        else if (keyType == "Stop")
        {
            StopKey = keyStr;
            _autoClickerService.StopKey = key;
        }

        IsRecordingTriggerKey = false;
        IsRecordingClickKey = false;
        IsRecordingStopKey = false;

        _ = SaveSettingsAsync();
    }

    private void ApplyClickerModeFromSelection()
    {
        var mode = GetCurrentMode();

        if (mode != AutoClickerMode.Keyboard && !HasStopKey())
        {
            StatusMessage = "AutoClicker_SetStopKeyBeforeMouse".GetLocalized();
            RevertMouseClickerToggles();
            return;
        }

        _autoClickerService.Mode = mode;
        _ = SaveSettingsAsync();
    }

    private AutoClickerMode GetCurrentMode()
    {
        if (IsMouseLeftClickerEnabled) return AutoClickerMode.MouseLeft;
        if (IsMouseRightClickerEnabled) return AutoClickerMode.MouseRight;
        return AutoClickerMode.Keyboard;
    }

    private bool IsMouseModeEnabled()
    {
        return IsMouseLeftClickerEnabled || IsMouseRightClickerEnabled;
    }

    private bool HasStopKey()
    {
        return !string.IsNullOrWhiteSpace(StopKey) && Enum.TryParse<VirtualKey>(StopKey, out var key) &&
               key != VirtualKey.None;
    }

    private void RevertAutoClickerToggle(bool value)
    {
        _isReverting = true;
        _dispatcherQueue.TryEnqueue(() =>
        {
            IsAutoClickerEnabled = value;
            _isReverting = false;
        });
    }

    private void RevertMouseClickerToggles()
    {
        _isReverting = true;
        IsMouseLeftClickerEnabled = false;
        IsMouseRightClickerEnabled = false;
        _autoClickerService.Mode = AutoClickerMode.Keyboard;
        _isReverting = false;
        _ = SaveSettingsAsync();
    }

    private void AutoClickerService_IsEnabledChanged(object sender, bool value)
    {
        if (_isInitializing || _isReverting) return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (IsAutoClickerEnabled == value) return;

            _isReverting = true;
            IsAutoClickerEnabled = value;
            _isReverting = false;
            if (!value)
            {
                StatusMessage = "AutoClicker_StoppedByHotkey".GetLocalized();
            }
        });
    }

    #endregion
}