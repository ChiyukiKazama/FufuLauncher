/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Services;

internal static class ElevatedInjectionResultFile
{
    private static string ResultPath =>
        Path.Combine(Path.GetTempPath(), "FufuLauncher.injection.failure.txt");

    public static void Clear()
    {
        try
        {
            if (File.Exists(ResultPath))
            {
                File.Delete(ResultPath);
            }
        }
        catch
        {
        }
    }

    public static void Write(string message)
    {
        try
        {
            File.WriteAllText(ResultPath, message);
        }
        catch
        {
        }
    }

    public static string? Consume()
    {
        try
        {
            if (!File.Exists(ResultPath))
            {
                return null;
            }

            var message = File.ReadAllText(ResultPath).Trim();
            File.Delete(ResultPath);
            return string.IsNullOrWhiteSpace(message) ? null : message;
        }
        catch
        {
            return null;
        }
    }
}