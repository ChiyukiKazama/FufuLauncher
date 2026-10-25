using System.Diagnostics;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using FufuLauncher.Helpers;

namespace FufuLauncher;

public sealed partial class MainWindow
{
    private static readonly TimeSpan NormalMemoryReleaseInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan GameMemoryTelemetryDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GameMemoryTelemetryInterval = TimeSpan.FromSeconds(10);

    private const double MinGameMemoryReleaseMinutes = 3;
    private const double MaxGameMemoryReleaseMinutes = 20;
    private const double NormalGameMemoryReleaseMinutes = 10;

    private readonly SystemResourceMonitor _systemResourceMonitor = new();
    private readonly Stopwatch _gameMemoryTelemetryStopwatch = new();
    private readonly List<(double Cpu, int Memory)> _gameMemorySamples = new();

    private DispatcherTimer? _gameMemoryTelemetryTimer;
    private DispatcherTimer? _gameMemoryReleaseTimer;
    private bool _isGameMemoryStrategyActive;
    private bool _isMemoryReleaseEnabled = true;
    private bool _isGameRunning;

    private void ApplyGameMemoryStrategy(bool isRunning)
    {
        _isGameRunning = isRunning;

        if (!_isMemoryReleaseEnabled || _isExit)
            return;

        if (isRunning)
            StartGameMemoryStrategy();
        else
            StopGameMemoryStrategy();
    }

    private async Task LoadMemoryReleaseSettingAsync()
    {
        try
        {
            var enabledObj =
                await _localSettingsService.ReadSettingAsync(MemoryOptimizationService.IsEnabledSettingKey);
            var isEnabled = enabledObj == null || Convert.ToBoolean(enabledObj);
            dispatcherQueue.TryEnqueue(() => ApplyMemoryReleaseEnabled(isEnabled));
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.MemorySchedule", $"Failed to read memory release switch: {ex.Message}");
        }
    }

    private void ApplyMemoryReleaseEnabled(bool isEnabled)
    {
        if (_isExit)
            return;

        _isMemoryReleaseEnabled = isEnabled;
        if (!isEnabled)
        {
            StopGameMemoryStrategy(restoreNormal: false);
            _periodicMemoryTimer?.Stop();
            DebugLog.Info("MainWindow.MemorySchedule", "[MemorySchedule] Memory release globally disabled");
            return;
        }

        if (_isGameRunning)
            StartGameMemoryStrategy();
        else if (_periodicMemoryTimer != null && !_periodicMemoryTimer.IsEnabled)
            _periodicMemoryTimer.Start();

        DebugLog.Info("MainWindow.MemorySchedule", "[MemorySchedule] Memory release enabled");
    }

    private void StartGameMemoryStrategy()
    {
        if (!_isMemoryReleaseEnabled || !_isGameRunning || _isGameMemoryStrategyActive)
            return;

        _isGameMemoryStrategyActive = true;
        _periodicMemoryTimer?.Stop();
        _gameMemoryTelemetryTimer?.Stop();
        _gameMemoryReleaseTimer?.Stop();
        _gameMemoryReleaseTimer = null;
        _gameMemorySamples.Clear();
        _systemResourceMonitor.ResetCpuBaseline();
        _ = _systemResourceMonitor.SampleCpuUsagePercent();
        _gameMemoryTelemetryStopwatch.Restart();

        _gameMemoryTelemetryTimer = new DispatcherTimer { Interval = GameMemoryTelemetryInterval };
        _gameMemoryTelemetryTimer.Tick += OnGameMemoryTelemetryTick;
        _gameMemoryTelemetryTimer.Start();
        DebugLog.Info("MainWindow.MemorySchedule",
            "[MemorySchedule] Game launched, starting 5-minute resource telemetry");
    }

    private void OnGameMemoryTelemetryTick(object? sender, object e)
    {
        if (!_isMemoryReleaseEnabled || !_isGameMemoryStrategyActive)
            return;

        var cpu = _systemResourceMonitor.SampleCpuUsagePercent();
        var memory = _systemResourceMonitor.GetMemoryUsagePercent();
        if (cpu.HasValue)
            _gameMemorySamples.Add((cpu.Value, memory));

        if (_gameMemoryTelemetryStopwatch.Elapsed < GameMemoryTelemetryDuration)
            return;

        _gameMemoryTelemetryTimer?.Stop();
        _gameMemoryTelemetryTimer = null;
        StartGameMemoryReleaseTimer();
    }

    private void StartGameMemoryReleaseTimer()
    {
        if (!_isMemoryReleaseEnabled || !_isGameMemoryStrategyActive)
            return;

        var (averageCpu, averageMemory) = GetAverageGameMemorySamples();
        var minutes = Math.Clamp(
            NormalGameMemoryReleaseMinutes + averageCpu / 10.0 - averageMemory / 10.0,
            MinGameMemoryReleaseMinutes,
            MaxGameMemoryReleaseMinutes);

        _gameMemoryTelemetryStopwatch.Stop();
        _gameMemoryReleaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(minutes) };
        _gameMemoryReleaseTimer.Tick += OnGameMemoryReleaseTick;
        _gameMemoryReleaseTimer.Start();
        DebugLog.Info("MainWindow.MemorySchedule",
            $"[MemorySchedule] Game telemetry complete, avg CPU {averageCpu:F1}%, avg memory {averageMemory:F1}%, " +
            $"release interval {minutes:F1} min");
    }

    private (double Cpu, double Memory) GetAverageGameMemorySamples()
    {
        if (_gameMemorySamples.Count == 0)
            return (0, 0);

        var cpu = 0.0;
        var memory = 0.0;
        foreach (var sample in _gameMemorySamples)
        {
            cpu += sample.Cpu;
            memory += sample.Memory;
        }

        return (cpu / _gameMemorySamples.Count, memory / _gameMemorySamples.Count);
    }

    private void OnGameMemoryReleaseTick(object? sender, object e)
    {
        if (!_isMemoryReleaseEnabled || !_isGameMemoryStrategyActive)
            return;

        DebugLog.Info("MainWindow.MemorySchedule", "[MemorySchedule] Releasing memory during game");
        FlushMemory(force: true);
    }

    private void StopGameMemoryStrategy(bool restoreNormal = true)
    {
        _isGameMemoryStrategyActive = false;
        _gameMemoryTelemetryTimer?.Stop();
        _gameMemoryTelemetryTimer = null;
        _gameMemoryReleaseTimer?.Stop();
        _gameMemoryReleaseTimer = null;
        _gameMemoryTelemetryStopwatch.Stop();
        _gameMemorySamples.Clear();
        _systemResourceMonitor.ResetCpuBaseline();

        if (restoreNormal && !_isExit && _isMemoryReleaseEnabled &&
            _periodicMemoryTimer != null && !_periodicMemoryTimer.IsEnabled)
            _periodicMemoryTimer.Start();

        DebugLog.Info("MainWindow.MemorySchedule", restoreNormal
            ? "[MemorySchedule] Game ended, restoring normal memory release"
            : "[MemorySchedule] Game memory scheduling stopped");
    }
}