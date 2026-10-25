/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using Microsoft.Win32;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.AuthTicket;

public sealed class GameRegistrySnapshot
{
    private const string CnRegistryKey = @"HKEY_CURRENT_USER\Software\miHoYo\原神";
    private const string CnRegistryValue = "MIHOYOSDK_ADL_PROD_CN_h3123967166";

    private const string OsRegistryKey = @"HKEY_CURRENT_USER\Software\miHoYo\Genshin Impact";
    private const string OsRegistryValue = "MIHOYOSDK_ADL_PROD_OVERSEA_h1158948810";

    private byte[]? _snapshot;
    private string? _keyName;
    private string? _valueName;

    public void TakeSnapshot(bool isOversea)
    {
        _keyName = isOversea ? OsRegistryKey : CnRegistryKey;
        _valueName = isOversea ? OsRegistryValue : CnRegistryValue;

        try
        {
            _snapshot = Registry.GetValue(_keyName, _valueName, null) as byte[];
            DebugLog.Info("GameRegistrySnapshot",
                $"[RegistrySnapshot] Saved snapshot: {_keyName}\\{_valueName} (length: {_snapshot?.Length ?? 0})");
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameRegistrySnapshot", $"[RegistrySnapshot] Failed to save snapshot: {ex.Message}");
            _snapshot = null;
        }
    }

    public void RestoreSnapshot()
    {
        if (_keyName == null || _valueName == null)
        {
            DebugLog.Info("GameRegistrySnapshot",
                "[RegistrySnapshot] No snapshot to restore (TakeSnapshot not called)");
            return;
        }

        if (_snapshot == null || _snapshot.Length == 0)
        {
            DebugLog.Info("GameRegistrySnapshot", "[RegistrySnapshot] Snapshot empty, skipping restore");
            return;
        }

        try
        {
            Registry.SetValue(_keyName, _valueName, _snapshot, RegistryValueKind.Binary);
            DebugLog.Info("GameRegistrySnapshot", $"[RegistrySnapshot] Restored snapshot: {_keyName}\\{_valueName}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameRegistrySnapshot", $"[RegistrySnapshot] Failed to restore snapshot: {ex.Message}");
        }
        finally
        {
            _snapshot = null;
            _keyName = null;
            _valueName = null;
        }
    }

    public bool HasSnapshot => _snapshot is { Length: > 0 };
}