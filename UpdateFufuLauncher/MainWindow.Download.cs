/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Downloader;

namespace Updater;

public partial class MainWindow
{
    #region Download

    private void StartMultiThreadDownload(string url, string? downloadLabelKey = null, params object?[] args)
    {
        bool useR2Tls = _isR2Download;

        LoadingPanel.Visibility = Visibility.Collapsed;
        SelectionPanel.Visibility = Visibility.Collapsed;
        ActionPanel.Visibility = Visibility.Collapsed;
        DownloadProgressPanel.Visibility = Visibility.Visible;
        SetLocalizedText(SubtitleText, "FetchingUpdate");
        SetLocalizedText(DownloadMainText, downloadLabelKey ?? "DownloadStarting", args);

        if (CancelBtn != null) CancelBtn.IsEnabled = true;

        string savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _fileName);

        var downloadOpt = new DownloadConfiguration()
        {
            ChunkCount = 4, 
            ParallelDownload = true,
            MaxTryAgainOnFailure = 9999,
            ClearPackageOnCompletionWithFailure = false,
            CustomHttpClientFactory = () => useR2Tls
                ? CreateR2HttpClient(TimeSpan.FromSeconds(30))
                : new HttpClient()
                {
                    Timeout = TimeSpan.FromSeconds(30)
                }
        };

        _downloader = new DownloadService(downloadOpt);

        _currentReceivedBytes = 0;
        _lastReceivedBytes = 0;
        _stuckTicks = 0;
        _isDownloading = true;

        _uiUpdateTimer.Restart();
        _stuckTimer.Start();

