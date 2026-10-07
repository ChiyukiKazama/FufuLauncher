/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Services;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using File = System.IO.File;

namespace FufuLauncher.Views;

public sealed partial class BlankPage
{
    #region 创建桌面快捷方式与启动命令

    private async void CreateShortcut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var localSettings = App.GetService<ILocalSettingsService>();
            var settingObj = await localSettings.ReadSettingAsync("GameInstallationPath");
            var rawPath = settingObj as string;

            if (string.IsNullOrEmpty(rawPath))
            {
                await ShowError("Err_GamePathNotSet".GetLocalized());
                return;
            }

            var finalExePath = rawPath;

            if (Directory.Exists(rawPath))
            {
                var exeNames = await GameExeManager.GetExeNamesAsync();
                bool found = false;
                foreach (var name in exeNames)
                {
                    var testPath = Path.Combine(rawPath, name);
                    if (File.Exists(testPath))
                    {
                        finalExePath = testPath;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    await ShowError(string.Format("Err_ExeNotFoundInFolder_Format".GetLocalized(), rawPath));
                    return;
                }
            }

            var appPath = Environment.ProcessPath;

            var presets = await Task.Run(() => PluginPresetStorage.ReadPresets(AppPaths.PluginPresetsDir));

            var presetComboBox = new ComboBox
            {
                ItemsSource = presets,
                DisplayMemberPath = "Name",
                PlaceholderText = "Placeholder_DefaultUseCurrentPreset".GetLocalized(),
                Width = 300,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var defaultShortcutName = Path.GetFileNameWithoutExtension("FileName_ShortcutName".GetLocalized());
            var nameTextBox = new TextBox
            {
                Text = defaultShortcutName,
                PlaceholderText = "Placeholder_ShortcutName".GetLocalized(),
                Width = 300,
                Margin = new Thickness(0, 10, 0, 0)
            };
            bool nameEdited = false;
            nameTextBox.TextChanged += (_, _) => nameEdited = true;
            presetComboBox.SelectionChanged += (_, _) =>
            {
                if (!nameEdited && presetComboBox.SelectedItem is PresetModel preset)
                {
                    nameTextBox.Text = string.IsNullOrWhiteSpace(preset.Name) ? defaultShortcutName : preset.Name;
                }
            };

            var customParamsObj = await localSettings.ReadSettingAsync("CustomLaunchParameters");
            var customLaunchParams = customParamsObj as string;
            string customParamsDisplay = string.IsNullOrWhiteSpace(customLaunchParams)
                ? "None_Value".GetLocalized()
                : customLaunchParams;

            var contentPanel = new StackPanel { Spacing = 10 };
            contentPanel.Children.Add(new TextBlock
                { Text = "Msg_ChooseShortcutAction".GetLocalized(), TextWrapping = TextWrapping.Wrap });
            contentPanel.Children.Add(new TextBlock
            {
                Text = string.Format("Msg_ImportedLaunchParams_Format".GetLocalized(), customParamsDisplay),
                Opacity = 0.7, TextWrapping = TextWrapping.Wrap
            });
            contentPanel.Children.Add(new TextBlock
                { Text = "Label_SpecifyInjectionPreset".GetLocalized(), Margin = new Thickness(0, 5, 0, 0) });
            contentPanel.Children.Add(presetComboBox);
            contentPanel.Children.Add(new TextBlock
                { Text = "Label_ShortcutName".GetLocalized(), Margin = new Thickness(0, 5, 0, 0) });
            contentPanel.Children.Add(nameTextBox);

            var choiceDialog = new ContentDialog
            {
                Title = "Title_ChooseAction".GetLocalized(),
                Content = contentPanel,
                PrimaryButtonText = "Btn_CreateDesktopShortcut".GetLocalized(),
                SecondaryButtonText = "Btn_CopyLaunchCommand".GetLocalized(),
                CloseButtonText = "CancelBtn".GetLocalized(),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var choiceResult = await choiceDialog.ShowAsync();

            if (choiceResult == ContentDialogResult.None)
            {
                return;
            }

            string presetArg = "";
            if (presetComboBox.SelectedItem is PresetModel selectedPreset)
            {
                presetArg = $" --preset {GameLauncherService.QuoteArgument(selectedPreset.Id, forceQuotes: true)}";
            }

            string customParamsArg = "";
            if (!string.IsNullOrWhiteSpace(customLaunchParams))
            {
                customParamsArg = $" {customLaunchParams}";
            }

            var argsOnly =
                $"--elevated-inject {GameLauncherService.QuoteArgument(finalExePath)}{presetArg} --{customParamsArg}";
            var fullCommandLine = $"{GameLauncherService.QuoteArgument(appPath ?? string.Empty)} {argsOnly}";

            if (choiceResult == ContentDialogResult.Primary)
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                var shortcutName = string.IsNullOrWhiteSpace(nameTextBox.Text)
                    ? defaultShortcutName
                    : nameTextBox.Text.Trim();
                foreach (var invalidChar in Path.GetInvalidFileNameChars())
                {
                    shortcutName = shortcutName.Replace(invalidChar.ToString(), string.Empty);
                }

                if (string.IsNullOrWhiteSpace(shortcutName))
                {
                    shortcutName = defaultShortcutName;
                }

                if (!shortcutName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    shortcutName += ".lnk";
                }

                var shortcutPath = Path.Combine(desktopPath, shortcutName);

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);

                var shortcut = shell.CreateShortcut(shortcutPath);

                shortcut.TargetPath = appPath;
                shortcut.Arguments = argsOnly;
                shortcut.WorkingDirectory = AppContext.BaseDirectory;
                shortcut.IconLocation = finalExePath + ",0";
                shortcut.Description = "Desc_ShortcutDescription".GetLocalized();

                shortcut.Save();

                using (FileStream fs = new(shortcutPath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(21, SeekOrigin.Begin);
                    int b = fs.ReadByte();
                    fs.Seek(21, SeekOrigin.Begin);
                    fs.WriteByte((byte)(b | 0x20));
                }

                var dialog = new ContentDialog
                {
                    Title = "Title_ShortcutCreated".GetLocalized(),
                    Content = "Msg_ShortcutCreated".GetLocalized(),
                    CloseButtonText = "OkBtn".GetLocalized(),
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
            }
            else if (choiceResult == ContentDialogResult.Secondary)
            {
                var argTextBox = new TextBox
                {
                    Text = fullCommandLine,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    Height = 100,
                    AcceptsReturn = true,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, Courier New, Monospace")
                };

                var copyDialog = new ContentDialog
                {
                    Title = "Title_LaunchCommand".GetLocalized(),
                    Content = new StackPanel
                    {
                        Spacing = 10,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Msg_LaunchCommandInstructions".GetLocalized(),
                                TextWrapping = TextWrapping.Wrap
                            },
                            argTextBox
                        }
                    },
                    PrimaryButtonText = "Btn_CopyAndClose".GetLocalized(),
                    CloseButtonText = "CloseBtn".GetLocalized(),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot
                };

                var copyResult = await copyDialog.ShowAsync();

                if (copyResult == ContentDialogResult.Primary)
                {
                    var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText(fullCommandLine);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                }
            }
        }
        catch (Exception ex)
        {
            await ShowError(string.Format("Err_OperationFailed_Format".GetLocalized(), ex.Message));
        }
    }

    #endregion
}