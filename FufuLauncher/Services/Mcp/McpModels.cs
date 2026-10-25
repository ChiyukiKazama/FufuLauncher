/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FufuLauncher.Services.Mcp;

internal sealed class McpHttpRequest
{
    public string Method
    {
        get;
        init;
    } = "GET";

    public string Path
    {
        get;
        init;
    } = "/";

    public string RawQuery
    {
        get;
        init;
    } = string.Empty;

    public IReadOnlyDictionary<string, string> Headers
    {
        get;
        init;
    } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public byte[] Body
    {
        get;
        init;
    } = Array.Empty<byte>();

    public IPAddress? RemoteAddress
    {
        get;
        init;
    }

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    public string? QueryValue(string name)
    {
        if (string.IsNullOrEmpty(RawQuery))
        {
            return null;
        }

        foreach (var part in RawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var key = separator < 0 ? part : part[..separator];
            if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = separator < 0 ? string.Empty : part[(separator + 1)..];
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return null;
    }
}

internal sealed class McpHttpResponse
{
    private static readonly IReadOnlyDictionary<string, string> NoExtraHeaders = new Dictionary<string, string>();

    public int StatusCode
    {
        get;
        init;
    } = 200;

    public string ContentType
    {
        get;
        init;
    } = McpJson.ContentType;

    public string Body
    {
        get;
        init;
    } = string.Empty;

    public IReadOnlyDictionary<string, string> Headers
    {
        get;
        init;
    } = NoExtraHeaders;

    public static McpHttpResponse Json(int statusCode, string body,
        IReadOnlyDictionary<string, string>? headers = null) =>
        new() { StatusCode = statusCode, Body = body, Headers = headers ?? NoExtraHeaders };

    public static McpHttpResponse Text(int statusCode, string text) =>
        new() { StatusCode = statusCode, ContentType = "text/plain; charset=utf-8", Body = text };

    public static McpHttpResponse Empty(int statusCode,
        IReadOnlyDictionary<string, string>? headers = null) =>
        new() { StatusCode = statusCode, Body = string.Empty, Headers = headers ?? NoExtraHeaders };

    public static McpHttpResponse EventStream(int statusCode, string body,
        IReadOnlyDictionary<string, string>? headers = null) =>
        new()
        {
            StatusCode = statusCode,
            ContentType = "text/event-stream; charset=utf-8",
            Body = body,
            Headers = headers ?? NoExtraHeaders
        };
}

internal static class McpJson
{
    public const string ContentType = "application/json; charset=utf-8";

    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(object? value) => JsonSerializer.Serialize(value, Options);

    public static JsonElement ToElement(object? value) => JsonSerializer.SerializeToElement(value, Options);
}

public sealed record McpToolResult(string Text, bool IsError = false)
{
    public static McpToolResult Success(object payload) => new(McpJson.Serialize(payload));
}

internal sealed class McpToolException : Exception
{
    public McpToolException(string message) : base(message)
    {
    }
}

internal sealed record McpToolDefinition(
    string Id,
    string Category,
    string TitleKey,
    string DescriptionKey,
    string Description,
    string InputSchemaJson);