/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Views;
using Microsoft.UI.Xaml;

namespace FufuLauncher.ViewModels;

public partial class MainViewModel
{
    #region 游戏启动与进程监控
    [ObservableProperty] private bool _isGameNotLaunching;

    [ObservableProperty] private string _launchButtonText = "LaunchBtn_SelectPath".GetLocalized();
    [ObservableProperty] private bool _isLaunchButtonEnabled = true;
    [ObservableProperty] private bool _isGameLaunching;

    [ObservableProperty] private bool _isGameRunning;
    [ObservableProperty] private string _launchButtonIcon = "\uE768";

    private List<string> _cachedProcessNames;

    private CancellationTokenSource _gameMonitoringCts;
    private bool _cachedGameRunning;
    private DateTimeOffset _lastGameProcessCheck = DateTimeOffset.MinValue;

    private CancellationTokenSource _launchCts;
    private DateTime _lastLaunchButtonPressTime = DateTime.MinValue;
    private static readonly TimeSpan LaunchButtonCooldown = TimeSpan.FromSeconds(1);

    partial void OnIsGameLaunchingChanged(bool value) => IsGameNotLaunching = !value;

    public IAsyncRelayCommand LaunchGameCommand
    {
        get;
    }

    private async Task<List<string>> GetTargetProcessNamesAsync()
    {
        if (_cachedProcessNames == null)
        {
            var exeNames = await FufuLauncher.Helpers.GameExeManager.GetExeNamesAsync();
            _cachedProcessNames = exeNames.Select(System.IO.Path.GetFileNameWithoutExtension).ToList();
        }
        return _cachedProcessNames;
    }

    public void UpdateLaunchButtonState()
    {
        var pathTask = _localSettingsService.ReadSettingAsync("GameInstallationPath");
        var savedPath = pathTask.Result as string;

        var hasPath = !string.IsNullOrEmpty(savedPath) &&
                      Directory.Exists(savedPath.Trim('"').Trim());

        if (IsGameRunning)
        {
            LaunchButtonText = "LaunchBtn_ExitGame".GetLocalized();
            LaunchButtonIcon = "\uE711";
        }
        else
        {
            if (hasPath)
            {
                LaunchButtonText = "LaunchBtn_StartGame".GetLocalized();
            }
            else
            {
                LaunchButtonText = "LaunchBtn_SelectPath".GetLocalized();
            }

            LaunchButtonIcon = "\uE768";
        }

        OnPropertyChanged(nameof(LaunchButtonText));
        OnPropertyChanged(nameof(LaunchButtonIcon));

        IsLaunchButtonEnabled = true;
    }

    private async Task LaunchGameAsync()
    {
        var now = DateTime.UtcNow;
        if (now - _lastLaunchButtonPressTime <= LaunchButtonCooldown)
        {
            return;
        }
        _lastLaunchButtonPressTime = now;

        if (IsGameLaunching)
        {
            _launchCts?.Cancel();
            return;
        }

        await ForceRefreshGameStateAsync();

        if (IsGameRunning)
        {
            await TerminateGameAsync();
            await Task.Delay(1200);
            await ForceRefreshGameStateAsync();
            return;
        }

        if (!_gameLauncherService.IsGamePathSelected())
        {
            _notificationService.Show("LaunchErr_NoGamePath".GetLocalized(), "LaunchErr_NoGamePathMsg".GetLocalized(), NotificationType.Error, 0);
            return;
        }

        IsGameLaunching = true;
        LaunchButtonText = "LaunchBtn_Launching".GetLocalized();
        OnPropertyChanged(nameof(LaunchButtonText));
        _launchCts = new CancellationTokenSource();

        try
        {
            if (await _gameLauncherService.GetUseInjectionAsync())
                await DeviceBanConsentDialog.ShowIfNeededAsync(App.GetService<FufuLauncher.Services.MotherboardBanService>());

            var result = await _gameLauncherService.LaunchGameAsync(_launchCts.Token);

            if (result.Cancelled)
            {
                _notificationService.Show("LaunchCancelled_Title".GetLocalized(), "LaunchCancelled_Msg".GetLocalized(), NotificationType.Information, 3000);
                return;
            }

            if (result.Success)
            {
                await ForceRefreshGameStateAsync();
                await ApplyPostLaunchBehaviorAsync();
            }
            else if (result.PluginDllConflicts.Count > 0)
            {
                if (!await PluginDllConflictDialog.ShowAsync(result.PluginDllConflicts))
                {
                    _notificationService.Show("LaunchErr_LaunchFailed".GetLocalized(), result.ErrorMessage, NotificationType.Error, 0);
                }
            }
            else
            {
                _notificationService.Show("LaunchErr_LaunchFailed".GetLocalized(), result.ErrorMessage, NotificationType.Error, 0);
            }
        }
        finally
        {
            IsGameLaunching = false;
            IsLaunchButtonEnabled = true;
            await ForceRefreshGameStateAsync();
            UpdateLaunchButtonState();
        }
    }

