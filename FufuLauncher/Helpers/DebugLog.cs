/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FufuLauncher.Helpers;

internal static class DebugLog
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly Regex FormatItem = new(@"\{\d+(?:[,:][^}]*)?\}", RegexOptions.Compiled);

    [Conditional("DEBUG")]
    public static void Info(string module, object? message, params object?[] args)
        => Debug.WriteLine(Format(module, "INFO", message, args));

    [Conditional("DEBUG")]
    public static void Warn(string module, object? message, params object?[] args)
        => Debug.WriteLine(Format(module, "WARN", message, args));

    [Conditional("DEBUG")]
    public static void Error(string module, object? message, Exception? exception = null)
    {
        var text = message?.ToString() ?? string.Empty;
        if (exception != null)
            text += Environment.NewLine + exception;
        Debug.WriteLine(Format(module, "ERROR", text, Array.Empty<object?>()));
    }

    private static string Format(string module, string level, object? message, object?[] args)
    {
        var text = message?.ToString() ?? string.Empty;
        if (args.Length > 0)
        {
            if (FormatItem.IsMatch(text))
            {
                try
                {
                    text = string.Format(text, args);
                }
                catch (FormatException)
                {
                    text = text + " " + string.Join(" ", args.Select(static value => value?.ToString()));
                }
            }
            else
            {
                var detail = string.Join(" ", args.Select(static value => value?.ToString()));
                if (!string.IsNullOrWhiteSpace(detail))
                    text = text + " " + detail;
            }
        }

        return $"[{module}][{Clock.ElapsedMilliseconds,7}ms][T{Environment.CurrentManagedThreadId}][{level}] {text}";
    }
}