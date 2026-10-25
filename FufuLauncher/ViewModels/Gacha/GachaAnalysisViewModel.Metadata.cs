/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class GachaAnalysisViewModel
{
    #region Item Metadata

    private async Task<string> GetCurrentCookieAsync()
    {
        var activeId = _accountManager.ActiveAccountId;
        if (activeId == null) return null;
        var cookies = await _accountManager.LoadCookiesAsync(activeId);
        if (cookies == null || cookies.Count == 0) return null;
        return string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    public async Task FetchMetadataFromApiAsync()
    {
        IsScraping = true;
        CrawlerStatus = "Gacha_CrawlerStatus_FetchingMetaApi".GetLocalized();

        try
        {
            var cookie = await GetCurrentCookieAsync();

            var results = new List<ScrapedMetadata>();

            var chars = await FetchCalculatorListWithRetryAsync(ApiEndpoints.CalculateAvatarListUrl,
                new { page = 1, size = 1000, is_all = true }, cookie, "char");
            results.AddRange(chars);

            var weapons = await FetchCalculatorListWithRetryAsync(ApiEndpoints.CalculateWeaponListUrl,
                new { page = 1, size = 1000, weapon_levels = new[] { 1, 2, 3, 4, 5 } }, cookie, "weapon");
            results.AddRange(weapons);

            UpdateMetadata(results, deferRefresh: true);

            await FetchGachaPoolMetadataAsync(deferRefresh: true);
        }
        catch (Exception ex)
        {
            DebugLog.Info("GachaAnalysisViewModel.Metadata",
                $"[Gacha] Failed to fetch metadata from API: {ex.Message}");
            UpdateMetadata(null, deferRefresh: true);
        }
        finally
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() => RefreshUIFromCache());
        }
    }

    private async Task<List<ScrapedMetadata>> FetchCalculatorListWithRetryAsync(string url, object payload,
        string? cookie, string type)
    {
        const int maxAttempts = 3;
        int[] delays = { 1000, 2000, 4000 };
        var typeName = type == "char"
            ? "Gacha_ItemType_Character".GetLocalized()
            : "Gacha_ItemType_Weapon".GetLocalized();

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var result = await FetchCalculatorListAsync(url, payload, cookie, type);
            if (result.Count > 0) return result;

            if (attempt < maxAttempts)
            {
                CrawlerStatus = string.Format("Gacha_CrawlerStatus_MetaRetry_Format".GetLocalized(), typeName,
                    delays[attempt - 1] / 1000, attempt, maxAttempts);
                await Task.Delay(delays[attempt - 1]);
            }
            else
            {
                DebugLog.Info("GachaAnalysisViewModel.Metadata",
                    $"[Gacha] {typeName} metadata fetch failed, still empty after {maxAttempts} attempts");
            }
        }

        return new List<ScrapedMetadata>();
    }

    private async Task<List<ScrapedMetadata>> FetchCalculatorListAsync(string url, object payload, string? cookie,
        string type)
    {
        var list = new List<ScrapedMetadata>();
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrEmpty(cookie))
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data)) return list;
            if (!data.TryGetProperty("list", out var items)) return list;

            foreach (var item in items.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name) || name == "旅行者") continue;

                var id = item.TryGetProperty("id", out var i) ? i.GetInt32().ToString() : "";
                var icon = item.TryGetProperty("icon", out var ic) ? ic.GetString() : null;

                var rank = "";
                if (type == "char")
                {
                    if (item.TryGetProperty("avatar_level", out var avLv)) rank = avLv.GetInt32().ToString();
                }
                else
                {
                    if (item.TryGetProperty("weapon_level", out var wpLv)) rank = wpLv.GetInt32().ToString();
                }

                var elementSrc = "";
                if (type == "char" && item.TryGetProperty("element_attr_id", out var elemId))
                {
                    var elementId = elemId.GetInt32();
                    elementSrc = ElementMapping.GetElementIconUrl(elementId) ?? "";
                }

                list.Add(new ScrapedMetadata
                {
                    Name = name!,
                    ImgSrc = icon,
                    ElementSrc = elementSrc,
                    Type = type,
                    ItemId = id,
                    Rank = rank
                });
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("GachaAnalysisViewModel.Metadata", $"[Gacha] Failed to fetch {type} list: {ex.Message}");
        }

        return list;
    }

    public void UpdateMetadata(List<ScrapedMetadata> scrapedData, bool deferRefresh = false)
    {
        IsFetching = false;
        IsScraping = false;

        if (scrapedData == null || scrapedData.Count == 0)
        {
            CrawlerStatus = "Gacha_CrawlerStatus_NoNewImages".GetLocalized();
            EnrichAndPersistCachedLogs(deferRefresh);
            return;
        }

        CrawlerStatus = string.Format("Gacha_CrawlerStatus_ImagesUpdated_Format".GetLocalized(), scrapedData.Count);

        SaveMetadataToDb(scrapedData);
        LoadMetadataFromDb();

        _ = ApplyMetadataToUIAsync(_savedMetadata);
        EnrichAndPersistCachedLogs(deferRefresh);
    }

    private void EnrichAndPersistCachedLogs(bool deferRefresh = false)
    {
        if (string.IsNullOrEmpty(_currentUid)) return;

        var total = _cachedCharacterLogs.Count + _cachedWeaponLogs.Count + _cachedChronicledLogs.Count
                    + _cachedNoviceLogs.Count + _cachedStandardLogs.Count;
        if (total == 0) return;

        var changed = FillMissingFieldsFromMetadata(
            _cachedCharacterLogs, _cachedWeaponLogs, _cachedChronicledLogs, _cachedNoviceLogs, _cachedStandardLogs);

        if (changed)
        {
            SaveGachaLogsToDb();
            if (!deferRefresh)
                RefreshUIFromCache();
        }
    }

    private async Task ApplyMetadataToUIAsync(List<ScrapedMetadata> metadataList)
    {
        if (metadataList == null || metadataList.Count == 0) return;
        var metaDict = metadataList.GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First());

        await UpdateCollectionImagesAsync(CharacterFiveStars, metaDict);
        await UpdateCollectionImagesAsync(CharacterFourStars, metaDict);
        await UpdateCollectionImagesAsync(WeaponFiveStars, metaDict);
        await UpdateCollectionImagesAsync(WeaponFourStars, metaDict);
        await UpdateCollectionImagesAsync(StandardFiveStars, metaDict);
        await UpdateCollectionImagesAsync(StandardFourStars, metaDict);
    }

    private async Task UpdateCollectionImagesAsync(ObservableCollection<GachaDisplayItem> collection,
        Dictionary<string, ScrapedMetadata> metaDict)
    {
        if (collection == null || collection.Count == 0) return;

        var items = collection.ToList();
        var updates = new List<(GachaDisplayItem item, string imgUrl, string elementUrl)>();

        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                ScrapedMetadata match = null;
                if (metaDict.TryGetValue(item.Name, out var exactMatch))
                {
                    match = exactMatch;
                }
                else
                {
                    match = metaDict.Values.FirstOrDefault(x =>
                        x.Name != null && item.Name != null &&
                        (x.Name.Contains(item.Name) || item.Name.Contains(x.Name)));
                }

                if (match != null)
                {
                    var imgUrl = !string.IsNullOrEmpty(match.ImgSrc) ? match.ImgSrc : null;
                    var elementUrl = (item.Type == "角色" || item.Type == "常驻") && !string.IsNullOrEmpty(match.ElementSrc)
                        ? match.ElementSrc
                        : null;
                    if (imgUrl != null || elementUrl != null)
                    {
                        updates.Add((item, imgUrl, elementUrl));
                    }
                }
            }
        });

        if (updates.Count > 0)
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                foreach (var (item, imgUrl, elementUrl) in updates)
                {
                    if (imgUrl != null) item.ImageUrl = imgUrl;
                    if (elementUrl != null) item.ElementUrl = elementUrl;
                }
            });
        }
    }

    #endregion
}