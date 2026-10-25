/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services.Background
{
    public interface IHoyoverseContentService
    {
        Task<ContentInfo> GetGameContentAsync(ServerType server);
    }

    public class HoyoverseContentService : IHoyoverseContentService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static HoyoverseContentService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            DebugLog.Info("HoyoverseContentService", "HoyoverseContentService: HttpClient initialized");
        }

        private static string GetApiLanguage()
        {
            var culture = ResourceExtensions.CurrentCulture;
            return string.IsNullOrEmpty(culture) ? "zh-cn" : culture.ToLowerInvariant();
        }


        public async Task<ContentInfo> GetGameContentAsync(ServerType server)
        {
            try
            {
                DebugLog.Info("HoyoverseContentService",
                    $"HoyoverseContentService: Requesting announcement content for {server}");

                var apiUrl = server switch
                {
                    ServerType.CN => ApiEndpoints.ContentCnApi,
                    ServerType.OS =>
                        ApiEndpoints.ContentOsApi.Replace("language=zh-cn", $"language={GetApiLanguage()}"),
                    _ => ApiEndpoints.ContentCnApi
                };

                var response = await _httpClient.GetStringAsync(apiUrl);
                DebugLog.Info("HoyoverseContentService", $"HoyoverseContentService: Response length {response.Length}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var result = JsonSerializer.Deserialize<HoyoverseContentResponse>(response, options);

                if (result?.Retcode != 0)
                {
                    DebugLog.Info("HoyoverseContentService",
                        $"HoyoverseContentService: API error code {result?.Retcode}");
                    return null;
                }

                return result.Data?.Content;
            }
            catch (Exception ex)
            {
                DebugLog.Info("HoyoverseContentService",
                    $"HoyoverseContentService: Request failed - {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }
}