/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Services.GameServer;

namespace FufuLauncher.Views
{
    public sealed partial class AdvancedServerSwitchPage : Page
    {
        private string _gameDir = string.Empty;
        private Window? _parentWindow;
        private string _targetServer = string.Empty;

        private CancellationTokenSource? _cts;
        private GameServerScheme? _currentScheme;

        private readonly GameServerDownloadMonitor _downloadMonitor = new();
        private readonly RemainingChunksTracker _remainingChunksTracker = new();
        private DownloadSpeedChartController? _chartController;

        public AdvancedServerSwitchPage()
        {
            InitializeComponent();
            _chartController = new DownloadSpeedChartController(SpeedGraphChart, _downloadMonitor);
            SpeedGraphChart.NoDataText = "SpeedGraph_NoData".GetLocalized();
            RemainingChunksList.ItemsSource = _remainingChunksTracker.Chunks;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is GameSettingsPage.SwitchPageParams param)
            {
                _gameDir = param.GameDir;
                _parentWindow = param.ParentWindow;
                _targetServer = param.TargetServer ?? string.Empty;
                _parentWindow.Closed += ParentWindow_Closed;
            }

            InitializeServerSelector();
        }

        private void InitializeServerSelector()
        {
            GameServerConfigurationService configurationService = App.GetService<GameServerConfigurationService>();
            _currentScheme = string.IsNullOrEmpty(_gameDir)
                ? null
                : configurationService.TryDetectCurrentScheme(_gameDir);

            CurrentServerText.Text = _currentScheme is null
                ? "AdvancedServerSwitch_UnknownCurrent".GetLocalized()
                : string.Format("AdvancedServerSwitch_CurrentServer".GetLocalized(), _currentScheme.DisplayName);

            TargetServerCombo.ItemsSource = GameServerScheme.Selectable;

            TargetServerCombo.SelectedItem = ResolvePreferredTarget();
        }

        private GameServerScheme? ResolvePreferredTarget()
        {
            if (!string.IsNullOrEmpty(_targetServer))
            {
                GameServerScheme preset = GameServerScheme.FromPreset(_targetServer);
                if (preset.IsNotCompatOnly)
                {
                    return preset;
                }
            }

            return GameServerScheme.Selectable.FirstOrDefault(scheme =>
                       _currentScheme is null || !scheme.Equals(_currentScheme)) ??
                   GameServerScheme.Selectable.FirstOrDefault();
        }

        private async void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            if (TargetServerCombo.SelectedItem is not GameServerScheme target || _currentScheme is null ||
                string.IsNullOrEmpty(_gameDir))
            {
                await ShowMessageAsync("AdvancedServerSwitch_SelectTarget".GetLocalized());
                return;
            }

            if (_cts is not null)
            {
                return;
            }

            StartBtn.IsEnabled = false;
            TargetServerCombo.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressBar.IsIndeterminate = true;
            ProgressDetailText.Text = string.Empty;
            CancelBtn.IsEnabled = true;
            _chartController!.Start();
            _remainingChunksTracker.Reset();
            RemainingCountText.Text = string.Format("AdvancedServerSwitch_RemainingChunks".GetLocalized(), 0);

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            var converter = App.GetService<GameServerConverter>();
            var progress =
                new Progress<GameServerConversionProgress>(p => DispatcherQueue.TryEnqueue(() => UpdateProgress(p)));

            try
            {
                await Task.Run(() => converter.ConvertAsync(_gameDir, _currentScheme, target, progress, AppendStatus,
                    token, _downloadMonitor));

                _chartController.Stop();
                ProgressPanel.Visibility = Visibility.Collapsed;

                var successDialog = new ContentDialog
                {
                    Title = "AdvancedServerSwitch_Done".GetLocalized(),
                    Content = string.Format("AdvancedServerSwitch_DoneMsg".GetLocalized(), target.DisplayName),
                    CloseButtonText = "OkBtn".GetLocalized(),
                    XamlRoot = XamlRoot
                };

                await successDialog.ShowAsync();

                _parentWindow?.Close();
            }
            catch (OperationCanceledException)
            {
                _chartController.SetPaused();
                ProgressPanel.Visibility = Visibility.Collapsed;
                AppendStatus("AdvancedServerSwitch_Cancelled".GetLocalized());
            }
            catch (Exception ex)
            {
                _chartController.SetFailed();
                ProgressPanel.Visibility = Visibility.Collapsed;
                await ShowMessageAsync(
                    string.Format("AdvancedServerSwitch_FailedMsg".GetLocalized(), ex.Message),
                    "AdvancedServerSwitch_FailedTitle".GetLocalized());
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                StartBtn.IsEnabled = true;
                TargetServerCombo.IsEnabled = true;
                CancelBtn.IsEnabled = false;
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            CancelBtn.IsEnabled = false;
            _cts?.Cancel();
        }

        private void ParentWindow_Closed(object sender, WindowEventArgs args)
        {
            _cts?.Cancel();
            _chartController?.Stop();
            if (_parentWindow is not null)
            {
                _parentWindow.Closed -= ParentWindow_Closed;
            }
        }

        private void UpdateProgress(GameServerConversionProgress p)
        {
            _chartController?.UpdateProgress(p);
            _remainingChunksTracker.Update(p);
            RemainingCountText.Text = string.Format("AdvancedServerSwitch_RemainingChunks".GetLocalized(),
                _remainingChunksTracker.Chunks.Count);

            StatusText.Text = p.ChunkName is null
                ? p.Stage
                : string.Format("AdvancedServerSwitch_Progress".GetLocalized(), p.DoneChunks, p.TotalChunks,
                    p.ChunkName);

            if (p.TotalChunks > 0)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Maximum = p.TotalChunks;
                ProgressBar.Value = p.DoneChunks;
                ProgressDetailText.Text = p.TotalBytes > 0
                    ? $"{p.DoneBytes / 1048576.0:F1} MB / {p.TotalBytes / 1048576.0:F1} MB ({p.Percent:F1}%)"
                    : $"{p.Percent:F1}%";
            }
            else
            {
                ProgressBar.IsIndeterminate = true;
                ProgressDetailText.Text = string.Empty;
            }
        }

        private void AppendStatus(string message)
        {
            DispatcherQueue.TryEnqueue(() => StatusText.Text = message);
        }

        private async Task ShowMessageAsync(string message, string? title = null)
        {
            var dialog = new ContentDialog
            {
                Title = title ?? "ErrorTitle".GetLocalized(),
                Content = message,
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}