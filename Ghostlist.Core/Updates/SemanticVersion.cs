using System.Globalization;

namespace Ghostlist.Core;

public sealed record SemanticVersion(int Major, int Minor, int Patch, string? PreRelease = null)
    : IComparable<SemanticVersion>
{
    public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

    public static SemanticVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];

        var buildIndex = value.IndexOf('+');
        if (buildIndex >= 0) value = value[..buildIndex];

        string? preRelease = null;
        var dashIndex = value.IndexOf('-');
        if (dashIndex >= 0)
        {
            preRelease = value[(dashIndex + 1)..];
            value = value[..dashIndex];
            if (preRelease.Length == 0) return null;
        }

        var parts = value.Split('.');
        if (parts.Length is < 1 or > 4) return null;

        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (i >= parts.Length) { numbers[i] = 0; continue; }
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return null;
            numbers[i] = number;
        }
        if (parts.Length == 4 && !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return null;

        return new SemanticVersion(numbers[0], numbers[1], numbers[2], preRelease);
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch) return Patch.CompareTo(other.Patch);
        if (IsPreRelease == other.IsPreRelease)
            return string.CompareOrdinal(PreRelease ?? string.Empty, other.PreRelease ?? string.Empty);
        return IsPreRelease ? -1 : 1;
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";
}
