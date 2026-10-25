/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;

namespace FufuLauncher.Models.CycleChallenge;

public readonly record struct CycleChallengeTimeContext(TimeSpan SourceOffset, TimeSpan DisplayOffset)
{
    public static CycleChallengeTimeContext Default
    {
        get;
    } = new(CycleChallengeTime.Utc8, CycleChallengeTime.Utc8);
}

public static class CycleChallengeTime
{
    public static readonly TimeSpan Utc8 = TimeSpan.FromHours(8);


    public static TimeSpan GetDisplayOffset(string? region) => GetServerOffset(region);


    public static TimeSpan GetServerOffset(string? region) => region?.ToLowerInvariant() switch
    {
        "os_usa" => TimeSpan.FromHours(-5),
        "os_euro" => TimeSpan.FromHours(1),

        "os_asia" => Utc8,
        "os_cht" => Utc8,
        _ => Utc8,
    };


    public static CycleChallengeTimeContext CreateContext(string? region) =>
        new(GetServerOffset(region), GetDisplayOffset(region));


    public static DateTimeOffset ToDisplay(DateTimeOffset value, CycleChallengeTimeContext context) =>
        value.ToOffset(context.DisplayOffset);


    public static string FormatStoredString(string? raw, string format, TimeSpan displayOffset)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();
        if (HasExplicitOffset(text))
        {
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                out var parsed)
                ? parsed.ToOffset(displayOffset).ToString(format, CultureInfo.InvariantCulture)
                : text;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall)
            ? wall.ToString(format, CultureInfo.InvariantCulture)
            : text;
    }


    public static string ToUtcStorageString(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);

    private static bool HasExplicitOffset(string text)
    {
        if (text.EndsWith('Z') || text.EndsWith('z'))
        {
            return true;
        }


        var timeStart = text.IndexOfAny([' ', 'T']);
        if (timeStart < 0)
        {
            return false;
        }

        return text.AsSpan(timeStart + 1).IndexOfAny('+', '-') >= 0;
    }


    public static string Format(DateTimeOffset value, string format, CycleChallengeTimeContext context) =>
        ToDisplay(value, context).ToString(format, CultureInfo.InvariantCulture);


    public static string FormatUnix(long unixSeconds, string format, CycleChallengeTimeContext context) =>
        Format(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), format, context);


    public static string FormatWallClock(DateTime value, string format, CycleChallengeTimeContext context)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, context.SourceOffset)
            .ToOffset(context.DisplayOffset)
            .ToString(format, CultureInfo.InvariantCulture);
    }


    public static DateTimeOffset WallClockToDisplay(DateTime value, CycleChallengeTimeContext context)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, context.SourceOffset).ToOffset(context.DisplayOffset);
    }
}