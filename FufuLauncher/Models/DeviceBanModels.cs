namespace FufuLauncher.Models;

public sealed class DeviceBanOptions
{
    public string ServiceUrl { get; set; } = string.Empty;
}

public sealed record DeviceObservation(string MotherboardId, string[] Uids);
public sealed record DeviceBanStatus(bool Banned, string Reason, DateTimeOffset? ExpiresAt);
public sealed record ObservedDevice(string MotherboardId, DateTimeOffset FirstReportedAt,
    DateTimeOffset LastReportedAt, bool Banned, string Reason, DateTimeOffset? ExpiresAt);
public sealed record DeviceBanMutation(string[] MotherboardIds, bool Banned, string Reason, int DurationDays = 30);

public enum DeviceBanCheckState
{
    Disabled, Allowed, Banned, ConsentRequired, UnknownMotherboard, Unavailable
}

public sealed record DeviceBanCheckResult(bool CanInject, DeviceBanCheckState State, string Reason = "");

public static class DeviceBanIdentifiers
{
    public static bool IsUid(string? value) => value is { Length: 9 or 10 } &&
        value.All(c => c is >= '0' and <= '9');

    public static bool IsMotherboardId(string? value) => value is { Length: 68 } &&
        value.StartsWith("MB1-", StringComparison.Ordinal) &&
        value.Skip(4).All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
