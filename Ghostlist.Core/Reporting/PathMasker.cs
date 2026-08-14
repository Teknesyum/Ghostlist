using System.Text.RegularExpressions;

namespace Ghostlist.Core;

public static partial class PathMasker
{
    public const string UserPlaceholder = "<user>";
    public const string MachinePlaceholder = "<machine>";
    public const string SidPlaceholder = "<sid>";

    public static string Mask(string? text) => Mask(text, Environment.UserName, Environment.MachineName);

    public static string Mask(string? text, string userName, string machineName)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var masked = UsersFolder().Replace(text, $"$1{UserPlaceholder}$3");
        masked = Sid().Replace(masked, SidPlaceholder);
        if (!string.IsNullOrWhiteSpace(userName))
            masked = Word(userName).Replace(masked, UserPlaceholder);
        if (!string.IsNullOrWhiteSpace(machineName))
            masked = Word(machineName).Replace(masked, MachinePlaceholder);
        return masked;
    }

    private static Regex Word(string value) =>
        new($@"(?<![A-Za-z0-9_]){Regex.Escape(value)}(?![A-Za-z0-9_])", RegexOptions.IgnoreCase);

    [GeneratedRegex(@"([A-Za-z]:\\Users\\|\\Users\\|/Users/)([^\\/:*?""<>|\r\n]+)([\\/]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex UsersFolder();

    [GeneratedRegex(@"S-1-[0-9]+(-[0-9]+)+", RegexOptions.IgnoreCase)]
    private static partial Regex Sid();
}
