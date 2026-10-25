/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.RegularExpressions;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class LuaPluginInstaller
{
    #region Security & Path Validation

    private string SanitizePath(string rawPath, string operation)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new SecurityViolationException(
                string.Format("PluginStoreSecurityEmptyPath".GetLocalized(), operation));
        }

        if (rawPath.Contains(".."))
        {
            DebugLog.Info("LuaPluginInstaller.Security",
                $"[LuaInstaller] SECURITY: Path traversal attempt blocked in {operation}: {rawPath}");
            throw new SecurityViolationException(
                string.Format("PluginStorePathTraversal".GetLocalized()));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch (Exception ex)
        {
            throw new SecurityViolationException(
                string.Format("PluginStoreSecurityInvalidPath".GetLocalized(), operation, ex.Message));
        }

        var pluginsDirFull = Path.GetFullPath(_pluginsDir);

        if (!fullPath.Equals(pluginsDirFull, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(Path.TrimEndingDirectorySeparator(pluginsDirFull) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            DebugLog.Info("LuaPluginInstaller.Security",
                $"[LuaInstaller] SECURITY: Path outside plugins dir blocked in {operation}: {fullPath}");
            throw new SecurityViolationException(
                string.Format("PluginStoreSecurityOutsideDir".GetLocalized(), operation));
        }

        return fullPath;
    }

    private static string SanitizeName(string name, string operation)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SecurityViolationException(
                string.Format("PluginStoreSecurityEmptyPath".GetLocalized(), operation));
        }

        if (name.Contains("..") ||
            name.Contains('/') || name.Contains('\\') ||
            name.Contains(':') || name.Contains('*') ||
            name.Contains('?') || name.Contains('"') ||
            name.Contains('<') || name.Contains('>') || name.Contains('|'))
        {
            DebugLog.Info("LuaPluginInstaller.Security",
                $"[LuaInstaller] SECURITY: Invalid name characters in {operation}: {name}");
            throw new SecurityViolationException(
                string.Format("PluginStorePathTraversal".GetLocalized()));
        }

        return name;
    }

    private static bool WildcardMatch(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return string.IsNullOrEmpty(input);

        if (pattern.Length > 500)
            throw new SecurityViolationException("Wildcard pattern too long.");

        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        try
        {
            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
        }
        catch (RegexMatchTimeoutException)
        {
            DebugLog.Info("LuaPluginInstaller.Security",
                $"[LuaInstaller] Wildcard match timeout for pattern: {pattern}");
            return false;
        }
    }

    #endregion
}