/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;

namespace FufuLauncher.Services.GameServer;

public sealed class GameChannelSdkService
{
    private const string SdkVersionFileName = "sdk_pkg_version";
    private static readonly KeyedSemaphoreSlim CacheLocks = new();
    private static readonly JsonSerializerOptions VersionJsonOptions = new() { NumberHandling = JsonNumberHandling.AllowReadingFromString };
    private static readonly string[] LegacySdkPaths =
    [
        $"{GameConstants.CN_DATA_DIR}/Plugins/PCGameSDK.dll",
        $"{GameConstants.OS_DATA_DIR}/Plugins/PCGameSDK.dll",
        $"{GameConstants.CN_DATA_DIR}/Plugins/EOSSDK-Win64-Shipping.dll",
        $"{GameConstants.OS_DATA_DIR}/Plugins/EOSSDK-Win64-Shipping.dll",
        $"{GameConstants.CN_DATA_DIR}/Plugins/PluginEOSSDK.dll",
        $"{GameConstants.OS_DATA_DIR}/Plugins/PluginEOSSDK.dll",
        SdkVersionFileName,
    ];

    private readonly ChunkDownloader _chunkDownloader;
    private readonly SophonBuildClient _sophonBuildClient;

    public GameChannelSdkService(ChunkDownloader chunkDownloader, SophonBuildClient sophonBuildClient)
    {
        _chunkDownloader = chunkDownloader;
        _sophonBuildClient = sophonBuildClient;
    }

    public async Task SwitchSchemeAsync(string gameDir, GameServerScheme scheme,
        Action<string>? print = null, CancellationToken token = default, Action<long>? onBytesTransferred = null)
    {
        gameDir = Path.GetFullPath(gameDir);
        using var operation = await GameServerOperation.EnterAsync(gameDir, token).ConfigureAwait(false);
        var configuration = new GameServerConfigurationService();
        GameServerScheme current = configuration.TryDetectCurrentScheme(gameDir)
            ?? throw new DirectoryNotFoundException("Home_ErrNoGamePath".GetLocalized());
        if (current.IsOversea != scheme.IsOversea)
        {
            throw new InvalidOperationException("GameServer_ClientConversionRequired".GetLocalized());
        }

        var settings = new IniFile(Path.Combine(gameDir, GameConstants.CONFIG_FILE_NAME)).ReadAll(token);
        bool configMatches = settings.TryGetValue("General", out var general) &&
                             general.TryGetValue("channel", out string? channel) && channel == ((int)scheme.Channel).ToString() &&
                             general.TryGetValue("sub_channel", out string? subChannel) && subChannel == ((int)scheme.SubChannel).ToString() &&
                             general.TryGetValue("cps", out string? cps) && cps.Equals(scheme.Cps, StringComparison.OrdinalIgnoreCase);
        if (current.Equals(scheme) && configMatches &&
            await IsInstalledSdkValidAsync(gameDir, scheme, token).ConfigureAwait(false))
        {
            return;
        }

        await PrepareAndInstallAsync(gameDir, scheme, true, () => configuration.ApplyScheme(gameDir, scheme),
            print, token, onBytesTransferred).ConfigureAwait(false);
        configuration.BackupConfig(gameDir, scheme.IsOversea);
    }

    public Task EnsureSdkAndDeprecatedFilesAsync(string gameDir, GameServerScheme scheme,
        Action<string>? print = null, CancellationToken token = default, Action<long>? onBytesTransferred = null)
        => PrepareAndInstallAsync(gameDir, scheme, true, null, print, token, onBytesTransferred);

    public Task EnsureChannelSdkAsync(string gameDir, GameServerScheme scheme,
        Action<string>? print = null, CancellationToken token = default, Action<long>? onBytesTransferred = null)
        => PrepareAndInstallAsync(gameDir, scheme, false, null, print, token, onBytesTransferred);

    public async Task VerifyAndRepairChannelSdkAsync(string gameDir, GameServerScheme scheme,
        Action<string>? print = null, CancellationToken token = default, Action<long>? onBytesTransferred = null)
    {
        if (!await IsInstalledSdkValidAsync(gameDir, scheme, token).ConfigureAwait(false))
        {
            print?.Invoke("GameServer_ChannelSdkConflict".GetLocalized());
            await EnsureSdkAndDeprecatedFilesAsync(gameDir, scheme, print, token, onBytesTransferred)
                .ConfigureAwait(false);
        }
    }

