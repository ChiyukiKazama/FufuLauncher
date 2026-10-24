/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Downloader;

namespace Updater;

public partial class MainWindow : Window
{
    #region Fields & Constants

    private const string AppVersion = "1.7.1.5";

    private static readonly HttpClient _httpClient = new() 
    { 
        Timeout = TimeSpan.FromSeconds(15) 
    };

    private string? _targetExeUrl;
    private string? _fileName;
    private string? _expectedSha256;
    private List<MirrorInfo> _mirrors = new();
    private DownloadService? _downloader;
    private Stopwatch _uiUpdateTimer = new();

    private DispatcherTimer? _stuckTimer;
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

    #endregion

    #region Initialization

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

    #endregion
}
