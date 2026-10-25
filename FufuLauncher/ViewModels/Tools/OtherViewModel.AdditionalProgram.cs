/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.Services;
using Windows.Storage.Pickers;

namespace FufuLauncher.ViewModels;

public partial class OtherViewModel
{
    #region Additional Program

    partial void OnAdditionalProgramPathChanged(string value)
    {
        IsApplyButtonEnabled = !string.IsNullOrWhiteSpace(value);

        if (!string.IsNullOrWhiteSpace(value))
        {
            var trimmedPath = value.Trim('"');
            if (File.Exists(trimmedPath) &&
                Path.GetExtension(trimmedPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "AdditionalProgram_PathValid".GetLocalized();
            }
            else
            {
                StatusMessage = "AdditionalProgram_ExeNotFoundOrInvalid".GetLocalized();
            }
        }
        else
        {
            StatusMessage = string.Empty;
        }
    }

    private async Task ApplyProgramPathAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(AdditionalProgramPath))
            {
                StatusMessage = "AdditionalProgram_PathEmpty".GetLocalized();
                return;
            }

            var trimmedPath = AdditionalProgramPath.Trim('"');

            if (File.Exists(trimmedPath) && System.IO.Path.GetExtension(trimmedPath)
                    .Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "AdditionalProgram_PathApplied".GetLocalized();

                await SaveSettingsAsync();

                _ = Task.Delay(2000).ContinueWith(_ =>
                    _dispatcherQueue?.TryEnqueue(() => StatusMessage = string.Empty));
            }
            else
            {
                StatusMessage = "AdditionalProgram_InvalidPathCheckExe".GetLocalized();

                var savedPath = await _localSettingsService.ReadSettingAsync("AdditionalProgramPath");
                if (savedPath != null)
                {
                    AdditionalProgramPath = savedPath.ToString().Trim('"');
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format("AdditionalProgram_ApplyFailed_Format".GetLocalized(), ex.Message);
            DebugLog.Info("OtherViewModel.AdditionalProgram", $"[ApplyProgramPathAsync] Failed: {ex.Message}");
        }
    }

    private async Task BrowseProgramAsync()
    {
        try
        {
            if (!_dispatcherQueue.HasThreadAccess)
            {
                DebugLog.Info("OtherViewModel.AdditionalProgram",
                    "[Error] BrowseProgramAsync not executing on UI thread");
                return;
            }

            var path = await FilePickerService.PickOpenFileAsync(
                null,
                new[] { ("Common_ExecutableFiles".GetLocalized(), new[] { ".exe" }) },
                PickerLocationId.Desktop,
                async msg =>
                {
                    StatusMessage = msg;
                    await ShowErrorAsync(msg);
                });

            if (!string.IsNullOrEmpty(path))
            {
                path = path.Trim('"');
                DebugLog.Info("OtherViewModel.AdditionalProgram", $"[OtherViewModel] User selected program: '{path}'");

                if (File.Exists(path))
                {
                    AdditionalProgramPath = path;
                }
                else
                {
                    await ShowErrorAsync("AdditionalProgram_FileNotFoundOrInaccessible".GetLocalized());
                }
            }
            else
            {
                DebugLog.Info("OtherViewModel.AdditionalProgram", "[OtherViewModel] User cancelled file selection");
            }
        }
        catch (UnauthorizedAccessException)
        {
            await ShowErrorAsync("AdditionalProgram_PermissionOrOccupied".GetLocalized());
            DebugLog.Info("OtherViewModel.AdditionalProgram", "[Critical] Admin mode permission issue");
        }
        catch (Exception ex)
        {
            DebugLog.Info("OtherViewModel.AdditionalProgram",
                $"Program selection failed: {ex.Message}\nStack trace: {ex.StackTrace}");
            await ShowErrorAsync(string.Format("AdditionalProgram_SelectFailed_Format".GetLocalized(), ex.Message));
        }
    }

    #endregion
}