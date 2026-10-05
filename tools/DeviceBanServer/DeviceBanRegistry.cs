using System.Text.Json;
using FufuLauncher.Models;

namespace DeviceBanServer;

public sealed class DeviceBanRegistry
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;
    private State _state;

    public DeviceBanRegistry(string path, Func<DateTimeOffset>? clock = null)
    {
        _path = Path.GetFullPath(path);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _state = File.Exists(_path)
            ? JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) ?? throw new InvalidDataException("Empty device registry")
            : new State();
    }

    public DeviceBanStatus Observe(DeviceObservation report)
    {
        if (!DeviceBanIdentifiers.IsMotherboardId(report.MotherboardId) || report.Uids == null ||
            report.Uids.Length > 128 || report.Uids.Any(uid => !DeviceBanIdentifiers.IsUid(uid)))
            throw new ArgumentException("Invalid motherboard ID or UID list");

        lock (_gate)
        {
            var state = CloneAndPrune();
            if (!state.Devices.TryGetValue(report.MotherboardId, out var device))
                state.Devices[report.MotherboardId] = device = new StoredDevice();
            var now = _clock();
            foreach (var uid in report.Uids.Distinct(StringComparer.Ordinal))
            {
                if (!device.Uids.TryGetValue(uid, out var observation))
                    device.Uids[uid] = observation = new Observation { FirstReportedAt = now };
                observation.LastReportedAt = now;
            }
            Commit(state);
            return Status(device);
        }
    }

    public IReadOnlyList<ObservedDevice> FindByUid(string uid)
    {
        if (!DeviceBanIdentifiers.IsUid(uid)) throw new ArgumentException("UID must contain 9 or 10 digits");
        lock (_gate)
        {
            var state = CloneAndPrune();
            Commit(state);
            return state.Devices.Where(pair => pair.Value.Uids.ContainsKey(uid)).Select(pair =>
            {
                var observation = pair.Value.Uids[uid];
                var status = Status(pair.Value);
                return new ObservedDevice(pair.Key, observation.FirstReportedAt, observation.LastReportedAt,
                    status.Banned, status.Reason, status.ExpiresAt);
            }).OrderByDescending(device => device.LastReportedAt).ToArray();
        }
    }

    public void SetBans(DeviceBanMutation mutation)
    {
        if (mutation.MotherboardIds is not { Length: > 0 and <= 128 } ||
            mutation.MotherboardIds.Any(id => !DeviceBanIdentifiers.IsMotherboardId(id)) ||
            mutation.Banned && (string.IsNullOrWhiteSpace(mutation.Reason) || mutation.Reason.Length > 256 ||
                mutation.DurationDays is < 1 or > 90))
            throw new ArgumentException("Select known devices, provide a reason, and use a duration of 1–90 days");

        lock (_gate)
        {
            var state = CloneAndPrune();
            if (mutation.MotherboardIds.Any(id => !state.Devices.ContainsKey(id)))
                throw new ArgumentException("A selected device no longer exists; refresh the results");
            foreach (var id in mutation.MotherboardIds.Distinct(StringComparer.Ordinal))
                state.Devices[id].Ban = mutation.Banned
                    ? new Ban { Reason = mutation.Reason.Trim(), ExpiresAt = _clock().AddDays(mutation.DurationDays) }
                    : null;
            Commit(state);
        }
    }

    public void Delete(string motherboardId)
    {
        if (!DeviceBanIdentifiers.IsMotherboardId(motherboardId)) throw new ArgumentException("Invalid motherboard ID");
        lock (_gate)
        {
            var state = CloneAndPrune();
            state.Devices.Remove(motherboardId);
            Commit(state);
        }
    }

    public void Prune()
    {
        lock (_gate) Commit(CloneAndPrune());
    }

    private State CloneAndPrune()
    {
        var state = JsonSerializer.Deserialize<State>(JsonSerializer.Serialize(_state))!;
        var now = _clock();
        foreach (var pair in state.Devices.ToArray())
        {
            var device = pair.Value;
            if (device.Ban?.ExpiresAt <= now) device.Ban = null;
            foreach (var uid in device.Uids.Where(uid => uid.Value.LastReportedAt < now.AddDays(-90)).Select(uid => uid.Key).ToArray())
                device.Uids.Remove(uid);
            if (device.Uids.Count == 0 && device.Ban == null) state.Devices.Remove(pair.Key);
        }
        return state;
    }

    private static DeviceBanStatus Status(StoredDevice device) => device.Ban is { } ban
        ? new(true, ban.Reason, ban.ExpiresAt) : new(false, string.Empty, null);

    private void Commit(State state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state));
            File.Move(temporaryPath, _path, overwrite: true);
            _state = state;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public sealed class State
    {
        public Dictionary<string, StoredDevice> Devices { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed class StoredDevice
    {
        public Dictionary<string, Observation> Uids { get; set; } = new(StringComparer.Ordinal);
        public Ban? Ban { get; set; }
    }

    public sealed class Observation
    {
        public DateTimeOffset FirstReportedAt { get; set; }
        public DateTimeOffset LastReportedAt { get; set; }
    }

    public sealed class Ban
    {
        public string Reason { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
