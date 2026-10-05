using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services.UID;
using Microsoft.Extensions.Options;

namespace FufuLauncher.Services;

public sealed class MotherboardBanService
{
    private const string ConsentKey = "DeviceBanConsentEndpointV1";
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 32 * 1024
    };

    private readonly ILocalSettingsService _settings;
    private readonly HttpClient _http;
    private readonly Func<string> _getMotherboardId;
    private readonly Func<IReadOnlyList<string>> _getUids;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Uri? _serviceUri;
    private Task<string>? _identityTask;

    public bool IsConfigured { get; }
    public string ServiceAddress => _serviceUri?.GetLeftPart(UriPartial.Path) ?? string.Empty;

    public MotherboardBanService(ILocalSettingsService settings, IOptions<DeviceBanOptions> options)
        : this(settings, options.Value, Http, SystemEnvironmentHelper.GetMotherboardId, UidLookupService.ReadLocalUids) { }

    internal MotherboardBanService(ILocalSettingsService settings, DeviceBanOptions options, HttpClient http,
        Func<string> getMotherboardId, Func<IReadOnlyList<string>> getUids)
    {
        _settings = settings;
        _http = http;
        _getMotherboardId = getMotherboardId;
        _getUids = getUids;
        IsConfigured = !string.IsNullOrWhiteSpace(options.ServiceUrl);
        if (Uri.TryCreate(options.ServiceUrl?.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
            _serviceUri = uri;
    }

    public async Task<bool> NeedsConsentAsync() => _serviceUri != null &&
        !string.Equals(await _settings.ReadSettingAsync(ConsentKey) as string, ServiceAddress, StringComparison.Ordinal);

    public Task GiveConsentAsync() => _settings.SaveSettingAsync(ConsentKey, ServiceAddress);

    public async Task<DeviceBanCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return new(true, DeviceBanCheckState.Disabled);
        if (_serviceUri == null) return new(false, DeviceBanCheckState.Unavailable);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (await NeedsConsentAsync()) return new(false, DeviceBanCheckState.ConsentRequired);
            _identityTask ??= Task.Run(_getMotherboardId);
            var motherboardId = await _identityTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!DeviceBanIdentifiers.IsMotherboardId(motherboardId))
                return new(false, DeviceBanCheckState.UnknownMotherboard);

            var uids = (await Task.Run(_getUids, cancellationToken)).Where(DeviceBanIdentifiers.IsUid)
                .Distinct(StringComparer.Ordinal).Take(128).ToArray();
            using var response = await _http.PostAsJsonAsync(new Uri(_serviceUri, "api/device/check"),
                new DeviceObservation(motherboardId, uids), cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!json.RootElement.TryGetProperty("banned", out var banned) ||
                banned.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new(false, DeviceBanCheckState.Unavailable);

            var reason = json.RootElement.TryGetProperty("reason", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString() ?? string.Empty : string.Empty;
            if (reason.Length > 256) reason = reason[..256];
            return banned.GetBoolean() ? new(false, DeviceBanCheckState.Banned, reason)
                : new(true, DeviceBanCheckState.Allowed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[设备限制] 检查未完成: {ex.GetType().Name}");
            return new(false, DeviceBanCheckState.Unavailable);
        }
        finally { _gate.Release(); }
    }
}
