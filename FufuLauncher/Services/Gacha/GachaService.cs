/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.MiHoYo;

namespace FufuLauncher.Services;

public class GachaService
{
    private const string Lk2Salt = "sidQFEglajEz7FA0Aj7HQPV88zpf17SO";
    private const string AppVersion = "2.95.1";
    private readonly HttpClient _httpClient;

    public static readonly Dictionary<string, string> GachaTypes = new()
    {
        { "301", "角色活动祈愿" },
        { "302", "武器活动祈愿" },
        { "200", "常驻祈愿" },
        { "100", "新手祈愿" },
        { "400", "角色活动祈愿" },
        { "500", "集录祈愿" }
    };

    public GachaService() : this(new HttpClient(new HttpClientHandler { UseCookies = false }))
    {
    }

    public GachaService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<GameRolesResponse> GetBoundRolesAsync(string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoints.MihoyoBbsUserGameRolesUrl);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("DS", CalculateDs(HeaderSalts.UserInfoServiceLegacySalt,
            Random.Shared.Next(100000, 200000).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", Guid.NewGuid().ToString("N"));
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation("Referer", ApiEndpoints.ActMihoyoBase + "/");
        request.Headers.TryAddWithoutValidation("Origin", ApiEndpoints.ActMihoyoBase);
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Linux; Android 12; Unspecified Device) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/103.0.5060.129 Mobile Safari/537.36 miHoYoBBS/2.93.1");
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<GameRolesResponse>(await response.Content.ReadAsStringAsync())
               ?? throw new JsonException("Invalid role-binding response.");
    }

    public Task<GachaLink> GenerateAuthKeyAsync(IReadOnlyDictionary<string, string> cookies, GameRoleInfo role)
    {
        string Read(params string[] names) => names
            .Select(name => cookies.TryGetValue(name, out var value) ? value : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

        var stoken = Read("stoken_v2", "stoken");
        var mid = Read("mid", "account_mid_v2", "ltmid_v2");
        var stuid = Read("stuid", "stuid_v2", "account_id", "account_id_v2", "ltuid", "ltuid_v2");
        if (string.IsNullOrEmpty(stoken) || string.IsNullOrEmpty(mid) || string.IsNullOrEmpty(stuid))
            throw new GachaAuthKeyException("米游社抽卡登录凭证不完整。请在账号页使用「米游社 APP 扫码」或短信验证码重新登录绑定了目标角色的账号。",
                requiresReLogin: true);
        return GenerateAuthKeyAsync(stoken, mid, stuid, role);
    }

    public async Task<GachaLink> GenerateAuthKeyAsync(string stoken, string mid, string stuid, GameRoleInfo role)
    {
        if (!GachaAccountResolver.IsSupportedRole(role))
            throw new ArgumentException("所选角色的游戏、UID 或服务器信息无效。", nameof(role));
        if (string.IsNullOrWhiteSpace(stoken) || string.IsNullOrWhiteSpace(mid) || string.IsNullOrWhiteSpace(stuid))
            throw new ArgumentException("米游社登录凭证不完整，请重新登录所选账号。");

        var body = JsonSerializer.Serialize(new
        {
            auth_appid = "webview_gacha",
            game_biz = role.game_biz,
            game_uid = long.Parse(role.game_uid, System.Globalization.CultureInfo.InvariantCulture),
            region = role.region
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.GenAuthKeyUrl);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Cookie", $"stuid={stuid};stoken={stoken};mid={mid};");
        request.Headers.TryAddWithoutValidation("DS", CalculateLk2Ds());
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", Guid.NewGuid().ToString("N"));
        request.Headers.TryAddWithoutValidation("Referer", ApiEndpoints.AppMihoyoBase);
        request.Headers.TryAddWithoutValidation("User-Agent",
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/{AppVersion}");

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new GachaAuthKeyException($"米游社认证请求失败（HTTP {(int)response.StatusCode}）。");
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("retcode", out var rc) ||
                rc.ValueKind != JsonValueKind.Number || !rc.TryGetInt32(out var retcode))
                throw new GachaAuthKeyException("米游社认证接口返回了无效数据。");
            if (retcode == -100)
                throw new GachaAuthKeyException("米游社未接受所选角色的抽卡认证（返回码 -100）。", retcode);
            if (retcode != 0)
                throw new GachaAuthKeyException($"米游社认证密钥生成失败（返回码 {retcode}）。", retcode);
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("authkey", out var key) || key.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(key.GetString()) ||
                !data.TryGetProperty("authkey_ver", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var authkeyVersion) || authkeyVersion <= 0 ||
                !data.TryGetProperty("sign_type", out var sign) || sign.ValueKind != JsonValueKind.Number ||
                !sign.TryGetInt32(out var signType) || signType <= 0)
                throw new GachaAuthKeyException("米游社认证接口未返回完整的祈愿认证信息。");

            var url = ApiEndpoints.GachaLogCnUrl +
                      $"?authkey={Uri.EscapeDataString(key.GetString()!)}&authkey_ver={authkeyVersion}" +
                      $"&sign_type={signType}&region={role.region}&lang=zh-cn";
            return GachaUrlHelper.Parse(url) ?? throw new GachaAuthKeyException("米游社返回的祈愿认证信息无效。");
        }
        catch (JsonException)
        {
            throw new GachaAuthKeyException("米游社认证接口返回了无效数据。");
        }
    }

