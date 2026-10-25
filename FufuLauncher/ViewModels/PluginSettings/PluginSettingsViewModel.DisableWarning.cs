/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;

namespace FufuLauncher.ViewModels;

public partial class PluginSettingsViewModel
{
    private const string ConfirmedSettingDisablesKey = "PluginSettingDisableConfirmed";

    private Dictionary<string, List<string>> _confirmedSettingDisables = new(StringComparer.OrdinalIgnoreCase);

    public bool IsResinRewardSetting(PluginSettingItem item) =>
        SelectedPluginIndex == 0 &&
        string.Equals(item.Type, "bool", StringComparison.OrdinalIgnoreCase) &&
        item.SectionKey.StartsWith("ResinItem", StringComparison.OrdinalIgnoreCase);

    public bool IsPreventDetectionPopupSetting(PluginSettingItem item) =>
        SelectedPluginIndex == 0 &&
        string.Equals(item.Type, "bool", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(item.SectionKey, "PreventDetectionPopup", StringComparison.OrdinalIgnoreCase);

    public bool RequiresSettingDisableWarning(PluginSettingItem item) =>
        IsResinRewardSetting(item) || IsPreventDetectionPopupSetting(item);

    public void ConfirmSettingDisables(IEnumerable<PluginSettingItem> items)
    {
        var target = GetSettingsTargetKey();
        if (!_confirmedSettingDisables.TryGetValue(target, out var confirmed))
        {
            confirmed = new List<string>();
            _confirmedSettingDisables[target] = confirmed;
        }

        var changed = false;
        foreach (var item in items.Where(RequiresSettingDisableWarning))
        {
            if (confirmed.Any(key => string.Equals(key, item.SectionKey, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            confirmed.Add(item.SectionKey);
            changed = true;
        }

        if (changed)
        {
            SaveConfirmedSettingDisables();
        }
    }

    private bool HasConfirmedSettingDisable(string sectionKey) =>
        _confirmedSettingDisables.TryGetValue(GetSettingsTargetKey(), out var confirmed) &&
        confirmed.Any(key => string.Equals(key, sectionKey, StringComparison.OrdinalIgnoreCase));

    private bool ApplySettingDisableGuard(PluginSettingItem item)
    {
        if (item.BoolValue || !RequiresSettingDisableWarning(item) || HasConfirmedSettingDisable(item.SectionKey))
        {
            return false;
        }

        item.BoolValue = true;
        return item.BoolValue;
    }

    private void NotifySettingDisablesRestored(IReadOnlyCollection<string> restoredNames)
    {
        if (restoredNames.Count == 0) return;

        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            "Plugin_SettingDisableRestore_Title".GetLocalized(),
            string.Format("Plugin_SettingDisableRestore_Content".GetLocalized(),
                string.Join(Environment.NewLine, restoredNames)),
            NotificationType.Warning,
            8000));
    }

    private void SaveConfirmedSettingDisables()
    {
        var localSettings = App.GetService<ILocalSettingsService>();
        if (localSettings == null) return;

        _ = localSettings.SaveSettingAsync(ConfirmedSettingDisablesKey,
            JsonSerializer.Serialize(_confirmedSettingDisables));
    }

    private static Dictionary<string, List<string>> DeserializeConfirmedSettingDisables(object? stored)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (stored == null) return result;

        try
        {
            var parsed =
                JsonSerializer.Deserialize<Dictionary<string, List<string>>>(stored.ToString() ?? string.Empty);
            if (parsed == null) return result;

            foreach (var pair in parsed)
            {
                result[pair.Key] = pair.Value ?? new List<string>();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginSettingsViewModel.DisableWarning",
                $"[PluginConfig] Failed to read disable-warning confirmations: {ex.Message}");
        }

        return result;
    }
}