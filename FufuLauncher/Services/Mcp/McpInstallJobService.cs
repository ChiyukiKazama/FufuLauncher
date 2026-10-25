/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net.Http;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Views;

namespace FufuLauncher.Services.Mcp;

public sealed record McpInstallJobSnapshot(
    string JobId,
    string Status,
    int ProgressPercent,
    string Message,
    string? PluginId,
    string? PluginName,
    string? CaptchaUrl,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt);

public sealed class McpInstallJobService
{
    private const int MaxJobs = 20;
    private const int MaxCaptchaRetries = 3;

    private readonly PluginStoreService _store;
    private readonly LuaPluginInstaller _installer;
    private readonly LightweightPluginService _lightweightPlugin;
    private readonly object _gate = new();
    private readonly List<InstallJob> _jobs = new();
    private InstallJob? _active;
    private int _counter;

    public McpInstallJobService(PluginStoreService store, LuaPluginInstaller installer,
        LightweightPluginService lightweightPlugin)
    {
        _store = store;
        _installer = installer;
        _lightweightPlugin = lightweightPlugin;
    }

    public McpInstallJobSnapshot StartStoreInstall(string pluginId, string? accessKey)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new McpToolException("pluginId is required.");
        }

        var id = pluginId.Trim();
        return StartJob(id, (job, ct) => RunStoreAsync(job, id, accessKey, ct));
    }

    public McpInstallJobSnapshot StartPathInstall(string path, bool overwrite)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new McpToolException($"File not found: {fullPath}");
        }

        return StartJob(null, (job, ct) => RunPathAsync(job, fullPath, overwrite, ct));
    }

    public McpInstallJobSnapshot StartUrlInstall(string url, bool overwrite)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new McpToolException("url must be an http or https address.");
        }

        return StartJob(null, (job, ct) => RunUrlAsync(job, url.Trim(), overwrite, ct));
    }

    public McpInstallJobSnapshot? GetJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return null;
        }

        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(item =>
                string.Equals(item.Id, jobId.Trim(), StringComparison.OrdinalIgnoreCase));
            return job == null ? null : Snapshot(job);
        }
    }

    public IReadOnlyList<McpInstallJobSnapshot> GetJobs(int maximum = 10)
    {
        lock (_gate)
        {
            return _jobs.Take(Math.Max(1, maximum)).Select(Snapshot).ToList();
        }
    }

    public bool Cancel(string jobId)
    {
        InstallJob? job;
        lock (_gate)
        {
            job = _jobs.FirstOrDefault(item =>
                string.Equals(item.Id, jobId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (job == null || IsTerminal(job.Status))
            {
                return false;
            }

            job.Message = "Cancelling installation.";
            job.UpdatedAt = DateTimeOffset.Now;
        }

        try
        {
            job.Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return true;
    }

    private McpInstallJobSnapshot StartJob(string? pluginId, Func<InstallJob, CancellationToken, Task> run)
    {
        InstallJob job;
        lock (_gate)
        {
            if (_active != null && !IsTerminal(_active.Status))
            {
                throw new McpToolException(
                    $"Another installation is already running ({_active.Id}). Use get_install_status or cancel_install first.");
            }

            job = new InstallJob($"install-{++_counter}", pluginId);
            _jobs.Insert(0, job);
            if (_jobs.Count > MaxJobs)
            {
                _jobs.RemoveRange(MaxJobs, _jobs.Count - MaxJobs);
            }

            _active = job;
        }

        _ = Task.Run(() => ExecuteAsync(job, run));
        lock (_gate)
        {
            return Snapshot(job);
        }
    }

    private async Task ExecuteAsync(InstallJob job, Func<InstallJob, CancellationToken, Task> run)
    {
        Update(job, state =>
        {
            state.Status = "running";
            state.Message = "Starting installation.";
        });

        try
        {
            await run(job, job.Cts.Token).ConfigureAwait(false);
            Update(job, state =>
            {
                state.Status = "succeeded";
                state.ProgressPercent = 100;
                state.Message = "Installation completed.";
                state.Error = null;
            });
        }
        catch (OperationCanceledException)
        {
            Update(job, state =>
            {
                state.Status = "cancelled";
                state.Message = "Installation cancelled.";
            });
        }
        catch (McpToolException ex)
        {
            Update(job, state =>
            {
                state.Status = "failed";
                state.Message = "Installation failed.";
                state.Error = ex.Message;
            });
        }
        catch (Exception ex)
        {
            Update(job, state =>
            {
                state.Status = "failed";
                state.Message = "Installation failed.";
                state.Error = ex.Message;
            });
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_active, job))
                {
                    _active = null;
                }
            }

            job.Cts.Dispose();
        }
    }

    private async Task RunStoreAsync(InstallJob job, string pluginId, string? accessKey,
        CancellationToken cancellationToken)
    {
        Update(job, state =>
        {
            state.Message = "Reading plugin information.";
        });

        PluginStoreItem? fetched;
        try
        {
            fetched = await _store.GetPluginDetailAsync(pluginId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new McpToolException($"Failed to read plugin information: {ex.Message}");
        }

        var detail = fetched ??
                     throw new McpToolException($"Plugin not found in the store: {pluginId}");

        if (!string.IsNullOrWhiteSpace(detail.MinAppVersion) &&
            AppVersionHelper.IsNewerVersion(detail.MinAppVersion, AppVersionHelper.NumericVersion))
        {
            throw new McpToolException(
                $"This plugin requires launcher version {detail.MinAppVersion} or newer; current version is {AppVersionHelper.NumericVersion}.");
        }

        Update(job, state =>
        {
            state.PluginId = detail.Id;
            state.PluginName = detail.Name;
        });

        string? accessToken = null;
        string? archivePassword = null;

        if (detail.IsPrivate)
        {
            if (string.IsNullOrWhiteSpace(accessKey))
            {
                throw new McpToolException(
                    "This is a private plugin. Provide accessKey or install it manually from the launcher plugin store.");
            }

            try
            {
                var access = await _store.GetPrivateAccessAsync(detail.Id, accessKey).ConfigureAwait(false);
                accessToken = access.AccessToken;
                archivePassword = access.ArchivePassword;
                if (access.Plugin != null)
                {
                    detail = access.Plugin;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new McpToolException($"Private plugin authorization failed: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(detail.LuaInstallUrl))
        {
            throw new McpToolException("This plugin has no install script URL and cannot be installed through MCP.");
        }

        void OnProgress(DownloadProgressInfo info)
        {
            var percent = Math.Clamp((int)Math.Round(info.Percent), 0, 99);
            Update(job, state =>
            {
                if (state.Status == "running")
                {
                    state.ProgressPercent = Math.Max(state.ProgressPercent, percent);
                }
            });
        }

        _installer.ProgressChanged += OnProgress;
        try
        {
            string? downloadToken = null;
            var captchaAttempts = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await _installer.ExecuteInstallScriptAsync(
                        detail.LuaInstallUrl,
                        detail.LuaHash,
                        detail.FileHash,
                        cancellationToken,
                        detail.DllFileName,
                        detail.Id,
                        downloadToken,
                        accessToken,
                        detail.DownloadUrl,
                        archivePassword,
                        detail.ArchiveFileName).ConfigureAwait(false);
                    break;
                }
                catch (CaptchaRequiredException ex)
                {
                    captchaAttempts++;
                    if (captchaAttempts > MaxCaptchaRetries)
                    {
                        throw new McpToolException(
                            "Verification was required too many times; install the plugin manually from the plugin store.");
                    }

                    Update(job, state =>
                    {
                        state.Status = "awaiting_verification";
                        state.CaptchaUrl = ex.VerifyUrl;
                        state.Message = "Waiting for the user to complete verification in the opened window.";
                    });

                    var token = await PluginCaptchaDialog.ShowAsync(ex.VerifyUrl, cancellationToken)
                        .ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(token))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new McpToolException(
                            "The verification window was closed before verification completed.");
                    }

                    downloadToken = token;
                    Update(job, state =>
                    {
                        state.Status = "running";
                        state.CaptchaUrl = null;
                        state.Message = "Verification completed; retrying installation.";
                    });
                }
                catch (PluginArchivePasswordException)
                {
                    throw new McpToolException(
                        "The plugin archive requires a password; install it manually from the plugin store.");
                }
                catch (PrivatePluginAccessException ex)
                {
                    throw new McpToolException($"Private plugin access failed: {ex.Message}");
                }
            }
        }
        finally
        {
            _installer.ProgressChanged -= OnProgress;
        }

        var pluginDirectory = Path.Combine(PluginsDirectory, detail.Id);
        _installer.EnsureConfigFileEntry(pluginDirectory, detail.DllFileName);

        if (!Directory.Exists(pluginDirectory))
        {
            throw new McpToolException("The install script finished but the plugin folder was not created.");
        }

        Update(job, state =>
        {
            state.PluginId = detail.Id;
            state.PluginName = detail.Name;
            state.Message = "Installation completed.";
        });
    }

    private async Task RunPathAsync(InstallJob job, string fullPath, bool overwrite,
        CancellationToken cancellationToken)
    {
        Update(job, state =>
        {
            state.ProgressPercent = 10;
            state.Message = "Reading package.";
        });

        if (string.Equals(Path.GetExtension(fullPath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            InstallSingleDll(job, fullPath, overwrite);
            return;
        }

        var extractionDirectory = Path.Combine(Path.GetTempPath(), "FufuMcpExtract_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractionDirectory);

        try
        {
            await InstallArchiveAsync(job, fullPath, Path.GetFileNameWithoutExtension(fullPath), overwrite,
                extractionDirectory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(extractionDirectory);
        }
    }

    private async Task RunUrlAsync(InstallJob job, string url, bool overwrite, CancellationToken cancellationToken)
    {
        var uri = new Uri(url);
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "plugin.zip";
        }

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "FufuMcpDownload_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryFile = Path.Combine(temporaryDirectory, fileName);

        try
        {
            Update(job, state =>
            {
                state.ProgressPercent = 5;
                state.Message = "Downloading package.";
            });

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await client
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new McpToolException($"Plugin download failed: HTTP {(int)response.StatusCode}");
            }

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken)
                             .ConfigureAwait(false))
            await using (var target = File.Create(temporaryFile))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;

                    if (totalBytes > 0)
                    {
                        var percent = 5 + (int)(received * 85 / totalBytes);
                        Update(job, state =>
                        {
                            if (state.ProgressPercent < percent)
                            {
                                state.ProgressPercent = Math.Min(percent, 90);
                            }
                        });
                    }
                }
            }

            Update(job, state =>
            {
                state.ProgressPercent = 92;
                state.Message = "Installing package.";
            });

            if (string.Equals(Path.GetExtension(fileName), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                InstallSingleDll(job, temporaryFile, overwrite);
                return;
            }

            var extractionDirectory = Path.Combine(Path.GetTempPath(),
                "FufuMcpExtract_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractionDirectory);
            try
            {
                await InstallArchiveAsync(job, temporaryFile, Path.GetFileNameWithoutExtension(fileName),
                    overwrite, extractionDirectory, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TryDeleteDirectory(extractionDirectory);
            }
        }
        finally
        {
            TryDeleteFile(temporaryFile);
            TryDeleteDirectory(temporaryDirectory);
        }
    }

    private void InstallSingleDll(InstallJob job, string dllPath, bool overwrite)
    {
        var fileName = Path.GetFileName(dllPath);
        var folderName = Path.GetFileNameWithoutExtension(fileName);

        var blockReason = _lightweightPlugin.GetInstallBlockReason(folderName, fileName);
        if (blockReason != null)
        {
            throw new McpToolException($"Plugin installation is blocked by the launcher settings: {blockReason}");
        }

        var destinationDirectory = Path.Combine(PluginsDirectory, folderName);
        if (Directory.Exists(destinationDirectory) && !overwrite)
        {
            throw new McpToolException(
                $"Plugin directory already exists: {folderName}. Set overwrite=true to replace it.");
        }

        var lockedFile = FileLockHelper.FindLockedFileInDirectory(destinationDirectory);
        if (lockedFile != null)
        {
            throw new McpToolException($"The plugin file is in use: {FileLockHelper.GetLockedFileMessage(lockedFile)}");
        }

        Directory.CreateDirectory(destinationDirectory);
        File.Copy(dllPath, Path.Combine(destinationDirectory, fileName), true);
        _installer.EnsureConfigFileEntry(destinationDirectory, fileName);

        Update(job, state =>
        {
            state.PluginId = folderName;
            state.PluginName = folderName;
            state.ProgressPercent = 100;
            state.Message = "Installation completed.";
        });
    }

    private async Task InstallArchiveAsync(InstallJob job, string archivePath, string fallbackFolderName,
        bool overwrite, string extractionDirectory, CancellationToken cancellationToken)
    {
        Update(job, state =>
        {
            state.ProgressPercent = 30;
            state.Message = "Extracting package.";
        });

        var extractor = new PluginArchiveExtractor();
        try
        {
            await extractor.ExtractAsync(archivePath, extractionDirectory, null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PluginArchivePasswordException)
        {
            throw new McpToolException("The archive requires a password and cannot be installed through MCP.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new McpToolException($"Failed to extract the plugin archive: {ex.Message}");
        }

        var subDirectories = Directory.GetDirectories(extractionDirectory);
        var files = Directory.GetFiles(extractionDirectory);
        var sourceDirectory = extractionDirectory;
        var folderName = fallbackFolderName;

        if (subDirectories.Length == 1 && files.Length == 0)
        {
            sourceDirectory = subDirectories[0];
            folderName = new DirectoryInfo(sourceDirectory).Name;
        }

        var dllName = Directory.GetFiles(sourceDirectory, "*.dll", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .FirstOrDefault();

        var blockReason = _lightweightPlugin.GetInstallBlockReason(folderName, dllName);
        if (blockReason != null)
        {
            throw new McpToolException($"Plugin installation is blocked by the launcher settings: {blockReason}");
        }

        var destinationDirectory = Path.Combine(PluginsDirectory, folderName);
        var lockedFile = FileLockHelper.FindLockedFileInDirectory(destinationDirectory);
        if (lockedFile != null)
        {
            throw new McpToolException($"The plugin file is in use: {FileLockHelper.GetLockedFileMessage(lockedFile)}");
        }

        if (Directory.Exists(destinationDirectory))
        {
            if (!overwrite)
            {
                throw new McpToolException(
                    $"Plugin directory already exists: {folderName}. Set overwrite=true to replace it.");
            }

            Directory.Delete(destinationDirectory, true);
        }

        Update(job, state =>
        {
            state.ProgressPercent = 70;
            state.Message = "Installing package.";
        });

        Directory.CreateDirectory(PluginsDirectory);
        CopyDirectory(sourceDirectory, destinationDirectory);
        _installer.EnsureConfigFileEntry(destinationDirectory);

        Update(job, state =>
        {
            state.PluginId = folderName;
            state.PluginName = folderName;
            state.ProgressPercent = 100;
            state.Message = "Installation completed.";
        });
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory))
        {
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpInstallJob", $"[Mcp] Cleanup failed: {ex.Message}");
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpInstallJob", $"[Mcp] Cleanup failed: {ex.Message}");
        }
    }

    private static string PluginsDirectory => Path.Combine(AppContext.BaseDirectory, "Plugins");

    private void Update(InstallJob job, Action<InstallJob> mutate)
    {
        lock (_gate)
        {
            mutate(job);
            job.UpdatedAt = DateTimeOffset.Now;
        }
    }

    private McpInstallJobSnapshot Snapshot(InstallJob job) => new(
        job.Id,
        job.Status,
        job.ProgressPercent,
        job.Message,
        job.PluginId,
        job.PluginName,
        job.CaptchaUrl,
        job.Error,
        job.StartedAt,
        job.UpdatedAt);

    private static bool IsTerminal(string status) =>
        status is "succeeded" or "failed" or "cancelled";

    private sealed class InstallJob
    {
        public InstallJob(string id, string? pluginId)
        {
            Id = id;
            PluginId = pluginId;
            StartedAt = DateTimeOffset.Now;
            UpdatedAt = StartedAt;
            Status = "queued";
            Message = "Waiting to start.";
        }

        public string Id
        {
            get;
        }

        public CancellationTokenSource Cts
        {
            get;
        } = new();

        public DateTimeOffset StartedAt
        {
            get;
        }

        public string Status
        {
            get;
            set;
        }

        public int ProgressPercent
        {
            get;
            set;
        }

        public string Message
        {
            get;
            set;
        }

        public string? PluginId
        {
            get;
            set;
        }

        public string? PluginName
        {
            get;
            set;
        }

        public string? CaptchaUrl
        {
            get;
            set;
        }

        public string? Error
        {
            get;
            set;
        }

        public DateTimeOffset UpdatedAt
        {
            get;
            set;
        }
    }
}