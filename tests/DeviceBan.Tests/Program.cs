using System.Net;
using System.Text.Json;
using DeviceBanServer;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    checks++;
}
void Reject(Action action, string name)
{
    try { action(); } catch (ArgumentException) { Check(true, name); return; }
    throw new Exception("FAIL: " + name);
}

var directory = Path.Combine(Path.GetTempPath(), "FufuLauncher-device-ban-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var boardA = MotherboardIdentity.FromSerialNumbers(new[] { "BOARD-A-987654" });
    var boardB = MotherboardIdentity.FromSerialNumbers(new[] { "BOARD-B-987654" });
    var boardC = MotherboardIdentity.FromSerialNumbers(new[] { "BOARD-C-987654" });
    Check(DeviceBanIdentifiers.IsMotherboardId(boardA), "Versioned SHA-256 motherboard identity");
    Check(boardA == MotherboardIdentity.FromSerialNumbers(new[] { " board-a-987654 ", "BOARD-A-987654" }), "Serial casing, trim and duplicate normalization");
    Check(MotherboardIdentity.FromSerialNumbers(new[] { "B", "A" }) == MotherboardIdentity.FromSerialNumbers(new[] { "A", "B" }), "Multiple baseboard ordering is stable");
    Check(MotherboardIdentity.FromSerialNumbers(new[] { "", "Default string", "To Be Filled By O.E.M.", "000000", "FFFFFFFF" }) == "", "Placeholder and missing serials never share a ban identity");
    Check(!DeviceBanIdentifiers.IsMotherboardId("MB1-" + new string('G', 64)), "Invalid hash rejected");
    Check(DeviceBanIdentifiers.IsUid("123456789") && !DeviceBanIdentifiers.IsUid("１２３４５６７８９"), "UIDs use exact ASCII digits");

    var now = DateTimeOffset.Parse("2026-10-05T00:00:00Z");
    var path = Path.Combine(directory, "devices.json");
    var registry = new DeviceBanRegistry(path, () => now);
    registry.Observe(new(boardA, new[] { "123456789", "987654321" }));
    registry.Observe(new(boardB, new[] { "123456789" }));
    registry.Observe(new(boardC, new[] { "1234567890" }));
    Check(registry.FindByUid("123456789").Count == 2, "UID lookup returns every observed board");
    Check(registry.FindByUid("1234567890").Single().MotherboardId == boardC, "UID lookup cannot substring-match another account");
    registry.SetBans(new(new[] { boardA }, true, "Manual review", 30));
    Check(registry.FindByUid("123456789").Count(device => device.Banned) == 1, "Manual selection restricts only the selected motherboard");
    Check(registry.Observe(new(boardA, new[] { "111111111" })).Banned, "Switching UID cannot remove an existing motherboard ban");
    Check(registry.Observe(new(boardA, Array.Empty<string>())).Banned, "Empty UID history still checks a known board ban");
    Check(!registry.Observe(new(boardB, new[] { "123456789" })).Banned, "Other boards stay allowed for the same UID");
    Reject(() => registry.SetBans(new(new[] { boardB, "MB1-" + new string('1', 64) }, true, "Test", 30)), "Unknown selection rejects the whole mutation");
    Check(!registry.Observe(new(boardB, Array.Empty<string>())).Banned, "Rejected mutation does not partially ban a board");
    Reject(() => registry.Observe(new(boardA, new[] { "bad" })), "Invalid UID input rejected");
    Reject(() => registry.SetBans(new(new[] { boardA }, true, "", 30)), "Ban reason is required");
    var restarted = new DeviceBanRegistry(path, () => now);
    Check(restarted.Observe(new(boardA, Array.Empty<string>())).Banned, "Ban survives a server restart");
    Check(!File.ReadAllText(path).Contains("BOARD-A-987654"), "Raw motherboard serial is never stored");
    restarted.SetBans(new(new[] { boardA }, false, ""));
    Check(!restarted.Observe(new(boardA, Array.Empty<string>())).Banned, "Manual unban takes effect");
    restarted.SetBans(new(new[] { boardA }, true, "Expires", 1));
    now = now.AddDays(2);
    Check(!restarted.Observe(new(boardA, Array.Empty<string>())).Banned, "Expired ban automatically releases the board");
    now = now.AddDays(91);
    Check(restarted.FindByUid("123456789").Count == 0, "Inactive UID associations expire after 90 days");
    now = now.AddDays(-91);
    restarted.Observe(new(boardA, new[] { "123456789" }));
    restarted.Delete(boardA);
    Check(restarted.FindByUid("123456789").Count == 0, "Admin deletion removes both association and restriction");

    var settings = new FakeSettings();
    var requests = 0;
    var handler = new FakeHandler(async request =>
    {
        requests++;
        Check(request.RequestUri == new Uri("https://device.example.test/api/device/check"), "Client posts to the configured endpoint");
        var report = JsonSerializer.Deserialize<DeviceObservation>(await request.Content!.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Check(report.MotherboardId == boardA && report.Uids.SequenceEqual(new[] { "123456789" }), "Client sends only hash and deduplicated valid UIDs");
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(restarted.Observe(report), new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
    });
    using var http = new HttpClient(handler);
    var service = new MotherboardBanService(settings, new() { ServiceUrl = "https://device.example.test" }, http,
        () => boardA, () => new[] { "123456789", "123456789", "bad" });
    Check((await service.CheckAsync()).State == DeviceBanCheckState.ConsentRequired && requests == 0, "No device upload before explicit consent");
    await service.GiveConsentAsync();
    Check((await service.CheckAsync()).CanInject, "Allowed client and registry integration");
    restarted.SetBans(new(new[] { boardA }, true, "Selected in admin", 30));
    Check((await service.CheckAsync()) is { CanInject: false, State: DeviceBanCheckState.Banned, Reason: "Selected in admin" }, "Selected board denies injection with its reason");
    restarted.SetBans(new(new[] { boardA }, false, ""));
    Check((await service.CheckAsync()).CanInject, "Unban permits injection on the next check");
    var changedEndpoint = new MotherboardBanService(settings, new() { ServiceUrl = "https://different.example.test" }, http, () => boardA, () => Array.Empty<string>());
    Check(await changedEndpoint.NeedsConsentAsync(), "Changing the data recipient requires new consent");
    var disabled = new MotherboardBanService(settings, new(), http, () => throw new Exception("Identity must not be read"), () => Array.Empty<string>());
    Check((await disabled.CheckAsync()) is { CanInject: true, State: DeviceBanCheckState.Disabled }, "Empty configuration leaves injection behavior unchanged");
    var invalidUrl = new MotherboardBanService(settings, new() { ServiceUrl = "http://insecure.example.test" }, http, () => boardA, () => Array.Empty<string>());
    Check((await invalidUrl.CheckAsync()).State == DeviceBanCheckState.Unavailable, "Remote HTTP is refused");
    var noIdentity = new MotherboardBanService(settings, new() { ServiceUrl = "https://device.example.test" }, http, () => "", () => Array.Empty<string>());
    var countBefore = requests;
    Check((await noIdentity.CheckAsync()).State == DeviceBanCheckState.UnknownMotherboard && requests == countBefore, "Missing motherboard stops injection without uploading a shared placeholder");
    foreach (var response in new[] { "{}", "{\"banned\":\"false\"}", "not json" })
    {
        using var malformedHttp = new HttpClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) })));
        var malformed = new MotherboardBanService(settings, new() { ServiceUrl = "https://device.example.test" }, malformedHttp, () => boardA, () => Array.Empty<string>());
        Check((await malformed.CheckAsync()).State == DeviceBanCheckState.Unavailable, "Malformed status cannot permit injection: " + response);
    }
    using var offlineHttp = new HttpClient(new FakeHandler(_ => throw new HttpRequestException("Offline")));
    var offline = new MotherboardBanService(settings, new() { ServiceUrl = "https://device.example.test" }, offlineHttp, () => boardA, () => Array.Empty<string>());
    Check(!(await offline.CheckAsync()).CanInject, "Network failure disables only the injection decision");
    settings.FailReads = true;
    Check((await service.CheckAsync()).State == DeviceBanCheckState.Unavailable, "Consent storage failure disables injection instead of aborting normal launch");
    settings.FailReads = false;
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await service.CheckAsync(cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(true, "Caller cancellation propagates"); }
    var actualId = SystemEnvironmentHelper.GetMotherboardId();
    Check(actualId == "" || DeviceBanIdentifiers.IsMotherboardId(actualId), "Real WMI reader returns a valid hash or explicitly unavailable");
    Console.WriteLine($"Completed {checks} checks. Real motherboard available: {actualId.Length > 0}");
}
finally
{
    foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
    Directory.Delete(directory);
}

sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
}

sealed class FakeSettings : ILocalSettingsService
{
    private readonly Dictionary<string, object?> _values = new();
    public bool FailReads { get; set; }
    public void StartBackgroundLoad() { }
    public Task<object?> ReadSettingAsync(string key) => FailReads
        ? throw new IOException("Settings unavailable") : Task.FromResult(_values.GetValueOrDefault(key));
    public Task SaveSettingAsync<T>(string key, T value) { _values[key] = value; return Task.CompletedTask; }
    public async Task<bool> TrySaveSettingAsync<T>(string key, T value) { await SaveSettingAsync(key, value); return true; }
    public Task RemoveSettingAsync(string key) { _values.Remove(key); return Task.CompletedTask; }
    public Task<bool> InvalidateAndReloadAsync() => Task.FromResult(true);
}
