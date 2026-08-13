using System.Text.RegularExpressions;

namespace ProgramFixer.Core;

public static partial class UninstallCommandParser
{
    [GeneratedRegex("^\\s*\"(?<path>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedPathRegex();

    [GeneratedRegex("^\\s*(?<path>.+?\\.(?:exe|com|bat|cmd|msi))(?=\\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecutablePathRegex();

    public static string? ResolveExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        var quoted = QuotedPathRegex().Match(expanded);
        if (quoted.Success) return Normalize(quoted.Groups["path"].Value);
        var unquoted = ExecutablePathRegex().Match(expanded);
        return unquoted.Success ? Normalize(unquoted.Groups["path"].Value) : null;
    }

    private static string Normalize(string value)
    {
        var path = value.Trim().Trim('"');
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}

