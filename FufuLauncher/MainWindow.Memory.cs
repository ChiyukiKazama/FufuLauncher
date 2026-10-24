/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Services;
using Microsoft.UI.Windowing;
using FufuLauncher.Helpers;

namespace FufuLauncher;

public sealed partial class MainWindow
{
    #region Memory Management

    private void FlushMemory(bool force = false)
    {
        if (!_isMemoryReleaseEnabled)
            return;

        try
        {
            var isMinimized = AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped &&
                              ((OverlappedPresenter)AppWindow.Presenter).State == OverlappedPresenterState.Minimized;

            var isHidden = !Visible;
            var shouldFlush = force || isMinimized || isHidden;
            if (!shouldFlush)
                return;

            if ((isMinimized || isHidden) && ContentFrame.BackStackDepth > 0)
                ContentFrame.BackStack.Clear();

            MemoryOptimizationService.FlushMemory();
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Memory", $"Memory cleanup exception: {ex.Message}");
        }
    }

    private void ReleaseMemoryAfterSuspend()
    {
        try
        {
            var isMinimized = AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped &&
                              ((OverlappedPresenter)AppWindow.Presenter).State == OverlappedPresenterState.Minimized;
            var isHidden = !Visible;

            if ((isMinimized || isHidden) && ContentFrame.BackStackDepth > 0)
                ContentFrame.BackStack.Clear();

            MemoryOptimizationService.FlushMemory();

            using var process = Process.GetCurrentProcess();
            DebugLog.Info("MainWindow.Memory",
                $"Memory released right after suspension, working set {process.WorkingSet64 / 1048576.0:F1} MB");
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Memory", $"Exception while releasing memory after suspension: {ex.Message}");
        }
    }

    private void OnMemoryOptimizationTick(object sender, object e)
    {
        _memoryOptimizationTimer.Stop();
        PerformMemoryOptimization();
    }

    private void PerformMemoryOptimization()
    {
        if (_isSuspended) return;
        _isSuspended = true;

        try
        {
            if (_globalBackgroundPlayer != null && _globalBackgroundPlayer.PlaybackSession != null)
            {
                try
                {
                    if (_globalBackgroundPlayer.PlaybackSession.CanPause)
                    {
                        _globalBackgroundPlayer.Pause();
                    }

                    _suspendedVideoSource = _globalBackgroundPlayer.Source;
                    _globalBackgroundPlayer.Source = null;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Memory", $"Exception while suspending media playback: {ex.Message}");
        }

        _slideshowTimer?.Stop();

        _networkMonitorService.Stop();
        _messageDismissTimer.Stop();

        _announcementCheckTimer.Stop();

        DebugLog.Info("MainWindow.Memory", "App suspended");
        ReleaseMemoryAfterSuspend();
    }

    private void RestoreFromSuspension()
    {
        _memoryOptimizationTimer.Stop();

        if (!_isSuspended) return;
        _isSuspended = false;

        try
        {
            if (_isVideoBackground && _globalBackgroundPlayer != null)
            {
                if (_suspendedVideoSource != null)
                {
                    _globalBackgroundPlayer.Source = _suspendedVideoSource;
                    _suspendedVideoSource = null;
                }

                _globalBackgroundPlayer.Play();
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Memory", $"Exception while resuming media playback: {ex.Message}");
        }

        _slideshowTimer?.Start();

        if (!_networkMonitorService.IsEnabled)
        {
            _networkMonitorService.Start();
        }

        if (!_announcementCheckTimer.IsEnabled)
        {
            _announcementCheckTimer.Start();
        }

        DebugLog.Info("MainWindow.Memory", "App resumed");
    }

    #endregion
}