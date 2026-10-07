/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using FufuLauncher.Contracts.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Data.Repositories;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;

namespace FufuLauncher.ViewModels;

public class LocalGachaData
{
    public string Url
    {
        get;
        set;
    }

    public List<GachaLogItem> CharacterLogs
    {
        get;
        set;
    } = new();

    public List<GachaLogItem> WeaponLogs
    {
        get;
        set;
    } = new();

    public List<GachaLogItem> StandardLogs
    {
        get;
        set;
    } = new();
}

public partial class GachaAnalysisModel : ObservableObject
{
    private bool _isFetchingPoolMetadata;
    private Dictionary<string, int> _charNameToIdMap;
    private Dictionary<string, int> _weaponNameToIdMap;
    private readonly string _gachaDataPath;
    private readonly MetadataRepository _metadataRepo;
    private readonly GachaService _gachaService;
    private readonly AccountManager _accountManager;
    private readonly ILocalSettingsService _localSettingsService;
    private const string LastSelectedUidKey = "GachaLastSelectedUid";

    private static readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    });

    static GachaAnalysisModel()
    {
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://webstatic.mihoyo.com");
        }
    }

    private List<GachaLogItem> _cachedCharacterLogs = new();
    private List<GachaLogItem> _cachedWeaponLogs = new();
    private List<GachaLogItem> _cachedChronicledLogs = new();
    private List<GachaLogItem> _cachedNoviceLogs = new();
    private List<GachaLogItem> _cachedStandardLogs = new();
    private List<ScrapedMetadata> _savedMetadata = new();
    private string _currentUid = "";
    private string _uidBeforeAddNew = "";
    private int _refreshVersion;
    private bool _analysisDashboardDirty = true;
    [ObservableProperty] private string _gachaUrl;
    [ObservableProperty] private string _crawlerStatus = "等待获取数据...";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSwitchRole))]
    private bool _isFetching;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSwitchRole))]
    private bool _isScraping;

    public bool CanSwitchRole => IsDataLoaded && !IsFetching && !IsScraping;
    [ObservableProperty] private GachaStatistic _characterStats = new() { PoolName = "角色活动" };
    [ObservableProperty] private GachaStatistic _weaponStats = new() { PoolName = "武器活动" };
    [ObservableProperty] private GachaStatistic _chronicledStats = new() { PoolName = "集录祈愿" };
    [ObservableProperty] private GachaStatistic _standardStats = new() { PoolName = "常驻祈愿" };
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _characterFiveStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _weaponFiveStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _chronicledFiveStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _standardFiveStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _characterFourStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _weaponFourStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _chronicledFourStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _standardFourStars = new();
    [ObservableProperty] private ObservableCollection<ScrapedMetadata> _characterMetadataPreview = new();
    [ObservableProperty] private ObservableCollection<ScrapedMetadata> _weaponMetadataPreview = new();
    [ObservableProperty] private ObservableCollection<string> _knownUids = new();
    [ObservableProperty] private ObservableCollection<string> _uidComboItems = new();
    [ObservableProperty] private string _selectedUid = "";
    [ObservableProperty] private bool _isCharacterFourStarVisible;
    [ObservableProperty] private bool _isWeaponFourStarVisible;
    [ObservableProperty] private bool _isChronicledFourStarVisible;
    [ObservableProperty] private bool _isStandardFourStarVisible;

    public bool ShowCharacterFourDivider => IsCharacterFourStarVisible && CharacterFourStars?.Count > 0;
    public bool ShowWeaponFourDivider => IsWeaponFourStarVisible && WeaponFourStars?.Count > 0;
    public bool ShowChronicledFourDivider => IsChronicledFourStarVisible && ChronicledFourStars?.Count > 0;
    public bool ShowStandardFourDivider => IsStandardFourStarVisible && StandardFourStars?.Count > 0;

    public bool ShowCharacterNoRecords => CharacterStats?.FiveStarCount == 0 &&
                                          (!IsCharacterFourStarVisible || CharacterFourStars?.Count == 0);

    public bool ShowWeaponNoRecords =>
        WeaponStats?.FiveStarCount == 0 && (!IsWeaponFourStarVisible || WeaponFourStars?.Count == 0);

    public bool ShowChronicledNoRecords => ChronicledStats?.FiveStarCount == 0 &&
                                           (!IsChronicledFourStarVisible || ChronicledFourStars?.Count == 0);

    public bool ShowStandardNoRecords => StandardStats?.FiveStarCount == 0 &&
                                         (!IsStandardFourStarVisible || StandardFourStars?.Count == 0);

    public const string AddNewUserItem = "＋ 添加新用户";
    [ObservableProperty] private bool _hasGachaData;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSwitchRole))]
    private bool _isDataLoaded;

    [ObservableProperty] private bool _isOverviewSelected = true;
    [ObservableProperty] private bool _isHistorySelected;
    [ObservableProperty] private bool _isAnalysisLoading;
    [ObservableProperty] private bool _isAnalysisReady;
    [ObservableProperty] private GachaAnalysisDashboard _analysisDashboard = GachaAnalysisDashboard.Empty();
    [ObservableProperty] private bool _isCardViewMode;
    public bool IsListViewMode => !IsCardViewMode;
    public bool ShowOverviewList => IsOverviewSelected && !IsCardViewMode;
    public bool ShowOverviewCards => IsOverviewSelected && IsCardViewMode;

    public bool IsAnalysisSelected => !IsOverviewSelected && !IsHistorySelected;
    public bool ShowAnalysisLoading => IsAnalysisSelected && IsAnalysisLoading;
    public bool ShowAnalysisContent => IsAnalysisSelected && IsAnalysisReady && !IsAnalysisLoading;

    public Action RequestMetadataScrapeAction;
    public Action<string> OnErrorAction;
    public Func<Window> GetWindow;
    public Func<string, string, Task<bool>> OnUidMismatchAsync;
    public Func<string, string, string, Task> OnShowConfirmDialogAsync;
    public Func<string, Task> OnRequireReLoginAsync;

    public GachaAnalysisModel(ILocalSettingsService localSettingsService, AccountManager accountManager,
        MetadataRepository metadataRepo)
    {
        _localSettingsService = localSettingsService;

        _gachaDataPath = Helpers.AppPaths.GachaDataFile;
        _metadataRepo = metadataRepo;
        _gachaService = new GachaService();
        _accountManager = accountManager;
    }

    partial void OnIsScrapingChanged(bool value)
    {
        if (!value && _pendingRoleSwitch)
            App.MainWindow.DispatcherQueue.TryEnqueue(async () => await FinishPendingRoleSwitchAsync());
    }

    partial void OnIsCharacterFourStarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCharacterNoRecords));
        OnPropertyChanged(nameof(ShowCharacterFourDivider));
    }

    partial void OnIsWeaponFourStarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWeaponNoRecords));
        OnPropertyChanged(nameof(ShowWeaponFourDivider));
    }

    partial void OnIsChronicledFourStarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowChronicledNoRecords));
        OnPropertyChanged(nameof(ShowChronicledFourDivider));
    }

    partial void OnIsStandardFourStarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStandardNoRecords));
        OnPropertyChanged(nameof(ShowStandardFourDivider));
    }

    partial void OnIsOverviewSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnalysisSelected));
        OnPropertyChanged(nameof(ShowAnalysisLoading));
        OnPropertyChanged(nameof(ShowAnalysisContent));
        OnPropertyChanged(nameof(ShowOverviewList));
        OnPropertyChanged(nameof(ShowOverviewCards));
    }

    partial void OnIsHistorySelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnalysisSelected));
        OnPropertyChanged(nameof(ShowAnalysisLoading));
        OnPropertyChanged(nameof(ShowAnalysisContent));
        OnPropertyChanged(nameof(ShowHistoryLoading));
        OnPropertyChanged(nameof(ShowHistoryContent));
        OnPropertyChanged(nameof(ShowHistoryEmpty));
    }

    partial void OnIsCardViewModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListViewMode));
        OnPropertyChanged(nameof(ShowOverviewList));
        OnPropertyChanged(nameof(ShowOverviewCards));
    }

    partial void OnIsAnalysisLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAnalysisLoading));
        OnPropertyChanged(nameof(ShowAnalysisContent));
    }

    partial void OnIsAnalysisReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAnalysisLoading));
        OnPropertyChanged(nameof(ShowAnalysisContent));
    }
}
