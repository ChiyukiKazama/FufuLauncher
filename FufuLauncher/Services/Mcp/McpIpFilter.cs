/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net;
using System.Net.Sockets;

namespace FufuLauncher.Services.Mcp;

internal sealed record McpIpRule(IPAddress Network, int PrefixLength)
{
    public bool Matches(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != Network.AddressFamily)
        {
            return false;
        }

        var addressBytes = address.GetAddressBytes();
        var networkBytes = Network.GetAddressBytes();
        var fullBytes = PrefixLength / 8;
        var remainingBits = PrefixLength % 8;

        for (var index = 0; index < fullBytes; index++)
        {
            if (addressBytes[index] != networkBytes[index])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (addressBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }
}

internal static class McpIpFilter
{
    private static readonly char[] Separators = ['\r', '\n', ',', ';', ' ', '\t'];

    public static bool TryParse(string? text, out List<McpIpRule> rules, out List<string> invalid)
    {
        rules = new List<McpIpRule>();
        invalid = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        foreach (var entry in text.Split(Separators,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseRule(entry, out var rule))
            {
                rules.Add(rule);
            }
            else
            {
                invalid.Add(entry);
            }
        }

        return invalid.Count == 0;
    }

    public static bool TryParseRule(string value, out McpIpRule rule)
    {
        rule = null!;

        var prefixLength = -1;
        var addressText = value;
        var slash = value.IndexOf('/');
        if (slash >= 0)
        {
            if (!int.TryParse(value[(slash + 1)..], out prefixLength) || prefixLength < 0)
            {
                return false;
            }

            addressText = value[..slash];
        }

        if (!IPAddress.TryParse(addressText, out var address))
        {
            return false;
        }

        var maximumPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0)
        {
            prefixLength = maximumPrefix;
        }

        if (prefixLength > maximumPrefix)
        {
            return false;
        }

        rule = new McpIpRule(address, prefixLength);
        return true;
    }

    public static string Describe(IPAddress? address)
    {
        if (address == null)
        {
            return "-";
        }

        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
    }
}