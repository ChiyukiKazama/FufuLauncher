/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Activation;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using FufuLauncher.Services.MiHoYo.DailyNote;
using Microsoft.UI.Dispatching;
using FufuLauncher.Helpers;

namespace FufuLauncher.ViewModels
{
    public partial class MainViewModel : ObservableRecipient
    {
        #region 服务字段

        private readonly IHoyoverseContentService _contentService;
        private readonly IBackgroundRenderer _backgroundRenderer;
        private readonly IDevBuildDetectionService _devBuildDetectionService;
        private readonly ILocalSettingsService _localSettingsService;
        private readonly IHoyoverseCheckinService _checkinService;
        private readonly IUnifiedCheckinService _unifiedCheckinService;
        private readonly IGameLauncherService _gameLauncherService;
        private readonly INotificationService _notificationService;
        private readonly DailyNoteCardService _dailyNoteCardService;
        private readonly GameSessionService _gameSessionService;
        private readonly DispatcherQueue _dispatcherQueue;

        #endregion

        #region 构造函数与消息订阅

        public MainViewModel(
            IHoyoverseBackgroundService backgroundService,
            IHoyoverseContentService contentService,
            IBackgroundRenderer backgroundRenderer,
            IDevBuildDetectionService devBuildDetectionService,
            ILocalSettingsService localSettingsService,
            IHoyoverseCheckinService checkinService,
            IUnifiedCheckinService unifiedCheckinService,
            IGameLauncherService gameLauncherService,
            ILauncherService launcherService,
            INavigationService navigationService,
            INotificationService notificationService,
            DailyNoteCardService dailyNoteCardService,
            GameSessionService gameSessionService)
        {
            _contentService = contentService;
            _backgroundRenderer = backgroundRenderer;
            _devBuildDetectionService = devBuildDetectionService;
            _localSettingsService = localSettingsService;
            _checkinService = checkinService;
            _unifiedCheckinService = unifiedCheckinService;
            _gameLauncherService = gameLauncherService;
            _notificationService = notificationService;
            _dailyNoteCardService = dailyNoteCardService;
            _gameSessionService = gameSessionService;
            _dispatcherQueue = App.MainWindow.DispatcherQueue;

            WeakReferenceMessenger.Default.Register<FufuLauncher.Messages.TextStyleChangedMessage>(this, async (r, m) =>
            {
                await LoadTextStylesAsync();
            });

            WeakReferenceMessenger.Default.Register<CardVisibilityChangedMessage>(this, async (r, m) =>
            {
                await LoadCardVisibilityAsync();
            });

            WeakReferenceMessenger.Default.Register<AccountChangedMessage>(this, async (r, m) =>
            {
                await ClearDailyNoteDataAsync();
                await Task.WhenAll(LoadDailyNoteAsync(force: true), LoadCheckinStatusAsync());
            });
            WeakReferenceMessenger.Default.Register<GameRoleChangedMessage>(this, (r, m) =>
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    if (App.GetService<AccountManager>().ActiveAccountId != m.AccountId) return;
                    await ClearDailyNoteDataAsync();
                    await Task.WhenAll(LoadDailyNoteAsync(force: true), LoadCheckinStatusAsync());
                }));
            WeakReferenceMessenger.Default.Register<GameRolesUpdatedMessage>(this, (r, m) =>
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    var account = App.GetService<AccountManager>().GetActiveAccountEntry();
                    if (account?.Id != m.AccountId) return;
                    if (GameRoleSelection.Current(account) == null)
                    {
                        await ClearDailyNoteDataAsync();
                        await Task.WhenAll(LoadDailyNoteAsync(force: true), LoadCheckinStatusAsync());
                    }
                }));

            WeakReferenceMessenger.Default.Register<DevBuildDetectionCompletedMessage>(this, async (r, m) =>
            {
                if (m.Value)
                {
                    await LoadAvailableBackgroundsAsync();
                    await LoadBackgroundAsync();
                }
                else
                {
                    if (PreferVideoBackground)
                    {
                        PreferVideoBackground = false;
                        await _localSettingsService.SaveSettingAsync("PreferVideoBackground", false);
                        await _localSettingsService.SaveSettingAsync("UserPreferVideoBackground", false);
                    }

                    await LoadAvailableBackgroundsAsync();
                    await LoadBackgroundAsync();
                }
            });

            _bannerTimer = _dispatcherQueue.CreateTimer();
            _bannerTimer.Interval = TimeSpan.FromSeconds(5);
            _bannerTimer.Tick += (s, e) => RotateBanner();

            LoadBackgroundCommand = new AsyncRelayCommand(LoadBackgroundAsync);
            TogglePanelCommand = new RelayCommand(() => IsPanelExpanded = !IsPanelExpanded);
            ToggleInfoCardCommand = new RelayCommand(ToggleInfoCard);
            ToggleBackgroundTypeCommand = new RelayCommand(ToggleBackgroundType);
            ExecuteCheckinCommand = new AsyncRelayCommand(ExecuteCheckinAsync);
            LaunchGameCommand =
                new AsyncRelayCommand(LaunchGameAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            OpenScreenshotFolderCommand = new AsyncRelayCommand(OpenScreenshotFolderAsync);
            SelectSpecificBackgroundCommand = new AsyncRelayCommand<BackgroundUrlInfo>(SelectSpecificBackgroundAsync);

            OpenPresetManagerCommand = new AsyncRelayCommand(OpenPresetManagerAsync);
            QuickSwitchPresetCommand = new RelayCommand<PresetModel>(QuickSwitchPreset);
            SelectInjectionModuleCommand = new RelayCommand<InjectionModuleInfo>(SelectInjectionModule);

            InitializeInjectionModules();

            WeakReferenceMessenger.Default.Register<GamePathChangedMessage>(this, (r, m) =>
            {
                _dispatcherQueue?.TryEnqueue(() => UpdateLaunchButtonState());
            });

            _gameMonitoringCts = new CancellationTokenSource();
            _ = StartGameMonitoringLoopAsync(_gameMonitoringCts.Token);

            WeakReferenceMessenger.Default.Register<PanelOpacityChangedMessage>(this, (r, m) =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    _panelOpacityValue = m.Value;
                    UpdatePanelBackgroundBrush();
                });
            });
        }

        #endregion

        #region 生命周期

        public async Task InitializeAsync()
        {
            await LoadTextStylesAsync();
            await LoadUserPreferencesAsync();
            await SwitchToStaticBackgroundOnVersionChangeAsync();
            await LoadCustomBackgroundPathAsync();

            var loadBackgroundTask = LoadBackgroundAsync();
            var loadAvailableBackgroundsTask = LoadAvailableBackgroundsAsync();
            var loadContentTask = LoadContentAsync();
            var loadCheckinStatusTask = LoadCheckinStatusAsync();
            var loadDailyNoteTask = LoadDailyNoteAsync();
            var getInjectionTask = _gameLauncherService.GetUseInjectionAsync();

            var loadPinnedPresetsTask = LoadPinnedPresetsAsync();

            await Task.WhenAll(
                loadBackgroundTask,
                loadAvailableBackgroundsTask,
                loadContentTask,
                loadCheckinStatusTask,
                loadDailyNoteTask,
                getInjectionTask,
                loadPinnedPresetsTask
            );

            UseInjection = getInjectionTask.Result;
            await LoadInjectionModuleAsync();

            try
            {
                var savedOpacity = await _localSettingsService.ReadSettingAsync("PanelBackgroundOpacity");
                if (savedOpacity != null)
                {
                    _panelOpacityValue = Convert.ToDouble(savedOpacity);
                }
            }
            catch
            {
            }

            UpdatePanelBackgroundBrush();
            UpdateLaunchButtonState();
        }

        public async Task OnPageReturnedAsync()
        {
            DebugLog.Info("MainViewModel", "[MainViewModel] Page returned, refreshing server config...");

            await RefreshSettingsAsync();
            await LoadUserPreferencesAsync();

            var refreshGameTask = ForceRefreshGameStateAsync();
            var checkinTask = LoadCheckinStatusAsync();
            var loadPinnedPresetsTask = LoadPinnedPresetsAsync();

            await Task.WhenAll(refreshGameTask, checkinTask, loadPinnedPresetsTask);
        }

        private async Task RefreshSettingsAsync()
        {
            _cachedProcessNames = null;
            var isInternationalObj = await _localSettingsService.ReadSettingAsync("IsInternationalAccount");
            _isInternationalAccount = isInternationalObj != null && isInternationalObj.ToString().ToLower() == "true";
            DebugLog.Info("MainViewModel", $"[MainViewModel] Config refresh: {_isInternationalAccount}");
        }

        public void Cleanup()
        {
            _bannerTimer?.Stop();
            try
            {
                _gameMonitoringCts?.Cancel();
                _gameMonitoringCts?.Dispose();
            }
            catch
            {
            }

            if (BackgroundVideoPlayer != null)
            {
                try
                {
                    BackgroundVideoPlayer.Pause();
                    BackgroundVideoPlayer = null;
                }
                catch
                {
                }
            }

            WeakReferenceMessenger.Default.UnregisterAll(this);
        }

        #endregion

        #region UI 线程调度

        private Task UpdateUI(Action uiAction)
        {
            if (_dispatcherQueue == null)
            {
                uiAction();
                return Task.CompletedTask;
            }

            return _dispatcherQueue.EnqueueAsync(() => uiAction());
        }

        #endregion
    }
}