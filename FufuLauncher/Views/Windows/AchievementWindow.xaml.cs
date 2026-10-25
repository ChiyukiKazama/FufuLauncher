/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using FufuLauncher.Data.Repositories;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Views;

public sealed partial class AchievementWindow : Window, INotifyPropertyChanged
{
    private const string AchievementDataFileName = "achievements.json";

    public AchievementViewModel ViewModel
    {
        get;
    } = new();

    private readonly string _workFilePath;
    private readonly string _assetsFilePath;
    private readonly AchievementRepository _achievementRepo;
    private bool _isDataLoaded;
    private Dictionary<AchievementItem, int> _itemUids = new();
    private HttpListener _listener;
    private bool _keepRunning = true;
    private bool _isBatchProcessing;
    private readonly string _archivesDir;
    private readonly string _profileRecordPath;
    private string _currentProfileName = "AchievementWindow_DefaultProfile".GetLocalized();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentProfileName
    {
        get => _currentProfileName;
        set
        {
            if (_currentProfileName != value)
            {
                _currentProfileName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentProfileName)));
                Bindings.Update();
            }
        }
    }

    public AchievementWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        string docPath = Helpers.AppPaths.DataDir;

        _archivesDir = Path.Combine(docPath, "archives");
        try
        {
            if (!Directory.Exists(_archivesDir))
            {
                Directory.CreateDirectory(_archivesDir);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("AchievementWindow",
                $"[ExceptionMarker] Failed to create archives save folder: {ex.Message}");
            _archivesDir = docPath;
        }

        _profileRecordPath = Path.Combine(docPath, "current_profile.txt");

        if (File.Exists(_profileRecordPath))
        {
            CurrentProfileName = File.ReadAllText(_profileRecordPath).Trim();
        }
        else
        {
            CurrentProfileName = "AchievementWindow_UnnamedProfile".GetLocalized();
        }

        _workFilePath = Path.Combine(docPath, "achievements.db");
        _assetsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets",
            AchievementDataFileName);
        _achievementRepo = App.GetService<AchievementRepository>();

        LoadData();
        StartLocalServer();
        Closed += (s, e) =>
        {
            _keepRunning = false;
            _listener?.Close();
        };
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowText(IntPtr hWnd, string text);
}