    private async Task ApplyPostLaunchBehaviorAsync()
    {
        var obj = await _localSettingsService.ReadSettingAsync("PostLaunchBehavior");
        if (obj is not string s || !Enum.TryParse<PostLaunchBehavior>(s, out var behavior))
            return;

        switch (behavior)
        {
            case PostLaunchBehavior.MinimizeToTray:
                _dispatcherQueue.TryEnqueue(() =>
                {
                    App.MainWindow.Hide();
                });
                break;

            case PostLaunchBehavior.Exit:
                await SaveStateBeforeExitAsync();
                _dispatcherQueue.TryEnqueue(() =>
                {
                    Application.Current.Exit();
                });
                break;
        }
    }

    private async Task SaveStateBeforeExitAsync()
    {
        try
        {
            var windowSaveService = App.GetService<ILocalSettingsService>();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            if (appWindow != null)
            {
                var size = appWindow.Size;
                await windowSaveService.SaveSettingAsync("WindowWidth", size.Width);
                await windowSaveService.SaveSettingAsync("WindowHeight", size.Height);
            }
        }
        catch
        {
            // 保存状态失败不影响退出
        }
    }

    private async Task ForceRefreshGameStateAsync()
    {
        bool actualState = await CheckGameProcessRunningAsync(forceRefresh: true);
        if (actualState != IsGameRunning)
        {
            await SetGameRunningStateAsync(actualState);
        }
    }

    private async Task<bool> CheckGameProcessRunningAsync(bool forceRefresh = false)
    {
        var now = DateTimeOffset.UtcNow;
        var currentInterval = IsGameRunning ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(1);

        if (!forceRefresh && now - _lastGameProcessCheck < currentInterval)
        {
            return _cachedGameRunning;
        }

        try
        {
            var processNames = await GetTargetProcessNamesAsync();
            _cachedGameRunning = processNames.Any(HasRunningProcess);
        }
        catch
        {
            _cachedGameRunning = false;
        }

        _lastGameProcessCheck = now;
        return _cachedGameRunning;
    }

    private static bool HasRunningProcess(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (!process.HasExited) return true;
            }
        }

        return false;
    }

    private async Task SetGameRunningStateAsync(bool isRunning, string temporaryText = null)
    {
        bool stateChanged = IsGameRunning != isRunning;

        await UpdateUI(() =>
        {
            IsGameRunning = isRunning;
            LaunchButtonIcon = isRunning ? "\uE711" : "\uE768";

            if (temporaryText != null)
            {
                LaunchButtonText = temporaryText;
            }
            else
            {
                UpdateLaunchButtonState();
            }

            OnPropertyChanged(nameof(LaunchButtonText));
            OnPropertyChanged(nameof(LaunchButtonIcon));
            OnPropertyChanged(nameof(IsGameRunning));
        });

        if (stateChanged)
        {
            WeakReferenceMessenger.Default.Send(new GameRunningStateChangedMessage(isRunning));
        }
    }

    private async Task TerminateGameAsync()
    {
        IsLaunchButtonEnabled = false;
        await SetGameRunningStateAsync(true, "LaunchBtn_Terminating".GetLocalized());

        try
        {
            var savedPathObj = await _localSettingsService.ReadSettingAsync("GameInstallationPath");
            var gamePath = savedPathObj?.ToString()?.Trim('"')?.Trim();

            var exeNames = await Helpers.GameExeManager.GetExeNamesAsync();
            var processNames = exeNames.Select(Path.GetFileNameWithoutExtension).ToList();

            var processes = new List<Process>();
            foreach (var name in processNames)
            {
                processes.AddRange(Process.GetProcessesByName(name));
            }

            if (processes.Count == 0)
            {
                await SetGameRunningStateAsync(false);
                UpdateLaunchButtonState();
                return;
            }

            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited) continue;

                    if (!string.IsNullOrEmpty(gamePath))
                    {
                        try
                        {
                            var processPath = process.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(processPath) &&
                                !processPath.StartsWith(gamePath, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (Win32Exception)
                        {
                            // ignored
                        }
                        catch (InvalidOperationException) { continue; }
                    }

                    process.Kill();
                    await process.WaitForExitAsync();
                }
                catch
                {
                    // ignored
                }
            }

            try
            {
                await _gameLauncherService.StopBetterGIAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"关闭 BetterGI 时发生错误: {ex.Message}");
            }

            await Task.Delay(1000);
            await SetGameRunningStateAsync(false);
            UpdateLaunchButtonState();
        }
        catch (Exception ex)
        {
            _notificationService.Show("终止失败", ex.Message, NotificationType.Error, 0);
            await SetGameRunningStateAsync(false);
            UpdateLaunchButtonState();
        }
        finally
        {
            IsLaunchButtonEnabled = true;
        }
    }

    private async Task StartGameMonitoringLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                bool currentState = await CheckGameProcessRunningAsync();

                if (currentState != IsGameRunning)
                {
                    await SetGameRunningStateAsync(currentState);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"进程监控错误: {ex.Message}");
            }

            int checkDelay = IsGameRunning ? 1000 : 1000;
            await Task.Delay(checkDelay, token);
        }
    }
    #endregion
}
