/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Globalization;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class PluginConfigPage
{
    private void StartGamepadWatcher()
    {
        if (!_isActive || _configuration == null ||
            !_configuration.Options.Any(option => option.UsesGamepadChoices)) return;
        if (_gamepadWatcher == null)
        {
            try
            {
                _gamepadWatcher = new GamepadConnectionWatcher(() => DispatcherQueue.TryEnqueue(() =>
                {
                    if (_isActive && _gamepadWatcher != null) RefreshGamepadChoices();
                }));
            }
            catch (Exception ex)
            {
                DebugLog.Info("PluginConfigPage.Save",
                    $"[GamepadChoices] Device notifications unavailable: {ex.Message}");
            }
        }

        RefreshGamepadChoices();
    }

    private void RefreshGamepadChoices()
    {
        if (_configuration == null) return;
        var families = GamepadConnectionWatcher.ReadConnectedFamilies();
        foreach (var option in _configuration.Options) option.SetConnectedGamepads(families);
    }

    private void StopGamepadWatcher()
    {
        _gamepadWatcher?.Dispose();
        _gamepadWatcher = null;
    }

    private async void OnAutoSaveTimerTick(DispatcherQueueTimer sender, object args)
    {
        await SavePendingChangesAsync();
    }

    private Task<bool> SavePendingChangesAsync()
    {
        _autoSaveTimer.Stop();
        if (_configuration == null || _loadedConfigPath == null)
        {
            return Task.FromResult(true);
        }

        if (_saveTask is { IsCompleted: false })
        {
            return _saveTask;
        }

        return _saveTask = SaveConfigurationAsync(_configuration, _loadedConfigPath);
    }

    private async Task<bool> SaveConfigurationAsync(PluginConfiguration configuration, string path)
    {
        try
        {
            if (configuration.HasChanges)
            {
                if (_isActive)
                {
                    SetSaveStatus("PluginConfigPage_Saving".GetLocalized(), "\uE895");
                }

                await configuration.SaveAsync(path);
            }

            if (_isActive && ReferenceEquals(_configuration, configuration))
            {
                SetSaveStatus("DeviceInfo_Saved".GetLocalized(), "\uE73E");
                if (_hasSaveError)
                {
                    ConfigMessageBar.IsOpen = false;
                    _hasSaveError = false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginConfigPage.Save", $"[PluginConfig] Save failed: {ex}");
            if (_isActive && ReferenceEquals(_configuration, configuration))
            {
                SetSaveStatus("PluginConfigPage_SaveFailed".GetLocalized(), "\uE783");
                ShowError("PluginConfigPage_SaveFailed".GetLocalized(),
                    string.Format(CultureInfo.CurrentCulture, "PluginConfigPage_SaveFailedMessage".GetLocalized(),
                        ex.Message),
                    RetrySaveAsync);
                _hasSaveError = true;
            }

            return false;
        }
    }

    private void SetSaveStatus(string text, string glyph)
    {
        SaveStatusTextBlock.Text = text;
        SaveStatusIcon.Glyph = glyph;
    }

    private void ShowError(string title, string message, Func<Task> retryAction)
    {
        _retryAction = retryAction;
        _hasSaveError = false;
        ConfigMessageBar.Title = title;
        ConfigMessageBar.Message = message;
        ConfigMessageBar.IsOpen = true;
    }

    private void ShowLoadError(string title, string message, Func<Task> retryAction)
    {
        _retryAction = retryAction;
        _hasSaveError = false;
        ConfigLoadErrorBar.Title = title;
        ConfigLoadErrorBar.Message = message;
        ConfigLoadErrorBar.IsOpen = true;

        var lockProcesses = FileLockHelper.DescribeFileUsers(_pluginItem?.ConfigFilePath);
        ConfigLockInfoBar.Message = lockProcesses ?? string.Empty;
        ConfigLockInfoBar.IsOpen = lockProcesses != null;

        ConfigLoadErrorOverlay.Visibility = Visibility.Visible;
        ConfigMessageBar.IsOpen = false;
    }

    private async Task RetrySaveAsync()
    {
        await SavePendingChangesAsync();
    }

    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isRetrying || _retryAction == null)
        {
            return;
        }

        _isRetrying = true;
        try
        {
            await _retryAction();
        }
        finally
        {
            _isRetrying = false;
        }
    }

    private async void OnReloadClick(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isNavigatingBack)
        {
            return;
        }

        ReloadButton.Focus(FocusState.Programmatic);
        if (await SavePendingChangesAsync() && _isActive)
        {
            await LoadConfigAsync();
        }
    }
}