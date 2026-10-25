/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using System.Net;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.EasterEgg;

public static class EasterEggMediaCache
{
    private const string VideoFileName = "BgVideo.mp4";
    private const string MusicFileName = "BgMusic.wav";
    private const int MaxRetryCount = 2;

    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> InFlight = new(StringComparer.Ordinal);
    private static readonly HttpClient Client = CreateHttpClient();

    public static string CacheDirectory => Path.Combine(Path.GetTempPath(), "FufuLauncher", "EasterEgg");

    public static Task<string?> GetVideoAsync(CancellationToken cancellationToken = default) =>
        GetAsync(ApiEndpoints.EasterEggVideoUrl, VideoFileName, cancellationToken);

    public static Task<string?> GetMusicAsync(CancellationToken cancellationToken = default) =>
        GetAsync(ApiEndpoints.EasterEggMusicUrl, MusicFileName, cancellationToken);

    private static async Task<string?> GetAsync(string url, string fileName, CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            Lazy<Task<string?>> lazy = InFlight.GetOrAdd(url, key => new Lazy<Task<string?>>(
                () => StartDownload(key, fileName),
                LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                DebugLog.Info("EasterEggMediaCache",
                    $"[EasterEggMediaCache] Failed to await download {url}: {ex.Message}");
                return null;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("EasterEggMediaCache", $"[EasterEggMediaCache] Get failed {url}: {ex.Message}");
            return null;
        }
    }

    private static Task<string?> StartDownload(string url, string fileName)
    {
        Task<string?> task = DownloadWithRetryAsync(url, fileName);
        _ = task.ContinueWith(
            completed => InFlight.TryRemove(url, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    private static async Task<string?> DownloadWithRetryAsync(string url, string fileName)
    {
        string filePath = Path.Combine(CacheDirectory, fileName);
        string tempPath = filePath + ".tmp";

        try
        {
            Directory.CreateDirectory(CacheDirectory);

            if (IsFreshCacheFile(filePath))
            {
                return filePath;
            }

            var uri = new Uri(url, UriKind.Absolute);
            Exception? lastError = null;

            for (int attempt = 0; attempt <= MaxRetryCount; attempt++)
            {
                try
                {
                    await DownloadFileAsync(uri, tempPath).ConfigureAwait(false);

                    if (!IsUsableFile(tempPath))
                    {
                        throw new IOException("Downloaded media file is empty.");
                    }

                    File.Move(tempPath, filePath, true);
                    DebugLog.Info("EasterEggMediaCache", $"[EasterEggMediaCache] Cached {uri} -> {filePath}");
                    return filePath;
                }
                catch (Exception ex) when (ex is HttpRequestException
                                               or IOException
                                               or UnauthorizedAccessException
                                               or TaskCanceledException
                                               or OperationCanceledException)
                {
                    lastError = ex;
                    TryDelete(tempPath);
                    DebugLog.Info("EasterEggMediaCache",
                        $"[EasterEggMediaCache] Download failed ({attempt + 1}/{MaxRetryCount + 1}) {uri}: {ex.Message}");

                    if (attempt == MaxRetryCount)
                    {
                        break;
                    }

                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1))).ConfigureAwait(false);
                    }
                    catch (Exception delayError)
                    {
                        DebugLog.Info("EasterEggMediaCache",
                            $"[EasterEggMediaCache] Retry delay failed: {delayError.Message}");
                        break;
                    }
                }
            }

            if (IsUsableFile(filePath))
            {
                DebugLog.Info("EasterEggMediaCache",
                    $"[EasterEggMediaCache] Falling back to stale cache {filePath}: {lastError?.Message}");
                return filePath;
            }

            DebugLog.Info("EasterEggMediaCache",
                $"[EasterEggMediaCache] Giving up download {url}: {lastError?.Message}");
            return null;
        }
        catch (Exception ex)
        {
            if (IsUsableFile(filePath))
            {
                DebugLog.Info("EasterEggMediaCache",
                    $"[EasterEggMediaCache] Using stale cache after error {filePath}: {ex.Message}");
                return filePath;
            }

            DebugLog.Info("EasterEggMediaCache", $"[EasterEggMediaCache] Download pipeline failed {url}: {ex.Message}");
            return null;
        }
    }

    private static async Task DownloadFileAsync(Uri uri, string tempPath)
    {
        using var timeout = new CancellationTokenSource(DownloadTimeout);
        CancellationToken token = timeout.Token;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using HttpResponseMessage response = await Client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? expectedLength = response.Content.Headers.ContentLength;

        using (var stream = new FileStream(
                   tempPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   81920,
                   FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            using Stream body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await body.CopyToAsync(stream, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);

            if (expectedLength is > 0 && stream.Length != expectedLength.Value)
            {
                throw new IOException($"Incomplete media download: {stream.Length}/{expectedLength} bytes.");
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };

        return new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    private static bool IsFreshCacheFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                   && info.Length > 0
                   && DateTime.UtcNow - info.LastWriteTimeUtc <= CacheMaxAge;
        }
        catch (Exception ex)
        {
            DebugLog.Info("EasterEggMediaCache",
                $"[EasterEggMediaCache] Failed to inspect cache file {path}: {ex.Message}");
            return false;
        }
    }

    private static bool IsUsableFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("EasterEggMediaCache", $"[EasterEggMediaCache] Failed to delete {path}: {ex.Message}");
        }
    }
}