    private static string CalculateLk2Ds()
    {
        var chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var r = new string(Enumerable.Range(0, 6).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
        return CalculateDs(Lk2Salt, r);
    }

    private static string CalculateDs(string salt, string r)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var check = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"salt={salt}&t={t}&r={r}")))
            .ToLowerInvariant();
        return $"{t},{r},{check}";
    }

    public string? ExtractBaseUrl(string fullUrl) => GachaUrlHelper.Parse(fullUrl)?.ApiUrl;

    public async Task<List<GachaLogItem>> FetchGachaLogAsync(string baseUrl, string gachaType,
        Action<int>? onPageFetched = null, long knownEndId = 0, bool requireComplete = false,
        string? expectedRegion = null, string? expectedUid = null)
    {
        var allItems = new List<GachaLogItem>();
        string? observedUid = null;
        string endId = "0";
        int page = 1;
        bool reachedKnown = false;
        const int maxRetry = 3;
        int[] retryDelays = { 2000, 4000, 6000 };

        var uri = new Uri(baseUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        var authParams = new[] { "authkey", "authkey_ver", "sign_type" };
        var cleanQuery = System.Web.HttpUtility.ParseQueryString(string.Empty);
        foreach (var key in query.AllKeys)
        {
            if (authParams.Contains(key) || key == "region" || key == "lang")
                cleanQuery[key] = query[key];
        }

        while (true)
        {
            cleanQuery["gacha_type"] = gachaType;
            cleanQuery["page"] = page.ToString();
            cleanQuery["size"] = "20";
            cleanQuery["end_id"] = endId;

            var requestUrl = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}?{cleanQuery}";

            bool gotData = false;
            bool reachedEnd = false;

            for (int retry = 0; retry < maxRetry; retry++)
            {
                try
                {
                    var json = await _httpClient.GetStringAsync(requestUrl);
                    var response = JsonSerializer.Deserialize<GachaLogResponse>(json);

                    if (response?.Retcode != 0)
                    {
                        if (requireComplete && response?.Retcode == -100)
                            throw new GachaFetchException("祈愿链接已过期或无效，请在游戏中重新打开祈愿历史记录后再试。", -100);
                        DebugLog.Info("GachaService",
                            $"[Gacha] type={gachaType} page={page} retry {retry + 1}/{maxRetry}, retcode={response?.Retcode}, message={response?.Message}");
                        if (retry < maxRetry - 1)
                        {
                            await Task.Delay(retryDelays[retry]);
                            continue;
                        }

                        if (requireComplete)
                            throw new GachaFetchException($"祈愿记录获取失败（返回码 {response?.Retcode}），请稍后重试。",
                                response?.Retcode);
                        DebugLog.Info("GachaService",
                            $"[Gacha] type={gachaType} page={page} retries exhausted, skipping");
                        break;
                    }

                    if (requireComplete)
                        ValidateIdentity(response!.Data, expectedRegion, expectedUid, ref observedUid);

                    if (response?.Data?.List == null || response.Data.List.Count == 0)
                    {
                        DebugLog.Info("GachaService",
                            $"[Gacha] type={gachaType} page={page} returned empty list, ending normally");
                        reachedEnd = true;
                        break;
                    }

                    DebugLog.Info("GachaService",
                        $"[Gacha] type={gachaType} page={page} fetched {response.Data.List.Count} records, end_id={response.Data.List.Last().Id}{(knownEndId > 0 ? $", baseline={knownEndId}" : "")}");

                    if (knownEndId > 0)
                    {
                        bool reachedBoundary = false;
                        foreach (var item in response.Data.List)
                        {
                            if (long.TryParse(item.Id, out var itemId) && itemId <= knownEndId)
                            {
                                reachedBoundary = true;
                                break;
                            }

                            allItems.Add(item);
                        }

                        endId = response.Data.List.Last().Id;
                        onPageFetched?.Invoke(allItems.Count);
                        await Task.Delay(500);

                        if (reachedBoundary)
                        {
                            reachedKnown = true;
                            DebugLog.Info("GachaService",
                                $"[Gacha] type={gachaType} incremental update reached known record boundary, ending early");
                        }
                        else
                        {
                            page++;
                        }

                        gotData = true;
                    }
                    else
                    {
                        allItems.AddRange(response.Data.List);
                        endId = response.Data.List.Last().Id;
                        page++;
                        onPageFetched?.Invoke(allItems.Count);
                        await Task.Delay(500);
                        gotData = true;
                    }

                    break;
                }
                catch (GachaFetchException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    DebugLog.Info("GachaService",
                        $"[Gacha] type={gachaType} page={page} retry {retry + 1}/{maxRetry}, exception: {ex.GetType().Name}: {ex.Message}");
                    if (retry < maxRetry - 1)
                    {
                        await Task.Delay(retryDelays[retry]);
                        continue;
                    }

                    if (requireComplete)
                        throw new GachaFetchException("祈愿记录请求失败，请检查网络后重试。", null);
                    DebugLog.Info("GachaService", $"[Gacha] type={gachaType} page={page} retries exhausted, skipping");
                }
            }

            if (reachedEnd || !gotData || reachedKnown) break;
        }

        allItems.Reverse();
        return allItems;
    }

    private static void ValidateIdentity(GachaLogData? data, string? expectedRegion, string? expectedUid,
        ref string? observedUid)
    {
        if (data?.List == null)
            throw new GachaFetchException("服务器返回了无效的祈愿记录数据。", null);
        if (!string.IsNullOrEmpty(expectedRegion) && !string.IsNullOrEmpty(data.Region) &&
            data.Region != expectedRegion)
            throw new GachaFetchException("祈愿链接与返回记录的服务器不一致，已停止导入。", null);

        var region = string.IsNullOrEmpty(expectedRegion) ? data.Region : expectedRegion;
        foreach (var item in data.List)
        {
            var uid = item.Uid;
            if (string.IsNullOrEmpty(uid) || uid.Length is < 9 or > 10 || !uid.All(char.IsAsciiDigit))
                throw new GachaFetchException("祈愿记录缺少有效的游戏 UID，已停止导入。", null);
            if (observedUid != null && uid != observedUid)
                throw new GachaFetchException("祈愿记录包含不同账号的数据，已停止导入。", null);
            if (!string.IsNullOrEmpty(expectedUid) && uid != expectedUid)
                throw new GachaFetchException("返回记录的 UID 与所选游戏角色不一致，已停止导入。", null);
            if (!string.IsNullOrEmpty(region) && ServerRegion.Resolve(uid) != region)
                throw new GachaFetchException("祈愿记录的 UID 与服务器不一致，已停止导入。", null);
            observedUid = uid;
        }
    }

    public static string GetPoolName(string gachaTypeId)
    {
        return gachaTypeId switch
        {
            "301" => "Gacha_Pool_Character_FullName".GetLocalized(),
            "302" => "Gacha_Pool_Weapon_FullName".GetLocalized(),
            "200" => "Gacha_Pool_Standard_FullName".GetLocalized(),
            "100" => "Gacha_Pool_Novice_FullName".GetLocalized(),
            "400" => "Gacha_Pool_Character2_FullName".GetLocalized(),
            "500" => "Gacha_Pool_Chronicled_FullName".GetLocalized(),
            _ => GachaTypes.TryGetValue(gachaTypeId, out var name) ? name : "Gacha_Pool_Unknown".GetLocalized()
        };
    }

    public GachaStatistic AnalyzePool(string gachaTypeId, List<GachaLogItem> items)
    {
        var stat = new GachaStatistic
        {
            PoolName = GetPoolName(gachaTypeId),
            TotalCount = items.Count,
            CurrentPity = 0,
            CurrentPity4 = 0
        };

        int pityCounter5 = 0;
        int pityCounter4 = 0;

        foreach (var item in items)
        {
            pityCounter5++;
            pityCounter4++;

            if (item.RankType == "5")
            {
                stat.FiveStarRecords.Add(new FiveStarRecord
                {
                    Name = item.Name,
                    ItemId = item.ItemId,
                    PityUsed = pityCounter5,
                    Time = item.Time,
                    Rank = 5
                });
                stat.FiveStarCount++;
                pityCounter5 = 0;
            }
            else if (item.RankType == "4")
            {
                stat.FourStarRecords.Add(new FiveStarRecord
                {
                    Name = item.Name,
                    ItemId = item.ItemId,
                    PityUsed = pityCounter4,
                    Time = item.Time,
                    Rank = 4
                });
                stat.FourStarCount++;
                pityCounter4 = 0;
            }
        }

        stat.CurrentPity = pityCounter5;
        stat.CurrentPity4 = pityCounter4;

        stat.FiveStarRecords.Reverse();
        stat.FourStarRecords.Reverse();

        return stat;
    }
}

public sealed class GachaFetchException(string message, int? returnCode) : Exception(message)
{
    public int? ReturnCode
    {
        get;
    } = returnCode;
}