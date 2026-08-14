using System.Text;

namespace Ghostlist.Core;

public static class PackedGuid
{
    private static readonly int[] ReversedGroupLengths = [8, 4, 4];
    private static readonly int[] SwappedGroupLengths = [4, 12];

    public static string? Pack(string? productCode)
    {
        if (!Guid.TryParse(productCode, out var guid)) return null;
        var digits = guid.ToString("D").Replace("-", string.Empty).ToUpperInvariant();
        var builder = new StringBuilder(32);
        var offset = 0;
        foreach (var length in ReversedGroupLengths)
        {
            for (var i = length - 1; i >= 0; i--) builder.Append(digits[offset + i]);
            offset += length;
        }
        foreach (var length in SwappedGroupLengths)
        {
            for (var i = 0; i < length; i += 2)
            {
                builder.Append(digits[offset + i + 1]);
                builder.Append(digits[offset + i]);
            }
            offset += length;
        }
        return builder.ToString();
    }

    public static string? Unpack(string? packed)
    {
        if (packed is null || packed.Length != 32 || !packed.All(Uri.IsHexDigit)) return null;
        var builder = new StringBuilder(32);
        var offset = 0;
        foreach (var length in ReversedGroupLengths)
        {
            for (var i = length - 1; i >= 0; i--) builder.Append(packed[offset + i]);
            offset += length;
        }
        foreach (var length in SwappedGroupLengths)
        {
            for (var i = 0; i < length; i += 2)
            {
                builder.Append(packed[offset + i + 1]);
                builder.Append(packed[offset + i]);
            }
            offset += length;
        }
        var digits = builder.ToString();
        return $"{{{digits[..8]}-{digits[8..12]}-{digits[12..16]}-{digits[16..20]}-{digits[20..]}}}".ToUpperInvariant();
    }
}