    private async Task PrepareAndInstallAsync(string gameDir, GameServerScheme scheme, bool cleanLegacy,
        Action? commitConfiguration, Action<string>? print, CancellationToken token, Action<long>? onBytesTransferred)
    {
        gameDir = Path.GetFullPath(gameDir);
        // Query both indexes and validate/extract the entire package before touching the live files.
        print?.Invoke("GameServer_FetchingChannelSdk".GetLocalized());
        Task<SdkPackage?> sdkTask = GetChannelSdkPackageAsync(scheme, token);
        Task<List<string>> deprecatedTask = cleanLegacy
            ? GetDeprecatedFilesAsync(scheme, token) : Task.FromResult(new List<string>());
        await Task.WhenAll(sdkTask, deprecatedTask).ConfigureAwait(false);
        SdkPackage? package = await sdkTask.ConfigureAwait(false);
        List<string> deprecated = await deprecatedTask.ConfigureAwait(false);

        using var transaction = new GameServerFileTransaction(gameDir);
        var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (package is not null)
        {
            string archivePath = await GetCachedPackageAsync(package, print, token, onBytesTransferred)
                .ConfigureAwait(false);
            print?.Invoke("GameServer_ExtractingChannelSdk".GetLocalized());
            string stagingDir = Path.Combine(transaction.WorkDir, "sdk");
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                string destination = GameServerFileTransaction.ResolvePath(gameDir, relative);
                string staging = GameServerFileTransaction.ResolvePath(stagingDir, relative);
                if (entry.Name.Length == 0) { continue; }
                if (destination.Equals(Path.Combine(gameDir, GameConstants.CONFIG_FILE_NAME), StringComparison.OrdinalIgnoreCase)
                    || !staged.TryAdd(destination, staging))
                {
                    throw new InvalidDataException($"Invalid channel SDK entry: {entry.FullName}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(staging)!);
                await using Stream input = entry.Open();
                await using var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    ChunkDownloader.BufferSize, true);
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            }

            if (!await IsInstalledSdkValidAsync(stagingDir, scheme, token).ConfigureAwait(false))
            {
                throw new InvalidDataException("GameServer_ChannelSdkConflict".GetLocalized());
            }
        }

        var deprecatedPaths = deprecated.Select(name => GameServerFileTransaction.ResolvePath(gameDir, name)).ToList();
        if (deprecatedPaths.Any(path => staged.ContainsKey(path) ||
                path.Equals(Path.Combine(gameDir, GameConstants.CONFIG_FILE_NAME), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Channel SDK and deprecated file indexes conflict.");
        }

        token.ThrowIfCancellationRequested();
        await GameServerOperation.EnsureGameClosedAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        // Commit is deliberately non-cancellable; interruption here must not leave half a channel installed.
        try
        {
            if (cleanLegacy)
            {
                print?.Invoke("GameServer_CleaningLegacySdk".GetLocalized());
                foreach (string relative in LegacySdkPaths)
                {
                    transaction.Remove(GameServerFileTransaction.ResolvePath(gameDir, relative));
                }
            }

            foreach (var (destination, source) in staged)
            {
                transaction.Install(source, destination);
            }

            print?.Invoke("GameServer_ProcessingDeprecatedFiles".GetLocalized());
            foreach (string path in deprecatedPaths)
            {
                transaction.BackupDeprecated(path);
            }

            if (commitConfiguration is not null)
            {
                string configPath = Path.Combine(gameDir, GameConstants.CONFIG_FILE_NAME);
                transaction.Capture(configPath);
                commitConfiguration();
                transaction.MarkChanged(configPath);
            }
        }
        catch (Exception failure)
        {
            try { transaction.Rollback(); }
            catch (Exception rollbackFailure) { throw new AggregateException(failure, rollbackFailure); }
            throw;
        }
    }

    private async Task<string> GetCachedPackageAsync(SdkPackage package, Action<string>? print,
        CancellationToken token, Action<long>? onBytesTransferred)
    {
        string cacheDir = Path.Combine(AppPaths.ServerCacheDir, "channel-sdk");
        Directory.CreateDirectory(cacheDir);
        string path = Path.Combine(cacheDir, package.Md5.ToLowerInvariant() + ".zip");
        using (await CacheLocks.LockAsync(path, token).ConfigureAwait(false))
        {
            if (File.Exists(path) && new FileInfo(path).Length == package.Size &&
                (await HashUtility.Md5FileAsync(path, token).ConfigureAwait(false))
                .Equals(package.Md5, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            print?.Invoke("GameServer_DownloadingChannelSdk".GetLocalized());
            string temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await _chunkDownloader.DownloadFileAsync(package.Url, temporary, package.Size, package.Md5,
                    token, onBytesTransferred).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!File.Exists(temporary) || new FileInfo(temporary).Length != package.Size ||
                    !(await HashUtility.Md5FileAsync(temporary, token).ConfigureAwait(false))
                    .Equals(package.Md5, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("GameServer_ChannelSdkConflict".GetLocalized());
                }

                File.Move(temporary, path, true);
                return path;
            }
            finally
            {
                if (File.Exists(temporary)) { File.Delete(temporary); }
            }
        }
    }

    private static async Task<bool> IsInstalledSdkValidAsync(string gameDir, GameServerScheme scheme,
        CancellationToken token)
    {
        if (scheme.Channel != ChannelType.Bili && scheme.SubChannel != SubChannelType.Epic)
        {
            return !LegacySdkPaths.Any(relative => File.Exists(Path.Combine(gameDir, relative)));
        }

        string versionPath = Path.Combine(gameDir, SdkVersionFileName);
        if (!File.Exists(versionPath)) { return false; }
        bool hasFiles = false;
        bool hasPcSdk = false;
        try
        {
            using var reader = File.OpenText(versionPath);
            while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } row)
            {
                if (string.IsNullOrWhiteSpace(row)) { continue; }
                ChannelSdkVersionItem? item = JsonSerializer.Deserialize<ChannelSdkVersionItem>(row, VersionJsonOptions);
                if (item is null || string.IsNullOrWhiteSpace(item.RelativePath) || !IsMd5(item.Md5)) { return false; }
                string path = GameServerFileTransaction.ResolvePath(gameDir, item.RelativePath);
                if (!File.Exists(path) || new FileInfo(path).Length != item.FileSize ||
                    !(await HashUtility.Md5FileAsync(path, token).ConfigureAwait(false))
                    .Equals(item.Md5, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                hasFiles = true;
                hasPcSdk |= Path.GetFileName(path).Equals("PCGameSDK.dll", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        return hasFiles && (scheme.Channel != ChannelType.Bili || hasPcSdk);
    }

    private async Task<SdkPackage?> GetChannelSdkPackageAsync(GameServerScheme scheme, CancellationToken token)
    {
        using JsonDocument doc = await GetIndexAsync(scheme, "getGameChannelSDKs", token).ConfigureAwait(false);
        foreach (JsonElement sdk in GetArray(doc, "game_channel_sdks").EnumerateArray())
        {
            if (!MatchesGame(sdk, scheme)) { continue; }
            JsonElement pkg = sdk.GetProperty("channel_sdk_pkg");
            string? url = pkg.GetProperty("url").GetString();
            string? md5 = pkg.GetProperty("md5").GetString();
            JsonElement sizeElement = pkg.GetProperty("size");
            string sizeText = sizeElement.ValueKind == JsonValueKind.String
                ? sizeElement.GetString() ?? "" : sizeElement.GetRawText();
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || !IsMd5(md5) ||
                !long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out long size) || size <= 0)
            {
                throw new InvalidDataException("Invalid channel SDK package metadata.");
            }

            return new SdkPackage(url!, size, md5!);
        }

        if (scheme.Channel == ChannelType.Bili || scheme.SubChannel == SubChannelType.Epic)
        {
            throw new InvalidDataException("GameServer_ChannelSdkConflict".GetLocalized());
        }

        return null;
    }

    private async Task<List<string>> GetDeprecatedFilesAsync(GameServerScheme scheme, CancellationToken token)
    {
        using JsonDocument doc = await GetIndexAsync(scheme, "getGameDeprecatedFileConfigs", token).ConfigureAwait(false);
        var paths = new List<string>();
        foreach (JsonElement config in GetArray(doc, "deprecated_file_configs").EnumerateArray())
        {
            if (!MatchesGame(config, scheme)) { continue; }
            foreach (JsonElement file in config.GetProperty("deprecated_files").EnumerateArray())
            {
                string? name = file.GetProperty("name").GetString();
                if (string.IsNullOrWhiteSpace(name)) { throw new InvalidDataException("Invalid deprecated file path."); }
                paths.Add(name);
            }
        }

        return paths;
    }

    private async Task<JsonDocument> GetIndexAsync(GameServerScheme scheme, string endpoint, CancellationToken token)
    {
        string url = $"{scheme.HypApi}/{endpoint}?channel={(int)scheme.Channel}&game_ids[]={scheme.GameId}&launcher_id={scheme.LauncherId}&sub_channel={(int)scheme.SubChannel}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        string json;
        try { json = await _sophonBuildClient.GetStringWithRetryAsync(url, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"Channel resource query timed out: {endpoint}", ex);
        }

        JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("retcode", out JsonElement retcode) || retcode.ValueKind != JsonValueKind.Number ||
            !retcode.TryGetInt32(out int code) || code != 0)
        {
            doc.Dispose();
            throw new InvalidDataException($"Channel resource query failed ({endpoint}): {json}");
        }

        return doc;
    }

    private static JsonElement GetArray(JsonDocument doc, string name)
    {
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty(name, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Invalid channel resource response: {name}");
        }

        return array;
    }

    private static bool MatchesGame(JsonElement item, GameServerScheme scheme)
        => item.TryGetProperty("game", out JsonElement game) && game.ValueKind == JsonValueKind.Object &&
           game.TryGetProperty("id", out JsonElement id) && id.GetString() == scheme.GameId;

    private static bool IsMd5(string? value)
        => value is { Length: 32 } && value.All(Uri.IsHexDigit);

    private sealed record SdkPackage(string Url, long Size, string Md5);
}
