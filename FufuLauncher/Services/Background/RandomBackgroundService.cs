using System.Diagnostics;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Background;

public enum RandomBackgroundType
{
    All,
    Home,
    Cg
}

public static class RandomBackgroundService
{
    private const string Endpoint = "https://image.fu1.fun/ba/";
    private const int MaxDownloadBytes = 20 * 1024 * 1024;
    public const string PinnedSettingKey = "IsRandomBackgroundPinned";
    private static readonly HttpClient Client;
    private static readonly object DisplayedImageLock = new();
    private static byte[]? _lastDisplayedBytes;
    private static string? _lastDisplayedPath;
    private static int _generation;

    static RandomBackgroundService()
    {
        Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        Client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/155.0.0.0 Safari/537.36");
        Client.DefaultRequestHeaders.Accept.ParseAdd("image/webp,image/*,*/*;q=0.8");
    }

    public static string CacheDirectory => Path.Combine(AppPaths.CacheDir, "RandomBackground");

    public static string PinnedImagePath =>
        Path.Combine(AppPaths.DataDir, "RandomBackgroundPinned", "pinned.img");

    public static bool HasPinnedImage => File.Exists(PinnedImagePath);

    public static RandomBackgroundType ParseType(string? value) =>
        Enum.TryParse<RandomBackgroundType>(value, true, out var type) &&
        Enum.IsDefined(typeof(RandomBackgroundType), type)
            ? type
            : RandomBackgroundType.All;

    private static string GetEndpoint(RandomBackgroundType type) => type switch
    {
        RandomBackgroundType.Home => Endpoint + "?type=home",
        RandomBackgroundType.Cg => Endpoint + "?type=cg",
        _ => Endpoint
    };

    private static string GetCacheFilePath(RandomBackgroundType type)
    {
        var fileName = type switch
        {
            RandomBackgroundType.Home => "next_home.img",
            RandomBackgroundType.Cg => "next_cg.img",
            _ => "next_all.img"
        };
        return Path.Combine(CacheDirectory, fileName);
    }

    public static async Task<BackgroundRenderResult?> LoadCachedAsync(RandomBackgroundType type)
    {
        var path = GetCacheFilePath(type);
        if (!File.Exists(path)) return null;

        try
        {
            var image = await BackgroundImageLoader.LoadFileAsync(path);
            lock (DisplayedImageLock)
            {
                _lastDisplayedPath = path;
                _lastDisplayedBytes = null;
            }
            return new BackgroundRenderResult { ImageSource = image, IsVideo = false };
        }
        catch (Exception ex)
        {
            DebugLog.Info("RandomBackgroundService", $"[RandomBackground] Failed to load cache: {ex.Message}");
            TryDelete(path);
            return null;
        }
    }

    public static async Task<BackgroundRenderResult?> LoadPinnedAsync()
    {
        var path = PinnedImagePath;
        if (!File.Exists(path)) return null;

        try
        {
            var image = await BackgroundImageLoader.LoadFileAsync(path);
            lock (DisplayedImageLock)
            {
                _lastDisplayedPath = path;
                _lastDisplayedBytes = null;
            }
            return new BackgroundRenderResult { ImageSource = image, IsVideo = false };
        }
        catch (Exception ex)
        {
            DebugLog.Info("RandomBackgroundService", $"[RandomBackground] Failed to load pinned image: {ex.Message}");
            return null;
        }
    }

    public static async Task<bool> PinCurrentImageAsync()
    {
        byte[]? bytes;
        string? sourcePath;
        lock (DisplayedImageLock)
        {
            bytes = _lastDisplayedBytes;
            sourcePath = _lastDisplayedPath;
        }

        if (bytes == null && (sourcePath == null || !File.Exists(sourcePath)))
            return false;

        var directory = Path.GetDirectoryName(PinnedImagePath);
        if (string.IsNullOrEmpty(directory))
            return false;

        var temp = PinnedImagePath + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            if (bytes != null)
                await File.WriteAllBytesAsync(temp, bytes);
            else
                File.Copy(sourcePath!, temp, true);

            File.Move(temp, PinnedImagePath, true);
            lock (DisplayedImageLock)
            {
                _lastDisplayedPath = PinnedImagePath;
                _lastDisplayedBytes = null;
            }
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("RandomBackgroundService", $"[RandomBackground] Failed to pin current image: {ex.Message}");
            TryDelete(temp);
            return false;
        }
    }

    public static async Task<BackgroundRenderResult?> FetchAndCacheAsync(RandomBackgroundType type)
    {
        var generation = Volatile.Read(ref _generation);
        var firstTask = DownloadAsync(type);
        var secondTask = DownloadAsync(type);
        await Task.WhenAll(firstTask, secondTask);
        var first = await firstTask;
        var second = await secondTask;

        if (generation != Volatile.Read(ref _generation))
            return null;

        var display = await CreateResultAsync(first);
        byte[]? displayBytes;
        byte[]? cache;
        if (display != null)
        {
            displayBytes = first;
            cache = second ?? first;
        }
        else
        {
            display = await CreateResultAsync(second);
            displayBytes = second;
            cache = second;
        }

        if (display == null || displayBytes == null || cache == null || cache.Length == 0)
            return null;

        lock (DisplayedImageLock)
        {
            _lastDisplayedBytes = displayBytes;
            _lastDisplayedPath = null;
        }

        await SaveNextCacheAsync(cache, type, generation);
        return display;
    }

    private static async Task<byte[]?> DownloadAsync(RandomBackgroundType type)
    {
        try
        {
            using var response = await Client.GetAsync(GetEndpoint(type), HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrEmpty(mediaType) ||
                !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            return bytes.Length is > 0 and <= MaxDownloadBytes ? bytes : null;
        }
        catch (Exception ex)
        {
            DebugLog.Info("RandomBackgroundService", $"[RandomBackground] Image download failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<BackgroundRenderResult?> CreateResultAsync(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
            return null;

        var temp = Path.Combine(CacheDirectory, $"display_{Guid.NewGuid():N}.img");
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllBytesAsync(temp, bytes);
            var image = await BackgroundImageLoader.LoadFileAsync(temp);
            return new BackgroundRenderResult { ImageSource = image, IsVideo = false };
        }
        catch (Exception ex)
        {
            DebugLog.Info("RandomBackgroundService", $"[RandomBackground] Image decode failed: {ex.Message}");
            return null;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task SaveNextCacheAsync(byte[] bytes, RandomBackgroundType type, int generation)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var temp = Path.Combine(CacheDirectory, $"next_{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(temp, bytes);

            if (generation != Volatile.Read(ref _generation))
            {
                TryDelete(temp);
                return;
            }

            var cachePath = GetCacheFilePath(type);
            File.Move(temp, cachePath, true);

            if (generation != Volatile.Read(ref _generation))
                TryDelete(cachePath);
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _generation))
                DebugLog.Info("RandomBackgroundService", $"[RandomBackground] Failed to write cache: {ex.Message}");
        }
    }

    public static void ClearCache()
    {
        Interlocked.Increment(ref _generation);
        try
        {
            if (Directory.Exists(CacheDirectory))
                Directory.Delete(CacheDirectory, true);
        }
        catch (Exception ex)
        {
            DebugLog.Info("RandomBackgroundService", $"[RandomBackground] Failed to clean cache: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
