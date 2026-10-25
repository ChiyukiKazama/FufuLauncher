/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FufuLauncher.Services.Mcp;

internal sealed class McpHttpServer
{
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxBodyBytes = 4 * 1024 * 1024;
    private const int RequestTimeoutMs = 15000;

    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public int Port
    {
        get;
        private set;
    }

    public event Action<string>? Diagnostic;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _listener != null;
            }
        }
    }

    public Task StartAsync(int port, Func<McpHttpRequest, CancellationToken, Task<McpHttpResponse>> handler)
    {
        lock (_gate)
        {
            if (_listener != null)
            {
                throw new InvalidOperationException("MCP server is already running.");
            }

            var listener = new TcpListener(IPAddress.Any, port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start(128);

            _listener = listener;
            Port = port;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => AcceptLoopAsync(listener, token, handler), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        TcpListener? listener;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            listener = _listener;
            cancellation = _cts;
            _listener = null;
            _cts = null;
        }

        if (listener == null)
        {
            return;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch
        {
        }

        try
        {
            listener.Stop();
        }
        catch
        {
        }

        cancellation?.Dispose();
        await Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token,
        Func<McpHttpRequest, CancellationToken, Task<McpHttpResponse>> handler)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                Diagnostic?.Invoke($"Failed to accept connection: {ex.Message}");
                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, handler, token), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client,
        Func<McpHttpRequest, CancellationToken, Task<McpHttpResponse>> handler, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                readCancellation.CancelAfter(RequestTimeoutMs);

                using var stream = client.GetStream();
                var remoteAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                var request = await ReadRequestAsync(stream, remoteAddress, readCancellation.Token)
                    .ConfigureAwait(false);

                McpHttpResponse response;
                if (request == null)
                {
                    response = McpHttpResponse.Text(400, "Bad Request");
                }
                else
                {
                    try
                    {
                        response = await handler(request, token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Diagnostic?.Invoke($"Failed to process request: {ex.Message}");
                        response = McpHttpResponse.Json(500,
                            McpJson.Serialize(new { error = "internal_error", message = ex.Message }));
                    }
                }

                await WriteResponseAsync(stream, response, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (Exception ex)
            {
                Diagnostic?.Invoke($"Failed to handle connection: {ex.Message}");
            }
        }
    }

    private static async Task<McpHttpRequest?> ReadRequestAsync(NetworkStream stream, IPAddress? remoteAddress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var received = new MemoryStream();
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                return null;
            }

            received.Write(buffer, 0, read);
            if (received.Length > MaxHeaderBytes)
            {
                throw new IOException("Request headers are too large.");
            }

            headerEnd = FindHeaderEnd(received.GetBuffer(), (int)received.Length);
        }

        var headerText = Encoding.ASCII.GetString(received.GetBuffer(), 0, headerEnd);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0)
        {
            return null;
        }

        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2)
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        if (headers.TryGetValue("Expect", out var expect) &&
            expect.Contains("100-continue", StringComparison.OrdinalIgnoreCase))
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"), cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var target = requestLine[1];
        var questionMark = target.IndexOf('?');
        var path = questionMark < 0 ? target : target[..questionMark];
        var query = questionMark < 0 ? string.Empty : target[(questionMark + 1)..];

        if (!headers.TryGetValue("Content-Length", out var contentLengthText))
        {
            contentLengthText = "0";
        }

        if (!int.TryParse(contentLengthText, out var contentLength))
        {
            throw new IOException("Invalid Content-Length header.");
        }

        if (contentLength < 0 || contentLength > MaxBodyBytes)
        {
            throw new IOException("Request body is too large.");
        }

        var body = new byte[contentLength];
        var bodyOffset = headerEnd + 4;
        var alreadyRead = (int)received.Length - bodyOffset;
        if (alreadyRead > 0)
        {
            var copyLength = Math.Min(alreadyRead, contentLength);
            Array.Copy(received.GetBuffer(), bodyOffset, body, 0, copyLength);
        }

        var written = Math.Max(0, Math.Min(alreadyRead, contentLength));
        while (written < contentLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(written, contentLength - written), cancellationToken)
                .ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            written += read;
        }

        return new McpHttpRequest
        {
            Method = requestLine[0].ToUpperInvariant(),
            Path = path,
            RawQuery = query,
            Headers = headers,
            Body = body,
            RemoteAddress = remoteAddress
        };
    }

    private static int FindHeaderEnd(byte[] data, int length)
    {
        for (var index = 0; index + 3 < length; index++)
        {
            if (data[index] == 13 && data[index + 1] == 10 && data[index + 2] == 13 && data[index + 3] == 10)
            {
                return index;
            }
        }

        return -1;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, McpHttpResponse response,
        CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(response.Body ?? string.Empty);
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ')
            .Append(ReasonPhrase(response.StatusCode)).Append("\r\n");
        builder.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        builder.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        builder.Append("Cache-Control: no-store\r\n");
        builder.Append("Connection: close\r\n");

        foreach (var header in response.Headers)
        {
            builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        }

        builder.Append("\r\n");

        await stream.WriteAsync(Encoding.UTF8.GetBytes(builder.ToString()), cancellationToken).ConfigureAwait(false);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch
    {
        200 => "OK",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        405 => "Method Not Allowed",
        413 => "Payload Too Large",
        431 => "Request Header Fields Too Large",
        500 => "Internal Server Error",
        503 => "Service Unavailable",
        _ => "OK"
    };
}