        _downloader.DownloadProgressChanged += (s, e) =>
        {
            _currentReceivedBytes = e.ReceivedBytesSize;

            if (_uiUpdateTimer.ElapsedMilliseconds >= 100 || e.ProgressPercentage >= 100)
            {
                _uiUpdateTimer.Restart();

                double downloadedMB = e.ReceivedBytesSize / 1024.0 / 1024.0;
                double totalMB = e.TotalBytesToReceive / 1024.0 / 1024.0;
                double speedMB = e.BytesPerSecondSpeed / 1024.0 / 1024.0;
                double progress = e.ProgressPercentage;

                Dispatcher.InvokeAsync(() =>
                {
                    DownloadProgressBar.Value = progress;
                    SetLocalizedText(DownloadDetailText, "DownloadProgressDetail", downloadedMB, totalMB, progress, speedMB);
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        };

        _downloader.DownloadFileCompleted += (s, e) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                _isDownloading = false;
                _stuckTimer.Stop();
                _uiUpdateTimer.Stop();

                bool wasNodeDownload = _isNodeDownload;
                bool wasR2Download = _isR2Download;
                _isNodeDownload = false;
                _isR2Download = false;

                if (e.Cancelled)
                {
                    if (wasR2Download)
                    {
                        _ = FallbackToGitHubPathsAsync("DownloadCancelledR2");
                    }
                    else if (wasNodeDownload)
                    {
                        _ = FallbackToMirrorSelectionAsync();
                    }
                    else
                    {
                        ResetToSelectionUI();
                    }
                }
                else if (e.Error != null)
                {
                    if (wasR2Download)
                    {
                        MessageBox.Show(Strings.Format("R2DownloadFailed", e.Error.Message), Strings.Get("DownloadFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        _ = FallbackToGitHubPathsAsync("R2DownloadFailedFallback");
                    }
                    else if (wasNodeDownload)
                    {
                        MessageBox.Show(Strings.Format("NodeDownloadFailed", e.Error.Message), Strings.Get("DownloadFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        _ = FallbackFromNodeAsync();
                    }
                    else
                    {
                        MessageBox.Show(Strings.Format("DownloadError", e.Error.Message, ApiEndpoints.DownloadPageUrl, ApiEndpoints.DownloadPagePassword), Strings.Get("DownloadFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        ResetToSelectionUI();
                    }
                }
                else
                {
                    SetLocalizedText(DownloadMainText, "VerifyingIntegrity");

                    if (!VerifyFileSha256(savePath))
                    {
                        try { File.Delete(savePath); } catch { }

                        string hashFailureMessage = wasR2Download
                            ? Strings.Get("HashFailureR2")
                            : wasNodeDownload
                                ? Strings.Get("HashFailureNode")
                                : Strings.Get("HashFailureOther");

                        MessageBox.Show(
                            hashFailureMessage,
                            Strings.Get("HashFailureTitle"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        if (wasR2Download)
                        {
                            _ = FallbackToGitHubPathsAsync("R2HashFallback");
                        }
                        else if (wasNodeDownload)
                        {
                            _ = FallbackFromNodeAsync();
                        }
                        else
                        {
                            ResetToSelectionUI();
                        }
                        return;
                    }

                    if (!_isPreviewMode)
                    {
                        SetLocalizedText(DownloadMainText, "VerifyingSignature");

                        uint trustResult = VerifyAuthenticodeSignature(savePath);
                        if (trustResult != 0)
                        {
                            try { File.Delete(savePath); } catch { }

                            string signatureFailureMessage = wasR2Download
                                ? Strings.Get("SignatureFailureR2")
                                : wasNodeDownload
                                    ? Strings.Get("SignatureFailureNode")
                                    : Strings.Get("SignatureFailureOther");

                            MessageBox.Show(
                                signatureFailureMessage,
                                Strings.Get("SignatureFailureTitle"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);

                            if (wasR2Download)
                            {
                                _ = FallbackToGitHubPathsAsync("R2SignatureFallback");
                            }
                            else if (wasNodeDownload)
                            {
                                _ = FallbackFromNodeAsync();
                            }
                            else
                            {
                                ResetToSelectionUI();
                            }
                            return;
                        }
                    }

                    SetLocalizedText(DownloadMainText, "StartingInstaller");

                    ProcessStartInfo psi = new()
                    {
                        FileName = savePath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    try
                    {
                        Process.Start(psi);
                    }
                    catch { }

                    Environment.Exit(0);
                }
            });
        };

        _downloader.DownloadFileTaskAsync(url, savePath);
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        if (CancelBtn != null) CancelBtn.IsEnabled = false;

        if (_downloader != null && !_downloader.IsCancelled)
        {
            SetLocalizedText(DownloadMainText, "CancellingDownload");
            _downloader.CancelAsync();
        }
    }

    private void ResetToSelectionUI()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        DownloadProgressPanel.Visibility = Visibility.Collapsed;
        SelectionPanel.Visibility = Visibility.Visible;
        ActionPanel.Visibility = Visibility.Visible;
        SetLocalizedText(SubtitleText, "DownloadPaused");

        if (CancelBtn != null) CancelBtn.IsEnabled = true;
    }

    private void StuckTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isDownloading || _downloader == null || _downloader.IsCancelled) return;

        long diff = _currentReceivedBytes - _lastReceivedBytes;
        if (diff < 20480) 
        {
            _stuckTicks++;
            if (_stuckTicks >= 10) 
            {
                _stuckTimer.Stop();
                var result = MessageBox.Show(Strings.Get("StuckDownloadMessage"), Strings.Get("StuckDownloadTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                {
                    CancelDownload_Click(null, null);
                }
                else
                {
                    _stuckTicks = 0;
                    _stuckTimer.Start();
                }
            }
        }
        else
        {
            _stuckTicks = 0;
        }
        _lastReceivedBytes = _currentReceivedBytes;
    }

    private void DirectDownload_Click(object sender, RoutedEventArgs e)
    {
        StartMultiThreadDownload(_targetExeUrl!, "DownloadingFromGitHub");
    }

    private void MirrorDownload_Click(object sender, RoutedEventArgs e)
    {
        if (MirrorListView.SelectedItem is MirrorInfo selectedMirror)
        {
            StartMultiThreadDownload(selectedMirror.FullUrl, "DownloadingFromMirror", selectedMirror.Domain);
        }
        else
        {
            MessageBox.Show(Strings.Get("SelectNodeFirstMessage"), Strings.Get("SelectNodeFirstTitle"));
        }
    }

    #endregion
}
