/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace Updater;

public partial class MainWindow
{
    #region Update Check

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string localJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Update.json");
            SubtitleText.Text = "请求API更新配置...";
            string apiUrl = "https://philia093.cyou/Update.json";
            string fallbackUrl = "https://fu1.fun/Update.json";
            string updateContent;
            try
            {
                updateContent = await _httpClient.GetStringAsync(apiUrl);
            }
            catch (HttpRequestException)
            {
                try
                {
                    updateContent = await _httpClient.GetStringAsync(fallbackUrl);
                }
                catch (HttpRequestException)
                {
                    if (!File.Exists(localJsonPath))
                    {
                        throw;
                    }

                    SubtitleText.Text = "API请求失败，读取本地更新配置...";
                    updateContent = await File.ReadAllTextAsync(localJsonPath);
                }
            }

            JObject updateJson = JObject.Parse(updateContent);
            _officialVersion = updateJson.GetValue("version", StringComparison.OrdinalIgnoreCase)?.ToString() ?? string.Empty;

            if (_isRollbackMode)
            {
                await CheckRollbackAsync();
                return;
            }

            if (_isPreviewMode)
            {
                await CheckPreviewUpdateAsync(updateJson);
                return;
            }

            if (!TryParseVersion(_installedVersion, out Version currentVersion) ||
                !TryParseVersion(_officialVersion, out Version remoteVersion))
            {
                MessageBox.Show("版本号无法识别，请前往官网下载\n此处不提供更新", "版本异常", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
                return;
            }

            if (currentVersion >= remoteVersion)
            {
                if (!_isTestForceUpdate)
                {
                    ShowNoUpdate("当前已是最新版本，无需更新", _isInstalledPreviewBuild);
                    return;
                }

                SubtitleText.Text = "测试模式：已是最新版本，仍继续下载...";
            }

            PrepareOfficialReleaseDownload();
            await PrepareDownloadAsync("请选择下载线路", "直连GitHub下载...");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"更新初始化失败，请检查网络，或者去下面下载\nhttps://wwaoi.lanzn.com/b00wnb99ef\n密码:6hnh\n错误详情: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
        }
    }

    private void ShowNoUpdate(string message, bool showRollback = false)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        NoUpdatePanel.Visibility = Visibility.Visible;
        NoUpdateText.Text = message;
        RollbackBtn.Visibility = showRollback ? Visibility.Visible : Visibility.Collapsed;
        SubtitleText.Text = "检查完毕";
    }

    private void PrepareOfficialReleaseDownload()
    {
        string version = _officialVersion.Trim();
        _fileName = $"FufuLauncher_Setup_v{version}.exe";
        _targetExeUrl = $"https://github.com/FufuLauncher/FufuLauncher/releases/latest/download/{_fileName}";
        _r2ExeUrl = $"{R2BaseUrl}/FufuLauncher_Setup_v{version}.exe";
    }

    private async Task PrepareDownloadAsync(string selectionSubtitle, string directSubtitle)
    {
        if (await TryStartR2DownloadAsync())
        {
            return;
        }

        await PrepareGitHubDownloadAsync(selectionSubtitle, directSubtitle);
    }

    private async Task PrepareGitHubDownloadAsync(string selectionSubtitle, string directSubtitle)
    {
        if (_useThirdPartyCDN)
        {
            if (await TryStartNodeDownloadAsync())
            {
                return;
            }

            await ShowMirrorSelectionAsync(selectionSubtitle);
        }
        else
        {
            SubtitleText.Text = directSubtitle;
            StartMultiThreadDownload(_targetExeUrl!, "正在从 GitHub 下载更新文件...");
        }
    }

    private async Task<bool> TryStartR2DownloadAsync()
    {
        string? r2Url = _r2ExeUrl;
        if (_isPreviewMode || string.IsNullOrWhiteSpace(r2Url))
        {
            return false;
        }

        SubtitleText.Text = "正在检测Cloudflare R2...";
        string? resolvedR2Url = await ResolveR2InstallerUrlAsync(r2Url);
        if (resolvedR2Url == null)
        {
            return false;
        }

        _isR2Download = true;
        SubtitleText.Text = "已发现Cloudflare R2";
        StartMultiThreadDownload(resolvedR2Url, "正在从Cloudflare R2下载更新文件...");
        return true;
    }

    private async Task<string?> ResolveR2InstallerUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsedUri) ||
            parsedUri is null ||
            !string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Uri currentUri = parsedUri;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

            for (int redirectCount = 0; redirectCount <= 3; redirectCount++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                request.Headers.Range = new RangeHeaderValue(0, 1);

                using var response = await _r2HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if ((int)response.StatusCode is >= 300 and <= 399)
                {
                    Uri? redirectUri = response.Headers.Location;
                    if (redirectUri == null)
                    {
                        return null;
                    }

                    if (!redirectUri.IsAbsoluteUri)
                    {
                        redirectUri = new Uri(currentUri, redirectUri);
                    }

                    if (!string.Equals(redirectUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    currentUri = redirectUri;
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                {
                    return null;
                }

                string? mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType != null &&
                    (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) ||
                     mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                byte[] signature = new byte[2];
                int read = await stream.ReadAtLeastAsync(signature, signature.Length, throwOnEndOfStream: false, cancellationToken: cts.Token);
                return read == 2 && signature[0] == (byte)'M' && signature[1] == (byte)'Z'
                    ? currentUri.AbsoluteUri
                    : null;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task FallbackToGitHubPathsAsync(string directFallbackSubtitle)
    {
        if (_useThirdPartyCDN)
        {
            await FallbackToMirrorSelectionAsync();
            return;
        }

        ResetToSelectionUI();
        SubtitleText.Text = directFallbackSubtitle;
    }

    private async Task<bool> TryStartNodeDownloadAsync()
    {
        if (_isPreviewMode)
        {
            return false;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var statusResponse = await _httpClient.GetStringAsync(NodeStatusUrl, cts.Token);
            var status = JObject.Parse(statusResponse);

            if (status["download_available"]?.Value<bool>() != true)
            {
                return false;
            }

            if ((status["quota"]?["remaining"]?.Value<long>() ?? 0) <= 0)
            {
                return false;
            }

            string nodeVersion = status["current_version"]?.ToString()?.Trim() ?? string.Empty;
            if (!string.Equals(nodeVersion, _officialVersion.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (Random.Shared.Next(100) != 0)
            {
                return false;
            }

            string downloadPath = status["download_url"]?.ToString()?.Trim() ?? string.Empty;
            if (downloadPath.Length == 0)
            {
                return false;
            }

            string nodeDownloadUrl = downloadPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? downloadPath
                : $"{NodeBaseUrl}/{(downloadPath.StartsWith('/') ? downloadPath.Substring(1) : downloadPath)}";

            _isNodeDownload = true;
            StartMultiThreadDownload(nodeDownloadUrl, "正在从站点线路下载更新文件...");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ShowMirrorSelectionAsync(string selectionSubtitle)
    {
        DownloadProgressPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        SubtitleText.Text = "节点联通性校验...";
        await TestMirrorsAsync();

        LoadingPanel.Visibility = Visibility.Collapsed;
        SelectionPanel.Visibility = Visibility.Visible;
        ActionPanel.Visibility = Visibility.Visible;
        MirrorListView.ItemsSource = _mirrors;

        if (_mirrors.Count > 0)
        {
            MirrorListView.SelectedIndex = 0;
            SubtitleText.Text = selectionSubtitle;
        }
        else
        {
            SubtitleText.Text = "所有节点均未通过校验";
            MessageBox.Show("所有镜像节点均未通过文件完整性校验或网络超时\n\n请更换网络环境，或尝试使用直连官方源下载", "网络不佳", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task FallbackToMirrorSelectionAsync()
    {
        try
        {
            await ShowMirrorSelectionAsync("请选择下载线路");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"下载线路获取失败，请关闭更新器后重试\n错误详情: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task CheckRollbackAsync()
    {
        SubtitleText.Text = "准备回退正式版...";

        string versionLabel = string.IsNullOrEmpty(_officialVersion) ? string.Empty : $" v{_officialVersion}";
        var confirm = MessageBox.Show(
            $"确定要回退到正式版{versionLabel}吗？\n\n回退将下载并安装最新正式版，覆盖当前预览版。",
            "回退正式版", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            Environment.Exit(0);
            return;
        }

        PrepareOfficialReleaseDownload();
        await PrepareDownloadAsync("请选择下载线路（回退正式版）", "直连GitHub下载正式版...");
    }

    private async void RollbackToStable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await CheckRollbackAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"回退正式版失败，请检查网络\n错误详情: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
        }
    }

    private async Task CheckPreviewUpdateAsync(JObject updateJson)
    {
        string previewVersionStr = updateJson.GetValue("PreReleaseVersion", StringComparison.OrdinalIgnoreCase)?.ToString()?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(previewVersionStr))
        {
            ShowNoUpdate("服务端暂未发布预览版更新", _isInstalledPreviewBuild);
            return;
        }

        if (!TryParseVersion(previewVersionStr, out Version previewVersion))
        {
            MessageBox.Show("预览版版本号无法识别，请前往官网下载\n此处不提供更新", "版本异常", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(0);
            return;
        }

        string officialVersionStr = updateJson.GetValue("version", StringComparison.OrdinalIgnoreCase)?.ToString();
        if (!TryParseVersion(officialVersionStr, out Version officialVersion))
        {
            MessageBox.Show("正式版版本号无法识别，请前往官网下载\n此处不提供更新", "版本异常", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(0);
            return;
        }

        if (previewVersion <= officialVersion)
        {
            ShowNoUpdate("预览版版本号未高于最新正式版，不允许更新预览版", _isInstalledPreviewBuild);
            return;
        }

        SubtitleText.Text = "获取GitHub标签列表...";
        string tagsUrl = "https://api.github.com/repos/FufuLauncher/FufuLauncher/tags?per_page=100";
        var tagsResponse = await _httpClient.GetStringAsync(tagsUrl);
        JArray tags = JArray.Parse(tagsResponse);


        string previewTagName = $"{StripPreReleaseSuffix(previewVersionStr)}Pre-release";
        JToken matchedTag = tags.FirstOrDefault(t => string.Equals(t["name"]?.ToString(), previewTagName, StringComparison.OrdinalIgnoreCase));

        if (matchedTag == null)
        {
            MessageBox.Show($"未在GitHub找到预览版发布包（标签: {previewTagName}）\n\n更新流程已终止，请稍后再试", "未找到预览版", MessageBoxButton.OK, MessageBoxImage.Information);
            Environment.Exit(0);
            return;
        }

        SubtitleText.Text = "获取预览版Release信息...";
        string tagName = matchedTag["name"]!.ToString();
        string releaseUrl = $"https://api.github.com/repos/FufuLauncher/FufuLauncher/releases/tags/{Uri.EscapeDataString(tagName)}";
        var releaseResponse = await _httpClient.GetStringAsync(releaseUrl);
        JObject releaseJson = JObject.Parse(releaseResponse);

        JToken targetAsset = releaseJson["assets"]?.FirstOrDefault(a => a["name"]?.ToString().EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);

        if (targetAsset == null)
        {
            throw new Exception("预览版Release中不存在文件");
        }

        _targetExeUrl = targetAsset["browser_download_url"]!.ToString();
        _fileName = targetAsset["name"]!.ToString();
        _r2ExeUrl = null;


        string digest = targetAsset["digest"]?.ToString();
        if (!string.IsNullOrEmpty(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            _expectedSha256 = digest.Substring("sha256:".Length);
        }

        await PrepareDownloadAsync("请选择下载线路（预览版）", "直连GitHub下载预览版...");
    }

    #endregion

    #region Version Helpers

    private static string StripPreReleaseSuffix(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return string.Empty;

        var trimmed = version.Trim();
        if (trimmed.EndsWith("Pre-release", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - "Pre-release".Length).Trim();
        }
        return trimmed;
    }

    private static bool TryParseVersion(string input, out Version version)
    {
        return Version.TryParse(StripPreReleaseSuffix(input), out version);
    }

    #endregion
}
