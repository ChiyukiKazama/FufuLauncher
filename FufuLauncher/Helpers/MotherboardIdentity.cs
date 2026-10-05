using System.Security.Cryptography;
using System.Text;

namespace FufuLauncher.Helpers;

public static class MotherboardIdentity
{
    private static readonly HashSet<string> PlaceholderSerials = new(StringComparer.OrdinalIgnoreCase)
    {
        "To Be Filled By O.E.M.", "To Be Filled By OEM", "System Serial Number",
        "Base Board Serial Number", "Default string", "None", "N/A", "Unknown",
        "Not Specified", "Not Available", "OEM", "123456789", "0123456789"
    };

    public static string FromSerialNumbers(IEnumerable<string?> serialNumbers)
    {
        var serials = serialNumbers.Select(value => value?.Trim().ToUpperInvariant() ?? string.Empty)
            .Where(value => value.Length > 0 && !PlaceholderSerials.Contains(value) &&
                !value.All(c => c == '0') && !value.All(c => c == 'F'))
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (serials.Length == 0) return string.Empty;

        var input = Encoding.UTF8.GetBytes("FufuLauncher:Motherboard:v1\n" + string.Join("\n", serials));
        return "MB1-" + Convert.ToHexString(SHA256.HashData(input));
    }
}
