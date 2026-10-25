/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models.Download;

namespace FufuLauncher.Services.Download;

public sealed class ParallelDownloadService
{
    private const string DefaultUserAgent = "FufuLauncher/1.0";

    private const string ResumeStateSuffix = ".state.json";

    private static readonly TimeSpan StallCheckInterval = TimeSpan.FromSeconds(1);

    public async Task<string> DownloadAsync(
        string url,
        string destinationPath,
        ParallelDownloadOptions? options = null,
        IProgress<ParallelDownloadProgress>? progress = null,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var opt = (options ?? ParallelDownloadOptions.Default).Validate();


        using var handler = CreateHandler(opt);
        using var client = CreateClient(handler);
        return await DownloadCoreAsync(client, url, destinationPath, opt, progress, token)
            .ConfigureAwait(false);
    }

    private async Task<string> DownloadCoreAsync(
        HttpClient client,
        string url,
        string destinationPath,
        ParallelDownloadOptions opt,
        IProgress<ParallelDownloadProgress>? progress,
        CancellationToken token)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        string directory = Path.GetDirectoryName(fullPath)
                           ?? throw new ArgumentException("目标路径缺少目录部分。", nameof(destinationPath));
        Directory.CreateDirectory(directory);

        if (File.Exists(fullPath) && !opt.OverwriteExisting)
        {
            throw new DownloadFailedException(
                $"目标文件已存在: {fullPath}", url, 0, 0, DownloadFailureReason.TargetUnwritable);
        }

        string workPath = opt.UseTemporaryFile ? fullPath + ".part" : fullPath;
        string resumeStatePath = workPath + ResumeStateSuffix;


        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (opt.OverallTimeout is { } overall && overall > TimeSpan.Zero)
        {
            overallCts.CancelAfter(overall);
        }

        CancellationToken ct = overallCts.Token;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var probe = await ProbeAsync(client, url, opt, ct).ConfigureAwait(false);
            long totalSize = opt.ExpectedSize ?? probe.ContentLength;

            if (opt.ExpectedSize is { } expected && probe.ContentLength > 0 && probe.ContentLength != expected)
            {
                throw new DownloadFailedException(
                    $"文件大小与预期不符: 服务端 {probe.ContentLength}B，预期 {expected}B",
                    url, probe.ContentLength, 0, DownloadFailureReason.IntegrityMismatch);
            }

            bool canParallel = probe.SupportsRange
                               && totalSize > opt.MinParallelFileSize
                               && opt.MaxParallelSegments > 1;

            if (!canParallel && !probe.SupportsRange && !opt.AllowRangeFallback)
            {
                throw new DownloadFailedException(
                    "服务端不支持分段下载（缺少 Accept-Ranges: bytes）。",
                    url, totalSize, 0, DownloadFailureReason.RangeNotSupported);
            }

