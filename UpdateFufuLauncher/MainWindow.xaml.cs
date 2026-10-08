/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Downloader;
using Newtonsoft.Json.Linq;

namespace Updater
{
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWA_MICA_EFFECT = 1029;
        
        private const int DWMSBT_MAINWINDOW = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }
        
        private const string AppVersion = "1.7.1.1";

        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        }) 
        { 
            Timeout = TimeSpan.FromSeconds(15) 
        };
        
        private string _targetExeUrl;
        private string _fileName;
        private string _expectedSha256;
        private List<MirrorInfo> _mirrors = new();
        private DownloadService _downloader;
        private Stopwatch _uiUpdateTimer = new();

        private DispatcherTimer _stuckTimer;
        private long _currentReceivedBytes = 0;
        private long _lastReceivedBytes = 0;
        private int _stuckTicks = 0;
        private bool _isDownloading = false;
        private bool _isNodeDownload = false;
        private bool _useThirdPartyCDN = true;
        private bool _isPreviewMode = false;
        private bool _isRollbackMode = false;
        private string _installedVersion = AppVersion;
        private bool _isInstalledPreviewBuild = false;
        private string _officialVersion = string.Empty;

        private const string TestFileOfficialUrl = "https://raw.githubusercontent.com/moodlehq/moodle-exttests/master/test.html";
        private const string ExpectedTestFileMD5 = "47250a973d1b88d9445f94db4ef2c97a";
        private const string NodeStatusUrl = "https://fufu.668851.xyz/api/status";
        private const string NodeBaseUrl = "https://fufu.668851.xyz";

        public MainWindow()
        {
            InitializeComponent();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");

            _stuckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _stuckTimer.Tick += StuckTimer_Tick;
            
            var args = Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (arg.StartsWith("--use-third-party-cdn=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = arg.Substring("--use-third-party-cdn=".Length);
                    _useThirdPartyCDN = !value.Equals("false", StringComparison.OrdinalIgnoreCase);
                }
                else if (arg.Equals("--preview", StringComparison.OrdinalIgnoreCase))
                {
                    _isPreviewMode = true;
                }
                else if (arg.Equals("--rollback", StringComparison.OrdinalIgnoreCase))
                {
                    _isRollbackMode = true;
                }
                else if (arg.StartsWith("--installed-version=", StringComparison.OrdinalIgnoreCase))
                {
                    _installedVersion = arg.Substring("--installed-version=".Length);
                }
            }

            _isInstalledPreviewBuild = _installedVersion.IndexOf("Pre-release", StringComparison.OrdinalIgnoreCase) >= 0;
        }

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

        private void StuckTimer_Tick(object sender, EventArgs e)
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

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            IntPtr windowHandle = new WindowInteropHelper(this).EnsureHandle();
            
            int useImmersiveDarkMode = 1;
            DwmSetWindowAttribute(windowHandle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, Marshal.SizeOf(typeof(int)));

            var osVersion = Environment.OSVersion.Version;
            if (osVersion.Major == 10 && osVersion.Minor == 0 && osVersion.Build >= 22000)
            {
                if (osVersion.Build >= 22621)
                {
                    int backdropType = DWMSBT_MAINWINDOW;
                    DwmSetWindowAttribute(windowHandle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, Marshal.SizeOf(typeof(int)));
                }
                else
                {
                    int micaValue = 1;
                    DwmSetWindowAttribute(windowHandle, DWMWA_MICA_EFFECT, ref micaValue, Marshal.SizeOf(typeof(int)));
                }
            }
            else
            {
                AccentPolicy accent = new AccentPolicy
                {
                    AccentState = 4, 
                    GradientColor = 0x7F202020 
                };

                int accentSize = Marshal.SizeOf(accent);
                IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);
                try
                {
                    Marshal.StructureToPtr(accent, accentPtr, false);

                    WindowCompositionAttributeData data = new WindowCompositionAttributeData
                    {
                        Attribute = 19, 
                        Data = accentPtr,
                        SizeOfData = accentSize
                    };

                    SetWindowCompositionAttribute(windowHandle, ref data);
                }
                finally
                {
                    Marshal.FreeHGlobal(accentPtr);
                }
            }
        }

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
                    ShowNoUpdate("当前已是最新版本，无需更新", _isInstalledPreviewBuild);
                    return;
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
        }

        private async Task PrepareDownloadAsync(string selectionSubtitle, string directSubtitle)
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
                // Skip mirror testing, directly download from GitHub
                SubtitleText.Text = directSubtitle;
                StartMultiThreadDownload(_targetExeUrl);
            }
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
                StartMultiThreadDownload(nodeDownloadUrl);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task ShowMirrorSelectionAsync(string selectionSubtitle)
        {
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

            // 服务器字段可能已带 Pre-release 后缀，先去掉再拼 tag，避免双重后缀
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

            // 从 GitHub API 的 asset 元数据中提取 SHA-256 哈希（格式: "sha256:xxxxx"）
            string digest = targetAsset["digest"]?.ToString();
            if (!string.IsNullOrEmpty(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                _expectedSha256 = digest.Substring("sha256:".Length);
            }

            await PrepareDownloadAsync("请选择下载线路（预览版）", "直连GitHub下载预览版...");
        }

        private async Task TestMirrorsAsync()
        {
            string[] proxyDomains = {
                "gh.llkk.cc", "github.dpik.top", "gh-proxy.com", "ghfile.geekertao.top",
                "github-proxy.memory-echoes.cn", "cdn.gh-proxy.com", "github.tbedu.top",
                "gh.927223.xyz", "gh.bugdey.us.kg", "git.yylx.win", "cdn.akaere.online",
                "ghproxy.net", "down.mxw.xx.kg", "down.mxw.qzz.io", "github.mxw.qzz.io",
                "gh.dpik.top", "gh.acmsz.top", "gitproxy.127731.xyz", "gh.jjj.gv.uy",
                "github.chenc.dev", "gh.felicity.ac.cn", "gh.inkchills.cn", "gh.jasonzeng.dev",
                "gh.ddlc.top", "gp.zkitefly.eu.org", "gitproxy.mrhjx.cn", "ghfast.top",
                "fastgit.cc", "gh.sixyin.com", "ghp.arslantu.xyz", "github.ednovas.xyz",
                "ghproxy.cxkpro.top", "ghproxy.imciel.com", "gh.idayer.com", "gh-proxy.net",
                "j.1lin.dpdns.org", "github.starrlzy.cn", "ghm.078465.xyz", "ghf.无名氏.top",
                "jiashu.1win.eu.org", "tvv.tw", "j.1win.ggff.net", "gh.catmak.name",
                "gh.b52m.cn", "slink.ltd", "github.tmby.shop", "ghpr.cc", "gh.tryxd.cn",
                "gh.monlor.com", "ghpxy.hwinzniej.top", "git.669966.xyz",
                "github.geekery.cn", "ghproxy.1888866.xyz", "github.xxlab.tech", "free.cn.eu.org",
                "gh.chjina.com", "ghp.keleyaa.com", "proxy.yaoyaoling.net", "ghproxy.monkeyray.net",
                "gh.noki.icu", "g.blfrp.cn"
            };

            int completed = 0;
            var tasks = proxyDomains.Select(async domain =>
            {
                try
                {
                    string testProxyUrl = $"https://{domain}/{TestFileOfficialUrl}";
                    string exeProxyUrl = $"https://{domain}/{_targetExeUrl}";
                    
                    var result = await VerifyAndMeasureNodeAsync(domain, testProxyUrl, exeProxyUrl);
                    
                    int current = Interlocked.Increment(ref completed);
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (TestStatusText != null)
                        {
                            TestStatusText.Text = $"正在测速中...\n{current} / {proxyDomains.Length}";
                        }
                    });
                    
                    return result;
                }
                catch
                {
                    Interlocked.Increment(ref completed);
                    return new MirrorInfo { Domain = domain, IsSuccess = false, ResponseTimeMs = long.MaxValue };
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);
            _mirrors = results.Where(r => r.IsSuccess).OrderBy(r => r.ResponseTimeMs).ToList();
        }

        private async Task<MirrorInfo> VerifyAndMeasureNodeAsync(string domain, string testUrl, string exeUrl)
        {
            var info = new MirrorInfo { Domain = domain, FullUrl = exeUrl, IsSuccess = false, ResponseTimeMs = long.MaxValue };
            var stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();
                using var response = await _httpClient.GetAsync(testUrl, HttpCompletionOption.ResponseContentRead);
                response.EnsureSuccessStatusCode();
                
                byte[] fileData = await response.Content.ReadAsByteArrayAsync();
                stopwatch.Stop();

                string hash = CalculateMD5(fileData);
                if (hash.Equals(ExpectedTestFileMD5, StringComparison.OrdinalIgnoreCase))
                {
                    info.IsSuccess = true;
                    info.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                    double speedKbps = (fileData.Length / 1024.0) / (info.ResponseTimeMs / 1000.0);
                    info.StatusDesc = $"{info.ResponseTimeMs} ms | {(speedKbps > 500 ? "极佳" : "正常")}";
                }
            }
            catch
            {
                stopwatch.Stop();
                info.IsSuccess = false;
                info.ResponseTimeMs = long.MaxValue;
            }
            return info;
        }

        private string CalculateMD5(byte[] data)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(data);
                StringBuilder sb = new();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
        
        private bool VerifyFileSha256(string filePath)
        {
            if (string.IsNullOrEmpty(_expectedSha256))
            {
                return true;
            }

            try
            {
                using var stream = File.OpenRead(filePath);
                byte[] hashBytes = SHA256.HashData(stream);
                StringBuilder sb = new();
                foreach (var b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                string actualHash = sb.ToString();

                return string.Equals(actualHash, _expectedSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static uint VerifyAuthenticodeSignature(string filePath)
        {
            IntPtr fileInfoPtr = IntPtr.Zero;
            try
            {
                var fileInfo = new WinTrustFileInfo
                {
                    cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                    pcwszFilePath = filePath,
                    hFile = IntPtr.Zero,
                    pgKnownSubject = IntPtr.Zero
                };

                fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

                var trustData = new WinTrustData
                {
                    cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = fileInfoPtr,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = IntPtr.Zero,
                    dwProvFlags = 0,
                    dwUIContext = 0,
                    pSignatureSettings = IntPtr.Zero
                };

                var actionId = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                uint result = WinVerifyTrust(IntPtr.Zero, ref actionId, ref trustData);

                trustData.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(IntPtr.Zero, ref actionId, ref trustData);

                return result;
            }
            catch
            {
                return 0x80004005;
            }
            finally
            {
                if (fileInfoPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(fileInfoPtr);
                }
            }
        }

        private bool ConfirmUntrustedInstaller(string filePath)
        {
            var firstWarning = MessageBox.Show(
                "这个安装包看起来好像有问题，没有通过数字签名校验。\n\n" +
                $"文件: {Path.GetFileName(filePath)}\n\n" +
                "可能是下载过程中出了差错，也可能被人动过手脚，不是官方原版。\n\n" +
                "继续安装意味着运行一个被篡改过的程序，可能导致系统被植入恶意代码、账号或数据被窃取。\n\n" +
                "要不要继续安装？",
                "安装包好像有问题", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (firstWarning != MessageBoxResult.Yes)
            {
                return false;
            }

            var secondWarning = MessageBox.Show(
                "再确认一下：这个安装包没能通过数字签名校验，若擅自安装后果可能非常严重！！！\n\n" +
                "继续安装可能运行被篡改的程序，造成系统被植入恶意代码、数据或账号信息泄露，甚至系统损坏且无法恢复。\n\n" +
                "建议换个节点重新下载。还是继续安装吗？",
                "再确认一下", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            return secondWarning == MessageBoxResult.Yes;
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }

        private void DirectDownload_Click(object sender, RoutedEventArgs e)
        {
            StartMultiThreadDownload(_targetExeUrl);
        }

        private void MirrorDownload_Click(object sender, RoutedEventArgs e)
        {
            if (MirrorListView.SelectedItem is MirrorInfo selectedMirror)
            {
                StartMultiThreadDownload(selectedMirror.FullUrl);
            }
            else
            {
                MessageBox.Show("请先在列表中选择一个可用节点", "提示");
            }
        }

        private void StartMultiThreadDownload(string url)
        {
            SelectionPanel.Visibility = Visibility.Collapsed;
            ActionPanel.Visibility = Visibility.Collapsed;
            DownloadProgressPanel.Visibility = Visibility.Visible;
            SubtitleText.Text = "正在获取更新...";
            DownloadMainText.Text = "正在下载更新文件...";
            
            if (CancelBtn != null) CancelBtn.IsEnabled = true;

            string savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _fileName);

            var downloadOpt = new DownloadConfiguration()
            {
                ChunkCount = 4, 
                ParallelDownload = true,
                MaxTryAgainOnFailure = 9999,
                ClearPackageOnCompletionWithFailure = false,
                CustomHttpClientFactory = () => new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                }) 
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

                    if (e.Cancelled)
                    {
                        if (_isNodeDownload)
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
                        if (_isNodeDownload)
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
                                "下载的文件SHA-256校验失败，文件可能在传输过程中被损坏或篡改\n\n已自动删除该文件，请更换其他节点重新下载",
                                "文件完整性校验失败",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);

                            ResetToSelectionUI();
                            return;
                        }

                        if (!_isPreviewMode)
                        {
                            DownloadMainText.Text = "正在校验数字签名...";

                            uint trustResult = VerifyAuthenticodeSignature(savePath);
                            if (trustResult != 0 && !ConfirmUntrustedInstaller(savePath))
                            {
                                ResetToSelectionUI();
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
    }

    public class MirrorInfo
    {
        public string Domain { get; set; }
        public string FullUrl { get; set; }
        public bool IsSuccess { get; set; }
        public long ResponseTimeMs { get; set; }
        public string StatusDesc { get; set; }
    }
}
