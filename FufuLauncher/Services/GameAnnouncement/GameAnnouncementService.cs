/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Models.GameAnnouncement;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.GameAnnouncement
{
    public class GameAnnouncementService : IGameAnnouncementService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

        private readonly ConcurrentDictionary<string, CachedAnnouncements> _cache = new();

        static GameAnnouncementService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36");
        }

        public async Task<AnnouncementWrapper?> GetAnnouncementsAsync(
            string languageCode,
            AnnouncementRegion region,
            bool forceRefresh,
            CancellationToken token = default)
        {
            string cacheKey = $"{languageCode}|{region}";

            if (!forceRefresh
                && _cache.TryGetValue(cacheKey, out CachedAnnouncements? cached)
                && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return cached.Data;
            }

            AnnouncementWrapper? wrapper =
                await FetchAnnouncementsAsync(languageCode, region, token).ConfigureAwait(false);
            if (wrapper is not null)
            {
                _cache[cacheKey] = new CachedAnnouncements(wrapper, DateTimeOffset.UtcNow + CacheLifetime);
            }

            return wrapper;
        }

        private async Task<AnnouncementWrapper?> FetchAnnouncementsAsync(
            string languageCode,
            AnnouncementRegion region,
            CancellationToken token)
        {
            try
            {
                bool isOversea = region.IsOversea();
                string listUrl = string.Format(
                    isOversea ? ApiEndpoints.GameAnnouncementListOsUrl : ApiEndpoints.GameAnnouncementListCnUrl,
                    languageCode,
                    region.ToCode());

                string listJson = await HttpClient.GetStringAsync(listUrl, token).ConfigureAwait(false);
                GameAnnouncementListResponse? listResponse =
                    JsonSerializer.Deserialize<GameAnnouncementListResponse>(listJson, JsonOptions);

                if (listResponse?.Retcode != 0 || listResponse.Data is null)
                {
                    DebugLog.Info("GameAnnouncementService",
                        $"[GameAnnouncementService] Announcement list API returned error code {listResponse?.Retcode}");
                    return null;
                }

                AnnouncementWrapper wrapper = listResponse.Data;

                Dictionary<int, string> contentMap = new();
                if (wrapper.List is not null
                    && wrapper.List.Any(item => item.List?.Any(announcement => announcement.HasContent) == true))
                {
                    string contentUrl = string.Format(
                        isOversea
                            ? ApiEndpoints.GameAnnouncementContentOsUrl
                            : ApiEndpoints.GameAnnouncementContentCnUrl,
                        languageCode,
                        region.ToCode());

                    string contentJson = await HttpClient.GetStringAsync(contentUrl, token).ConfigureAwait(false);
                    GameAnnouncementContentResponse? contentResponse =
                        JsonSerializer.Deserialize<GameAnnouncementContentResponse>(contentJson, JsonOptions);

                    if (contentResponse?.Retcode == 0 && contentResponse.Data?.List is not null)
                    {
                        foreach (AnnouncementContent content in contentResponse.Data.List)
                        {
                            contentMap.TryAdd(content.AnnId, content.Content);
                        }
                    }
                }

                PreprocessAnnouncements(contentMap, wrapper.List);
                return wrapper;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameAnnouncementService",
                    $"[GameAnnouncementService] Failed to fetch announcements: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static void PreprocessAnnouncements(
            Dictionary<int, string> contentMap,
            List<AnnouncementListWrapper>? wrappers)
        {
            if (wrappers is null)
            {
                return;
            }

            foreach (AnnouncementListWrapper listWrapper in wrappers)
            {
                if (listWrapper.List is null)
                {
                    continue;
                }

                foreach (Models.GameAnnouncement.GameAnnouncement item in listWrapper.List)
                {
                    item.Subtitle = new StringBuilder(item.Subtitle)
                        .Replace("\r<br>", string.Empty)
                        .Replace("<br />", string.Empty)
                        .ToString();

                    item.Content = GameAnnouncementRegex.XmlTimeTagRegex.Replace(
                        contentMap.GetValueOrDefault(item.AnnId, string.Empty),
                        match => match.Groups[1].Value);
                }
            }
        }

        private sealed record CachedAnnouncements(AnnouncementWrapper Data, DateTimeOffset ExpiresAtUtc);
    }
}