            if (canParallel)
            {
                try
                {
                    await DownloadParallelAsync(client, url, workPath, totalSize, opt, progress, ct, resumeStatePath)
                        .ConfigureAwait(false);
                }
                catch (DownloadFailedException ex)
                    when (ex.Reason == DownloadFailureReason.RangeNotSupported && opt.AllowRangeFallback)
                {
                    DebugLog.Info("ParallelDownloadService",
                        "[ParallelDownload] Server ignored Range, falling back to single thread");
                    TryDelete(resumeStatePath);
                    await DownloadSingleThreadAsync(client, url, workPath, totalSize, opt, progress, ct)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                DebugLog.Info("ParallelDownloadService",
                    $"[ParallelDownload] Single thread (range supported={probe.SupportsRange}, size={totalSize}B)");
                await DownloadSingleThreadAsync(client, url, workPath, totalSize, opt, progress, ct)
                    .ConfigureAwait(false);
            }

            long actualLength = new FileInfo(workPath).Length;
            if (totalSize > 0 && actualLength != totalSize)
            {
                throw new DownloadFailedException(
                    $"下载大小不符: 实际 {actualLength}B，预期 {totalSize}B",
                    url, totalSize, 0, DownloadFailureReason.IntegrityMismatch);
            }

            await VerifyHashAsync(workPath, url, opt, totalSize, ct).ConfigureAwait(false);

            if (opt.UseTemporaryFile)
            {
                File.Move(workPath, fullPath, overwrite: true);
            }

            TryDelete(resumeStatePath);

            stopwatch.Stop();
            DebugLog.Info("ParallelDownloadService",
                $"[ParallelDownload] Completed {Path.GetFileName(fullPath)} " +
                $"({actualLength / 1024.0 / 1024.0:F2} MB, {stopwatch.Elapsed.TotalSeconds:F1}s)");

            progress?.Report(new ParallelDownloadProgress
            {
                BytesDownloaded = actualLength,
                TotalBytes = totalSize > 0 ? totalSize : actualLength,
                BytesPerSecond = 0,
                CompletedChunks = 1,
                TotalChunks = 1,
                IsCompleted = true,
            });

            return fullPath;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            Cleanup(workPath, resumeStatePath, opt);
            throw new DownloadFailedException(
                "下载超时。", url, 0, 0, DownloadFailureReason.TimedOut);
        }
        catch (OperationCanceledException)
        {
            Cleanup(workPath, resumeStatePath, opt);
            throw new DownloadFailedException(
                "下载已取消。", url, 0, 0, DownloadFailureReason.Cancelled);
        }
        catch (DownloadFailedException)
        {
            Cleanup(workPath, resumeStatePath, opt);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            Cleanup(workPath, resumeStatePath, opt);
            throw new DownloadFailedException(
                $"目标路径不可写: {ex.Message}", url, 0, 0,
                DownloadFailureReason.TargetUnwritable, ex);
        }
        catch (Exception ex)
        {
            Cleanup(workPath, resumeStatePath, opt);
            throw new DownloadFailedException(
                $"下载失败: {ex.Message}", url, 0, 0, DownloadFailureReason.Unknown, ex);
        }
    }

    public async Task<IReadOnlyList<ParallelDownloadResult>> DownloadManyAsync(
        IEnumerable<ParallelDownloadItem> items,
        ParallelDownloadOptions? options = null,
        IProgress<ParallelDownloadProgress>? progress = null,
        int maxConcurrentFiles = 3,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var list = items.ToList();
        var results = new ParallelDownloadResult[list.Count];
        if (list.Count == 0)
        {
            return results;
        }


        var batchOpt = (options ?? ParallelDownloadOptions.Default).Validate();
        int peakSegments = list
            .Select(i => (i.Options ?? batchOpt).Validate().MaxParallelSegments)
            .DefaultIfEmpty(batchOpt.MaxParallelSegments)
            .Max();


        int explicitConnections = new[] { options?.MaxConnectionsPerServer ?? 0 }
            .Concat(list.Select(i => i.Options?.MaxConnectionsPerServer ?? 0))
            .Max();

        var handlerOpt = batchOpt with
        {
            MaxConnectionsPerServer = explicitConnections > 0
                ? explicitConnections
                : Math.Max(peakSegments, maxConcurrentFiles),
        };

        using var handler = CreateHandler(handlerOpt);
        using var client = CreateClient(handler);
        using var gate = new SemaphoreSlim(Math.Max(maxConcurrentFiles, 1));


        var tasks = list.Select((item, index) => Task.Run(async () =>
        {
            try
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                results[index] = new ParallelDownloadResult(item.Url, null, ex);
                return;
            }

            try
            {
                var itemOpt = (item.Options ?? batchOpt).Validate();
                string path = await DownloadCoreAsync(
                        client, item.Url, item.DestinationPath, itemOpt, progress, token)
                    .ConfigureAwait(false);
                results[index] = new ParallelDownloadResult(item.Url, path, null);
            }
            catch (Exception ex)
            {
                results[index] = new ParallelDownloadResult(item.Url, null, ex);
            }
            finally
            {
                gate.Release();
            }
        })).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }


    private static SocketsHttpHandler CreateHandler(ParallelDownloadOptions opt) => new()
    {
        MaxConnectionsPerServer = opt.MaxConnectionsPerServer,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = opt.ConnectTimeout,
        EnableMultipleHttp2Connections = true,
    };

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler, disposeHandler: true)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, ParallelDownloadOptions opt)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.UserAgent.ParseAdd(opt.UserAgent ?? DefaultUserAgent);

        req.Headers.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("identity"));
        return req;
    }


    private readonly record struct ProbeResult(bool SupportsRange, long ContentLength);

    private static async Task<ProbeResult> ProbeAsync(
        HttpClient client, string url, ParallelDownloadOptions opt, CancellationToken ct)
    {
        using (var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            probeCts.CancelAfter(opt.RequestTimeout);

            HttpResponseMessage? headResp = null;
            try
            {
                using var req = CreateRequest(HttpMethod.Head, url, opt);
                headResp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, probeCts.Token)
                    .ConfigureAwait(false);

                if (headResp.IsSuccessStatusCode)
                {
                    return new ProbeResult(SupportsRange(headResp), headResp.Content.Headers.ContentLength ?? 0);
                }

                if (headResp.StatusCode is not (HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented))
                {
                    throw new DownloadFailedException(
                        $"HEAD 预检失败: HTTP {(int)headResp.StatusCode} {headResp.ReasonPhrase}",
                        url, 0, 0, DownloadFailureReason.PreFlightFailed);
                }
            }
            catch (HttpRequestException ex)
            {
                throw new DownloadFailedException(
                    $"HEAD 预检网络错误: {ex.Message}", url, 0, 0,
                    DownloadFailureReason.PreFlightFailed, ex);
            }
            finally
            {
                headResp?.Dispose();
            }


            using var probeReq = CreateRequest(HttpMethod.Get, url, opt);
            probeReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var probeResp = await client
                .SendAsync(probeReq, HttpCompletionOption.ResponseHeadersRead, probeCts.Token)
                .ConfigureAwait(false);

            if (!probeResp.IsSuccessStatusCode)
            {
                throw new DownloadFailedException(
                    $"预检失败: HTTP {(int)probeResp.StatusCode} {probeResp.ReasonPhrase}",
                    url, 0, 0, DownloadFailureReason.PreFlightFailed);
            }

            long length = probeResp.Content.Headers.ContentRange?.Length
                          ?? probeResp.Content.Headers.ContentLength
                          ?? 0;

            return new ProbeResult(SupportsRange(probeResp), length);
        }
    }

    private static bool SupportsRange(HttpResponseMessage resp) =>
        resp.Headers.AcceptRanges?.Any(r => r.Equals("bytes", StringComparison.OrdinalIgnoreCase)) == true;


    private static async Task DownloadSingleThreadAsync(
        HttpClient client, string url, string workPath,
        long totalSize, ParallelDownloadOptions opt,
        IProgress<ParallelDownloadProgress>? progress, CancellationToken ct)
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        long lastProgressTicks = Environment.TickCount64;


        PeriodicTimer? stallTimer = null;
        Task? watchdog = null;

        try
        {
            using var req = CreateRequest(HttpMethod.Get, url, opt);

            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(attemptCts.Token);
            if (opt.RequestTimeout > TimeSpan.Zero)
            {
                sendCts.CancelAfter(opt.RequestTimeout);
            }

            HttpResponseMessage resp;
            try
            {
                resp = await client.SendAsync(
                    req, HttpCompletionOption.ResponseHeadersRead, sendCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new DownloadFailedException(
                    $"建立连接或排队超时（>{opt.RequestTimeout.TotalSeconds:F0}s）。",
                    url, totalSize, 0, DownloadFailureReason.TimedOut);
            }

            using (resp)
            {
                resp.EnsureSuccessStatusCode();

                await using var source = await resp.Content.ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false);
                await using var destination = new FileStream(
                    workPath, FileMode.Create, FileAccess.Write, FileShare.None, opt.BufferSize, useAsync: true);

                if (opt.StallTimeout > TimeSpan.Zero)
                {
                    stallTimer = new PeriodicTimer(StallCheckInterval);
                    watchdog = RunStallWatchdogAsync(
                        stallTimer, attemptCts, () => Volatile.Read(ref lastProgressTicks), opt.StallTimeout);
                }

                var limiter = new TokenBucketRateLimiter(opt.MaxBytesPerSecond);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(opt.BufferSize);
                long downloaded = 0;
                long lastReportTicks = Environment.TickCount64;
                long lastBytes = 0;

                try
                {
                    while (true)
                    {
                        int read = await source.ReadAsync(buffer.AsMemory(0, opt.BufferSize), attemptCts.Token)
                            .ConfigureAwait(false);
                        if (read <= 0)
                        {
                            break;
                        }

                        await limiter.AcquireAsync(read, attemptCts.Token).ConfigureAwait(false);


                        Volatile.Write(ref lastProgressTicks, Environment.TickCount64);
                        await destination.WriteAsync(buffer.AsMemory(0, read), attemptCts.Token).ConfigureAwait(false);

                        downloaded += read;

                        long now = Environment.TickCount64;
                        double elapsed = (now - lastReportTicks) / 1000.0;
                        if (elapsed >= opt.ProgressReportInterval.TotalSeconds)
                        {
                            progress?.Report(new ParallelDownloadProgress
                            {
                                BytesDownloaded = downloaded,
                                TotalBytes = totalSize,
                                BytesPerSecond = (downloaded - lastBytes) / elapsed,
                                TotalChunks = 1,
                                ActiveSegments = 1,
                            });
                            lastBytes = downloaded;
                            lastReportTicks = now;
                        }
                    }

                    await destination.FlushAsync(attemptCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new DownloadFailedException(
                "单线程下载停滞超时。", url, totalSize, 0, DownloadFailureReason.Stalled);
        }
        finally
        {
            stallTimer?.Dispose();
            if (watchdog != null)
            {
                try
                {
                    await watchdog.ConfigureAwait(false);
                }
                catch
                {
                    /* 看门狗自身异常一律忽略 */
                }
            }
        }
    }


    private sealed class ParallelState
    {
        public long TotalDownloaded;
        public int CompletedChunks;
        public int ActiveSegments;
        public int FailedChunks;
        public int TotalChunks;
        public Exception? LastError;

        public long LastReportTicks = Environment.TickCount64;
        public long LastReportBytes;
        public readonly object ReportLock = new();
        public readonly object ErrorLock = new();
    }

    private async Task DownloadParallelAsync(
        HttpClient client, string url, string workPath, long totalSize,
        ParallelDownloadOptions opt, IProgress<ParallelDownloadProgress>? progress,
        CancellationToken ct, string resumeStatePath)
    {
        var chunks = BuildChunks(totalSize, opt);
        DebugLog.Info("ParallelDownloadService",
            $"[ParallelDownload] Parallel {chunks.Count} chunks × {opt.MaxParallelSegments} threads " +
            $"(total {totalSize / 1024.0 / 1024.0:F2}MB)");


        var completed = opt.EnableResume
            ? LoadResumeState(resumeStatePath, url, totalSize, chunks.Count)
            : new HashSet<int>();


        if (completed.Count > 0 && !IsWorkFileIntact(workPath, totalSize))
        {
            DebugLog.Info("ParallelDownloadService",
                "[ParallelDownload] .part file missing or wrong length, discarding resume state");
            completed.Clear();
            TryDelete(resumeStatePath);
        }

        long alreadyDone = 0;
        foreach (int i in completed)
        {
            alreadyDone += chunks[i].End - chunks[i].Start + 1;
        }


        using var handle = File.OpenHandle(
            workPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite,
            FileOptions.Asynchronous);

        if (RandomAccess.GetLength(handle) != totalSize)
        {
            RandomAccess.SetLength(handle, totalSize);
        }

        var state = new ParallelState
        {
            TotalDownloaded = alreadyDone,
            CompletedChunks = completed.Count,
            TotalChunks = chunks.Count,
        };

        var queue = new ConcurrentQueue<int>();
        for (int i = 0; i < chunks.Count; i++)
        {
            if (!completed.Contains(i))
            {
                queue.Enqueue(i);
            }
        }

        var globalLimiter = new TokenBucketRateLimiter(opt.MaxBytesPerSecond);
        var resumeLock = new object();


        using var parallelAbort = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var workers = new List<Task>(opt.MaxParallelSegments);
        for (int w = 0; w < opt.MaxParallelSegments; w++)
        {
            var segmentLimiter = new TokenBucketRateLimiter(opt.MaxBytesPerSecondPerSegment);
            workers.Add(Task.Run(async () =>
            {
                Interlocked.Increment(ref state.ActiveSegments);
                try
                {
                    while (queue.TryDequeue(out int index))
                    {
                        parallelAbort.Token.ThrowIfCancellationRequested();
                        var (start, end) = chunks[index];
                        try
                        {
                            await DownloadChunkAsync(
                                    client, url, start, end, handle, parallelAbort.Token,
                                    state, totalSize, opt, progress, globalLimiter, segmentLimiter)
                                .ConfigureAwait(false);

                            Interlocked.Increment(ref state.CompletedChunks);
                            if (opt.EnableResume)
                            {
                                lock (resumeLock)
                                {
                                    completed.Add(index);
                                    SaveResumeState(resumeStatePath, url, totalSize, chunks.Count, completed);
                                }
                            }
                        }
                        catch (DownloadFailedException ex)
                            when (ex.Reason == DownloadFailureReason.RangeNotSupported)
                        {
                            lock (state.ErrorLock)
                            {
                                state.LastError = ex;
                            }

                            Interlocked.Increment(ref state.FailedChunks);
                            parallelAbort.Cancel();
                            throw;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref state.FailedChunks);
                            lock (state.ErrorLock)
                            {
                                state.LastError = ex;
                            }
                        }
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref state.ActiveSegments);
                }
            }, CancellationToken.None));
        }

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (DownloadFailedException ex) when (ex.Reason == DownloadFailureReason.RangeNotSupported)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            Exception? cause;
            lock (state.ErrorLock)
            {
                cause = state.LastError;
            }

            if (cause is DownloadFailedException { Reason: DownloadFailureReason.RangeNotSupported } rns)
            {
                throw rns;
            }

            throw;
        }

        if (Volatile.Read(ref state.FailedChunks) > 0)
        {
            ct.ThrowIfCancellationRequested();

            Exception? last;
            lock (state.ErrorLock)
            {
                last = state.LastError;
            }

            int failed = Volatile.Read(ref state.FailedChunks);
            throw new DownloadFailedException(
                $"下载失败: {failed} 个小段出错" +
                (last != null ? $"（{last.GetType().Name}: {last.Message}）" : ""),
                url, totalSize, failed, DownloadFailureReason.ChunksFailed, last)
            {
                BytesDownloaded = Interlocked.Read(ref state.TotalDownloaded),
            };
        }

        if (opt.EnableResume)
        {
            TryDelete(resumeStatePath);
        }
    }

    private static List<(long Start, long End)> BuildChunks(long totalSize, ParallelDownloadOptions opt)
    {
        int totalChunks = Math.Max(opt.MaxParallelSegments * opt.ChunkMultiplier, 1);
        long chunkSize = totalSize / totalChunks;


        if (chunkSize < opt.MinChunkSize)
        {
            totalChunks = (int)Math.Max(totalSize / opt.MinChunkSize, 1);
            chunkSize = totalSize / totalChunks;
        }

        totalChunks = Math.Max(totalChunks, 1);
        var chunks = new List<(long, long)>(totalChunks);
        for (int i = 0; i < totalChunks; i++)
        {
            long start = i * chunkSize;
            long end = i == totalChunks - 1 ? totalSize - 1 : start + chunkSize - 1;
            chunks.Add((start, end));
        }

        return chunks;
    }

    private static async Task DownloadChunkAsync(
        HttpClient client, string url, long start, long end,
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        CancellationToken ct, ParallelState state, long totalSize,
        ParallelDownloadOptions opt, IProgress<ParallelDownloadProgress>? progress,
        TokenBucketRateLimiter globalLimiter, TokenBucketRateLimiter segmentLimiter)
    {
        long chunkSize = end - start + 1;
        Exception? lastError = null;


        long writtenThisAttempt = 0;

        for (int attempt = 0; attempt <= opt.MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            long lastProgressTicks = Environment.TickCount64;
            writtenThisAttempt = 0;


            PeriodicTimer? stallTimer = null;
            Task? watchdog = null;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(opt.BufferSize);
            try
            {
                using var req = CreateRequest(HttpMethod.Get, url, opt);
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);


                using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(attemptCts.Token);
                if (opt.RequestTimeout > TimeSpan.Zero)
                {
                    sendCts.CancelAfter(opt.RequestTimeout);
                }

                HttpResponseMessage resp;
                try
                {
                    resp = await client.SendAsync(
                        req, HttpCompletionOption.ResponseHeadersRead, sendCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new IOException($"建立连接或排队超时（>{opt.RequestTimeout.TotalSeconds:F0}s）。");
                }

                using (resp)
                {
                    if (resp.StatusCode == HttpStatusCode.OK)
                    {
                        throw new DownloadFailedException(
                            "服务端忽略了 Range 请求，返回完整内容。",
                            url, totalSize, 1, DownloadFailureReason.RangeNotSupported);
                    }

                    resp.EnsureSuccessStatusCode();


                    var contentRange = resp.Content.Headers.ContentRange;
                    if (contentRange?.From != start || contentRange.To != end)
                    {
                        throw new DownloadFailedException(
                            $"分段响应范围不符: 请求 [{start}-{end}]，返回 " +
                            (contentRange == null
                                ? "无 Content-Range 头"
                                : $"[{contentRange.From}-{contentRange.To}]"),
                            url, totalSize, 1, DownloadFailureReason.RangeNotSupported);
                    }

                    await using var stream = await resp.Content.ReadAsStreamAsync(attemptCts.Token)
                        .ConfigureAwait(false);


                    if (opt.StallTimeout > TimeSpan.Zero)
                    {
                        stallTimer = new PeriodicTimer(StallCheckInterval);
                        watchdog = RunStallWatchdogAsync(
                            stallTimer, attemptCts, () => Volatile.Read(ref lastProgressTicks), opt.StallTimeout);
                    }

                    long writePos = start;
                    long written = 0;
                    while (true)
                    {
                        int read = await stream.ReadAsync(buffer.AsMemory(0, opt.BufferSize), attemptCts.Token)
                            .ConfigureAwait(false);
                        if (read <= 0)
                        {
                            break;
                        }

                        await globalLimiter.AcquireAsync(read, attemptCts.Token).ConfigureAwait(false);
                        await segmentLimiter.AcquireAsync(read, attemptCts.Token).ConfigureAwait(false);


                        Volatile.Write(ref lastProgressTicks, Environment.TickCount64);

                        await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), writePos, attemptCts.Token)
                            .ConfigureAwait(false);

                        writePos += read;
                        written += read;
                        writtenThisAttempt += read;
                        Interlocked.Add(ref state.TotalDownloaded, read);
                        ReportProgress(state, totalSize, opt, progress);
                    }

                    if (written < chunkSize)
                    {
                        throw new IOException($"小段不完整: 预期 {chunkSize}B，实际 {written}B");
                    }

                    return;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = new DownloadFailedException(
                    "小段下载停滞超时。", url, totalSize, 1, DownloadFailureReason.Stalled);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DownloadFailedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                stallTimer?.Dispose();
                if (watchdog != null)
                {
                    try
                    {
                        await watchdog.ConfigureAwait(false);
                    }
                    catch
                    {
                        /* 看门狗自身异常一律忽略 */
                    }
                }
            }


            if (writtenThisAttempt > 0)
            {
                Interlocked.Add(ref state.TotalDownloaded, -writtenThisAttempt);
                writtenThisAttempt = 0;
            }

            if (attempt < opt.MaxRetries)
            {
                double backoff = opt.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt);
                var delay = TimeSpan.FromMilliseconds(
                    Math.Min(backoff, opt.RetryMaxDelay.TotalMilliseconds));
                DebugLog.Info("ParallelDownloadService",
                    $"[ParallelDownload] Chunk [{start}-{end}] attempt {attempt + 1} failed, " +
                    $"retrying in {delay.TotalSeconds:F1}s" +
                    (lastError != null ? $"（{lastError.Message}）" : ""));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        throw new IOException(
            $"小段 [{start}-{end}] 重试 {opt.MaxRetries} 次后仍失败", lastError);
    }

    private static async Task RunStallWatchdogAsync(
        PeriodicTimer timer, CancellationTokenSource attemptCts,
        Func<long> lastProgressTicks, TimeSpan stallTimeout)
    {
        long thresholdMs = (long)stallTimeout.TotalMilliseconds;
        try
        {
            while (await timer.WaitForNextTickAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (Environment.TickCount64 - lastProgressTicks() > thresholdMs)
                {
                    attemptCts.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void ReportProgress(
        ParallelState state, long totalSize,
        ParallelDownloadOptions opt, IProgress<ParallelDownloadProgress>? progress)
    {
        if (progress == null)
        {
            return;
        }

        long now = Environment.TickCount64;
        bool report = false;
        double speed = 0;
        long downloaded = 0;

        lock (state.ReportLock)
        {
            double elapsed = (now - state.LastReportTicks) / 1000.0;
            if (elapsed >= opt.ProgressReportInterval.TotalSeconds)
            {
                downloaded = Interlocked.Read(ref state.TotalDownloaded);
                speed = (downloaded - state.LastReportBytes) / elapsed;
                state.LastReportBytes = downloaded;
                state.LastReportTicks = now;
                report = true;
            }
        }

        if (report)
        {
            progress.Report(new ParallelDownloadProgress
            {
                BytesDownloaded = downloaded,
                TotalBytes = totalSize,
                BytesPerSecond = speed,
                CompletedChunks = Volatile.Read(ref state.CompletedChunks),
                TotalChunks = state.TotalChunks,
                ActiveSegments = Volatile.Read(ref state.ActiveSegments),
            });
        }
    }


    private static async Task VerifyHashAsync(
        string path, string url, ParallelDownloadOptions opt, long totalSize, CancellationToken ct)
    {
        if (opt.HashAlgorithm == DownloadHashAlgorithm.None || string.IsNullOrWhiteSpace(opt.ExpectedHash))
        {
            return;
        }

        string actual;
        if (opt.HashAlgorithm == DownloadHashAlgorithm.Md5)
        {
            actual = await HashUtility.Md5FileAsync(path, ct).ConfigureAwait(false);
        }
        else
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true);
            actual = await HashUtility.XxHash64HexAsync(stream, ct).ConfigureAwait(false);
        }

        if (!actual.Equals(opt.ExpectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new DownloadFailedException(
                $"哈希校验失败: 实际 {actual}，预期 {opt.ExpectedHash}",
                url, totalSize, 0, DownloadFailureReason.IntegrityMismatch);
        }
    }


    private static bool IsWorkFileIntact(string workPath, long totalSize)
    {
        try
        {
            var info = new FileInfo(workPath);
            return info.Exists && info.Length == totalSize;
        }
        catch (Exception ex)
        {
            DebugLog.Info("ParallelDownloadService", $"[ParallelDownload] Failed to check .part file: {ex.Message}");
            return false;
        }
    }

    private sealed record ResumeState(long TotalSize, int TotalChunks, string UrlHash, List<int> Completed);


    private static string ComputeUrlHash(string url) =>
        HashUtility.Md5Bytes(System.Text.Encoding.UTF8.GetBytes(url));

    private static HashSet<int> LoadResumeState(
        string statePath, string url, long totalSize, int totalChunks)
    {
        try
        {
            if (!File.Exists(statePath))
            {
                return new HashSet<int>();
            }

            var state = JsonSerializer.Deserialize<ResumeState>(File.ReadAllText(statePath));

            if (state == null
                || state.TotalSize != totalSize
                || state.TotalChunks != totalChunks
                || !string.Equals(state.UrlHash, ComputeUrlHash(url), StringComparison.OrdinalIgnoreCase))
            {
                DebugLog.Info("ParallelDownloadService",
                    "[ParallelDownload] Resume state does not match current source or parameters, ignoring");
                return new HashSet<int>();
            }

            return new HashSet<int>(state.Completed.Where(i => i >= 0 && i < totalChunks));
        }
        catch (Exception ex)
        {
            DebugLog.Info("ParallelDownloadService",
                $"[ParallelDownload] Failed to read resume state, ignoring: {ex.Message}");
            return new HashSet<int>();
        }
    }

    private static void SaveResumeState(
        string statePath, string url, long totalSize, int totalChunks, HashSet<int> completed)
    {
        try
        {
            var state = new ResumeState(
                totalSize, totalChunks, ComputeUrlHash(url), completed.OrderBy(i => i).ToList());
            File.WriteAllText(statePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            DebugLog.Info("ParallelDownloadService", $"[ParallelDownload] Failed to write resume state: {ex.Message}");
        }
    }


    private static void Cleanup(string workPath, string resumeStatePath, ParallelDownloadOptions opt)
    {
        if (opt.EnableResume)
        {
            return;
        }

        if (opt.DeleteOnFailure && opt.UseTemporaryFile)
        {
            TryDelete(workPath);
        }

        if (opt.DeleteOnFailure)
        {
            TryDelete(resumeStatePath);
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
            DebugLog.Info("ParallelDownloadService", $"[ParallelDownload] Failed to delete {path}: {ex.Message}");
        }
    }
}

public sealed record ParallelDownloadItem(
    string Url,
    string DestinationPath,
    ParallelDownloadOptions? Options = null);

public sealed record ParallelDownloadResult(string Url, string? Path, Exception? Error)
{
    public bool IsSuccess => Error == null && Path != null;
}