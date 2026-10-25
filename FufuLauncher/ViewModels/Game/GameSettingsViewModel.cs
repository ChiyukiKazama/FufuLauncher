/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Activation;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FufuLauncher.ViewModels
{
    public class GameAccount
    {
        public Guid InnerId
        {
            get;
            set;
        } = Guid.NewGuid();

        public string Name
        {
            get;
            set;
        } = string.Empty;

        public string MihoyoSDK
        {
            get;
            set;
        } = string.Empty;

        public string? Mid
        {
            get;
            set;
        }

        public string? MacAddress
        {
            get;
            set;
        }

        public bool IsExpired
        {
            get;
            set;
        }

        public DateTime? LastUsed
        {
            get;
            set;
        }

        public static GameAccount Create(string name, string sdk, string? mid, string? mac) => new()
        {
            Name = name,
            MihoyoSDK = sdk,
            Mid = mid,
            MacAddress = mac,
            LastUsed = DateTime.Now
        };
    }

    public partial class GameSettingsViewModel : ObservableRecipient
    {
        private readonly IGameConfigService _gameConfigService;
        private readonly ILocalSettingsService _localSettingsService;
        private const string GamePathKey = "GameInstallationPath";
        private const string RegistryKey = @"HKEY_CURRENT_USER\Software\miHoYo\原神";
        private const string RegistryValueName = "MIHOYOSDK_ADL_PROD_CN_h3123967166";

        [ObservableProperty] private GameConfig? currentGameConfig;

        [ObservableProperty] private bool isLoading;

        [ObservableProperty] private ObservableCollection<GameAccount> accounts = new();

        [ObservableProperty] private GameAccount? selectedAccount;

        [ObservableProperty] private bool canAccessRegistry;

        private string? _lastLoadedPath;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly string _accountsFilePath;

        public ICommand SelectGamePathCommand
        {
            get;
        }

        public IAsyncRelayCommand LoadAccountsCommand
        {
            get;
        }

        public IAsyncRelayCommand AddCurrentAccountCommand
        {
            get;
        }

        public IAsyncRelayCommand<GameAccount> SwitchAccountCommand
        {
            get;
        }

        public IAsyncRelayCommand<GameAccount> DeleteAccountCommand
        {
            get;
        }


        public GameSettingsViewModel(IGameConfigService gameConfigService, ILocalSettingsService localSettingsService)
        {
            _gameConfigService = gameConfigService;
            _localSettingsService = localSettingsService;
            _dispatcherQueue = App.MainWindow.DispatcherQueue;

            _accountsFilePath = Helpers.AppPaths.GameAccountsFile;

            SelectGamePathCommand = new AsyncRelayCommand(SelectGamePathAsync);
            LoadAccountsCommand = new AsyncRelayCommand(LoadAccountsAsync);
            AddCurrentAccountCommand = new AsyncRelayCommand(AddCurrentAccountAsync);
            SwitchAccountCommand = new AsyncRelayCommand<GameAccount>(SwitchAccountAsync);
            DeleteAccountCommand = new AsyncRelayCommand<GameAccount>(DeleteAccountAsync);

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                var savedPath = await _localSettingsService.ReadSettingAsync(GamePathKey) as string;
                if (!string.IsNullOrEmpty(savedPath))
                {
                    savedPath = savedPath.Trim('"').Trim();
                    DebugLog.Info("GameSettingsViewModel", $"[GameInfoPage] Init read path: '{savedPath}'");
                    _lastLoadedPath = savedPath;
                    await LoadGameInfoAsync(savedPath);
                }

                await LoadAccountsAsync();
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameSettingsViewModel", $"Initialization failed: {ex.Message}");
            }
        }

        private async Task SelectGamePathAsync()
        {
            try
            {
                if (!_dispatcherQueue.HasThreadAccess)
                {
                    DebugLog.Info("GameSettingsViewModel", "[Error] Not executing on UI thread");
                    return;
                }

                var path = await FilePickerService.PickOpenFileAsync(
                    null,
                    new[] { ("Common_ExecutableFiles".GetLocalized(), new[] { ".exe" }) },
                    PickerLocationId.ComputerFolder,
                    msg => _ = ShowError(msg));

                if (!string.IsNullOrEmpty(path))
                {
                    var folder = Path.GetDirectoryName(path)?.Trim('"').Trim();

                    if (!string.IsNullOrEmpty(folder))
                    {
                        DebugLog.Info("GameSettingsViewModel",
                            $"[GameInfoPage] User selected program file: '{path}', extracted directory: '{folder}'");

                        await LoadGameInfoAsync(folder);
                        await _localSettingsService.SaveSettingAsync(GamePathKey, folder);
                        _lastLoadedPath = folder;

                        WeakReferenceMessenger.Default.Send(new GamePathChangedMessage(folder));
                        DebugLog.Info("GameSettingsViewModel", "[GameInfoPage] Path change message sent");
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                await ShowError("Blank_PermissionOrOccupied_GamePath".GetLocalized());
                DebugLog.Info("GameSettingsViewModel", "[Critical] Admin mode permission issue");
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameSettingsViewModel",
                    $"Path selection failed: {ex.Message}\nStack trace: {ex.StackTrace}");
                await ShowError(string.Format("Blank_SelectPathFailed_Format".GetLocalized(), ex.Message));
            }
        }

        private async Task ShowError(string message)
        {
            try
            {
                await _dispatcherQueue.EnqueueAsync(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Common_OperationFailed".GetLocalized(),
                        Content = message,
                        CloseButtonText = "Common_Confirm".GetLocalized(),
                        XamlRoot = App.MainWindow.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                });
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameSettingsViewModel", $"Failed to show error dialog: {ex.Message}");
            }
        }

        private async Task LoadGameInfoAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            IsLoading = true;
            try
            {
                DebugLog.Info("GameSettingsViewModel", $"[GameInfoPage] Loading game info: {path}");
                CurrentGameConfig = await _gameConfigService.LoadGameConfigAsync(path);
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameSettingsViewModel", $"Load failed: {ex.Message}");
                CurrentGameConfig = null;
            }
            finally
            {
                IsLoading = false;
            }
        }

        #region 账号管理

        private async Task LoadAccountsAsync()
        {
            try
            {
                CanAccessRegistry = await TestRegistryAccessAsync();
                if (!CanAccessRegistry)
                {
                    DebugLog.Info("GameSettingsViewModel", "Cannot access registry");
                    return;
                }

                var accounts = await ReadAccountsFromFileAsync();
                Accounts.Clear();
                foreach (var account in accounts.OrderByDescending(a => a.LastUsed))
                {
                    Accounts.Add(account);
                }

                var current = await GetCurrentAccountFromRegistryAsync();
                SelectedAccount = current;
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameSettingsViewModel", $"Failed to load accounts: {ex.Message}");
                CanAccessRegistry = false;
            }
        }

        private async Task AddCurrentAccountAsync()
        {
            try
            {
                IsLoading = true;

                var current = await GetCurrentAccountFromRegistryAsync();
                if (current == null)
                {
                    DebugLog.Info("GameSettingsViewModel", "Current account info not found in registry");
                    return;
                }

                var accounts = await ReadAccountsFromFileAsync();
                if (accounts.Any(a => a.Mid == current.Mid))
                {
                    DebugLog.Info("GameSettingsViewModel", "Account already exists");
                    return;
                }

                var newAccount = GameAccount.Create(
                    string.Format("Blank_DefaultAccountName_Format".GetLocalized(), DateTime.Now.ToString("MMdd_HHmm"),
                        current.Mid ?? "Unknown"),
                    current.MihoyoSDK,
                    current.Mid,
                    current.MacAddress
                );

                accounts.Add(newAccount);
                await WriteAccountsToFileAsync(accounts);

                Accounts.Add(newAccount);
                DebugLog.Info("GameSettingsViewModel", $"Account added: {newAccount.Name}");
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameSettingsViewModel", $"Failed to add account: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SwitchAccountAsync(GameAccount account)
        {
            if (account == null) return;

            try
            {
                IsLoading = true;
                await SetCurrentAccountToRegistryAsync(account);
                SelectedAccount = account;
                await LoadAccountsAsync();

                DebugLog.Info("GameSettingsViewModel", $"Switched to account: {account.Name}");
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameSettingsViewModel", $"Failed to switch account: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DeleteAccountAsync(GameAccount account)
        {
            if (account == null) return;

            try
            {
                var accounts = await ReadAccountsFromFileAsync();
                accounts.RemoveAll(a => a.InnerId == account.InnerId);
                await WriteAccountsToFileAsync(accounts);

                Accounts.Remove(account);
                DebugLog.Info("GameSettingsViewModel", $"Account deleted: {account.Name}");
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameSettingsViewModel", $"Failed to delete account: {ex.Message}");
            }
        }

        #endregion

        #region 注册表和文件操作

        private async Task<bool> TestRegistryAccessAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RegistryKey.Replace("HKEY_CURRENT_USER\\", ""));
                    return key != null;
                }
                catch
                {
                    return false;
                }
            });
        }

        private async Task<GameAccount?> GetCurrentAccountFromRegistryAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var value = Registry.GetValue(RegistryKey, RegistryValueName, null);
                    if (value is not byte[] bytes || bytes.Length == 0)
                        return null;

                    int nullIndex = Array.IndexOf(bytes, (byte)0);
                    int length = nullIndex >= 0 ? nullIndex : bytes.Length;
                    var sdkString = Encoding.UTF8.GetString(bytes, 0, length);

                    return new GameAccount
                    {
                        MihoyoSDK = sdkString,
                        Mid = "Blank_CurrentAccount".GetLocalized(),
                        Name = "Blank_CurrentLoggedInAccount".GetLocalized()
                    };
                }
                catch
                {
                    return null;
                }
            });
        }

        private async Task SetCurrentAccountToRegistryAsync(GameAccount account)
        {
            await Task.Run(() =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKey.Replace("HKEY_CURRENT_USER\\", ""));
                if (key == null)
                    throw new InvalidOperationException("Blank_CannotReadRegistry".GetLocalized());

                var sdkBytes = Encoding.UTF8.GetBytes(account.MihoyoSDK);
                var target = new byte[sdkBytes.Length + 1];
                Array.Copy(sdkBytes, target, sdkBytes.Length);
                target[sdkBytes.Length] = 0;

                key.SetValue(RegistryValueName, target, RegistryValueKind.Binary);
            });
        }

        private async Task<List<GameAccount>> ReadAccountsFromFileAsync()
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(_accountsFilePath))
                    return new List<GameAccount>();

                try
                {
                    var json = File.ReadAllText(_accountsFilePath, Encoding.UTF8);
                    return JsonSerializer.Deserialize<List<GameAccount>>(json) ?? new List<GameAccount>();
                }
                catch (JsonException ex)
                {
                    DebugLog.Info("GameSettingsViewModel",
                        $"[BlankViewModel] Failed to parse game account file: {ex.Message}");
                    return new List<GameAccount>();
                }
            });
        }

        private async Task WriteAccountsToFileAsync(List<GameAccount> accounts)
        {
            await Task.Run(() =>
            {
                var directory = Path.GetDirectoryName(_accountsFilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(accounts, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                File.WriteAllText(_accountsFilePath, json, Encoding.UTF8);
            });
        }

        #endregion
    }
}