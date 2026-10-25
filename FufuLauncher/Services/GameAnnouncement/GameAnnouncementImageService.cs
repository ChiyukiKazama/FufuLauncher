/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.GameAnnouncement
{
    public class GameAnnouncementImageService : IGameAnnouncementImageService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

        private readonly ConcurrentDictionary<string, CachedImage> _cache = new();

        static GameAnnouncementImageService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36");
        }

        public async Task<byte[]?> GetImageBytesAsync(string? url, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (_cache.TryGetValue(url, out CachedImage? cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return cached.Bytes;
            }

            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                {
                    request.Headers.Referrer = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
                }

                using HttpResponseMessage response = await HttpClient.SendAsync(
                    request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    DebugLog.Info("GameAnnouncementImageService",
                        $"[GameAnnouncementImageService] Image download failed {url} -> {(int)response.StatusCode}");
                    return null;
                }

                byte[] bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                if (bytes.Length == 0)
                {
                    return null;
                }

                _cache[url] = new CachedImage(bytes, DateTimeOffset.UtcNow + CacheLifetime);
                return bytes;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameAnnouncementImageService",
                    $"[GameAnnouncementImageService] Image download error {url}: {ex.Message}");
                return null;
            }
        }

        private sealed record CachedImage(byte[] Bytes, DateTimeOffset ExpiresAtUtc);
    }
}