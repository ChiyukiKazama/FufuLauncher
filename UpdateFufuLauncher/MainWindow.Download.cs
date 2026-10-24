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

    private void StartMultiThreadDownload(string url, string downloadText = "正在下载更新文件...")
    {
        bool useR2Tls = _isR2Download;

        LoadingPanel.Visibility = Visibility.Collapsed;
        SelectionPanel.Visibility = Visibility.Collapsed;
        ActionPanel.Visibility = Visibility.Collapsed;
        DownloadProgressPanel.Visibility = Visibility.Visible;
        SubtitleText.Text = "正在获取更新...";
        DownloadMainText.Text = downloadText;

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
                    DownloadDetailText.Text = $"{downloadedMB:F2} MB / {totalMB:F2} MB ({progress:F1}%) • {speedMB:F2} MB/s";
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

                bool wasR2Download = _isR2Download;
                _isR2Download = false;

                if (e.Cancelled)
                {
                    if (wasR2Download)
                    {
                        _ = FallbackToGitHubPathsAsync("已取消 Cloudflare R2 下载，可直连 GitHub 下载");
                    }
                    else if (_isNodeDownload)
                    {
                        _isNodeDownload = false;
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
                        MessageBox.Show($"Cloudflare R2 下载失败，可改用 GitHub 线路\n\n错误详情: {e.Error.Message}", "下载失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _ = FallbackToGitHubPathsAsync("Cloudflare R2 下载失败，可直连 GitHub 下载");
                    }
                    else if (_isNodeDownload)
                    {
                        _isNodeDownload = false;
                        MessageBox.Show($"站点线路下载失败，已经切回GitHub线路\n\n请在列表里重新选择一个节点下载\n错误详情: {e.Error.Message}", "下载失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _ = FallbackToMirrorSelectionAsync();
                    }
                    else
                    {
                        MessageBox.Show($"错误: {e.Error.Message}\n\n请选择其他节点继续下载\n或者去这里下载：https://wwaoi.lanzn.com/b00wnb99ef\n密码:6hnh", "失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        ResetToSelectionUI();
                    }
                }
                else
                {
                    DownloadMainText.Text = "正在校验文件完整性";

                    if (!VerifyFileSha256(savePath))
                    {
                        try { File.Delete(savePath); } catch { }

                        MessageBox.Show(
                            wasR2Download
                                ? "下载的文件SHA-256校验失败，文件可能在传输过程中被损坏或篡改\n\n已自动删除该文件，可改用 GitHub 线路"
                                : "下载的文件SHA-256校验失败，文件可能在传输过程中被损坏或篡改\n\n已自动删除该文件，请更换其他节点重新下载",
                            "文件完整性校验失败",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        if (wasR2Download)
                        {
                            _ = FallbackToGitHubPathsAsync("Cloudflare R2 文件校验失败，可直连 GitHub 下载");
                        }
                        else
                        {
                            ResetToSelectionUI();
                        }
                        return;
                    }

                    if (!_isPreviewMode)
                    {
                        DownloadMainText.Text = "正在校验数字签名...";

                        uint trustResult = VerifyAuthenticodeSignature(savePath);
                        if (trustResult != 0)
                        {
                            try { File.Delete(savePath); } catch { }

                            MessageBox.Show(
                                wasR2Download
                                    ? "下载的文件未通过数字签名校验，文件可能已被损坏或篡改。\n\n出于安全考虑，已终止安装并自动删除该文件，可改用 GitHub 线路。"
                                    : "下载的文件未通过数字签名校验，文件可能已被损坏或篡改。\n\n出于安全考虑，已终止安装并自动删除该文件，请更换其他节点重新下载。",
                                "数字签名校验失败",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);

                            if (wasR2Download)
                            {
                                _ = FallbackToGitHubPathsAsync("Cloudflare R2 文件签名校验失败，可直连 GitHub 下载");
                            }
                            else
                            {
                                ResetToSelectionUI();
                            }
                            return;
                        }
                    }

                    DownloadMainText.Text = "正在启动安装...";

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
            DownloadMainText.Text = "正在取消下载...";
            _downloader.CancelAsync();
        }
    }

    private void ResetToSelectionUI()
    {
        DownloadProgressPanel.Visibility = Visibility.Collapsed;
        SelectionPanel.Visibility = Visibility.Visible;
        ActionPanel.Visibility = Visibility.Visible;
        SubtitleText.Text = "下载已暂停";

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
                var result = MessageBox.Show("下载进度长期未动或网络极慢，建议取消当前下载并更换其他节点尝试\n\n是否立即取消下载？", "下载缓慢", MessageBoxButton.YesNo, MessageBoxImage.Information);
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
        StartMultiThreadDownload(_targetExeUrl!, "正在从 GitHub 下载更新文件...");
    }

    private void MirrorDownload_Click(object sender, RoutedEventArgs e)
    {
        if (MirrorListView.SelectedItem is MirrorInfo selectedMirror)
        {
            StartMultiThreadDownload(selectedMirror.FullUrl, $"正在从 {selectedMirror.Domain} 下载更新文件...");
        }
        else
        {
            MessageBox.Show("请先在列表中选择一个可用节点", "提示");
        }
    }

    #endregion
}
