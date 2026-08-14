using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ghostlist.Core;

public static partial class UninstallCommandParser
{
    private const int MaxWrapperDepth = 4;

    [GeneratedRegex("^\\s*(?<path>.+?\\.(?:exe|com|bat|cmd|msi))(?=\\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecutablePathRegex();

    [GeneratedRegex("\\{?(?<guid>[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\\}?", RegexOptions.CultureInvariant)]
    private static partial Regex ProductCodeRegex();

    public static string? ResolveExecutable(string? command, RegistryView view = RegistryView.Registry64, string? installLocation = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var target = ExtractTarget(ExpandForView(command, view), 0);
        return target is null ? null : Resolve(target, ExpandForView(installLocation, view));
    }

    public static string? ResolveMsiProductCode(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var trimmed = command.Trim();
        var isMsi = trimmed.Contains("msiexec", StringComparison.OrdinalIgnoreCase);
        var match = ProductCodeRegex().Match(trimmed);
        if (!match.Success) return null;
        if (!isMsi && match.Length != trimmed.Length) return null;
        return $"{{{match.Groups["guid"].Value.ToUpperInvariant()}}}";
    }

    public static string ExpandForView(string? command, RegistryView view)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;
        var wow = view == RegistryView.Registry32 && Environment.Is64BitOperatingSystem;
        var result = command;
        result = ReplaceVariable(result, "ProgramW6432", ProgramFiles64());
        result = ReplaceVariable(result, "CommonProgramW6432", CommonProgramFiles64());
        result = ReplaceVariable(result, "ProgramFiles(x86)", ProgramFiles32());
        result = ReplaceVariable(result, "CommonProgramFiles(x86)", CommonProgramFiles32());
        result = ReplaceVariable(result, "ProgramFiles", wow ? ProgramFiles32() : ProgramFiles64());
        result = ReplaceVariable(result, "CommonProgramFiles", wow ? CommonProgramFiles32() : CommonProgramFiles64());
        return Environment.ExpandEnvironmentVariables(result);
    }

    private static string ProgramFiles64() =>
        Environment.GetEnvironmentVariable("ProgramW6432")
        ?? Environment.GetEnvironmentVariable("ProgramFiles")
        ?? @"C:\Program Files";

    private static string ProgramFiles32() =>
        Environment.GetEnvironmentVariable("ProgramFiles(x86)")
        ?? Environment.GetEnvironmentVariable("ProgramFiles")
        ?? @"C:\Program Files (x86)";

    private static string CommonProgramFiles64() =>
        Environment.GetEnvironmentVariable("CommonProgramW6432")
        ?? Environment.GetEnvironmentVariable("CommonProgramFiles")
        ?? Path.Combine(ProgramFiles64(), "Common Files");

    private static string CommonProgramFiles32() =>
        Environment.GetEnvironmentVariable("CommonProgramFiles(x86)")
        ?? Environment.GetEnvironmentVariable("CommonProgramFiles")
        ?? Path.Combine(ProgramFiles32(), "Common Files");

    private static string ReplaceVariable(string value, string name, string replacement) =>
        Regex.Replace(value, $"%{Regex.Escape(name)}%", replacement.Replace("$", "$$"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? ExtractTarget(string command, int depth)
    {
        if (depth >= MaxWrapperDepth) return null;
        var trimmed = command.Trim();
        if (trimmed.Length == 0) return null;

        var tokens = Tokenize(trimmed);
        var headIndex = NextMeaningful(tokens, -1);
        if (headIndex < 0) return null;

        var head = tokens[headIndex];
        switch (HeadName(head.Value))
        {
            case "msiexec":
                return null;

            case "cmd":
                return Continue(trimmed, tokens, IndexOfSwitch(tokens, headIndex, "/c", "/k"), depth);

            case "start":
                return Continue(trimmed, tokens, headIndex, depth);

            case "powershell":
            case "pwsh":
            {
                var index = IndexOfSwitch(tokens, headIndex, "-c", "-command", "-file");
                return Continue(trimmed, tokens, index, depth, invocation: true);
            }

            case "rundll32":
            {
                var index = NextMeaningful(tokens, headIndex);
                return index < 0 ? null : SplitEntryPoint(tokens[index].Value);
            }

            default:
            {
                if (head.Quoted) return head.Value;
                var match = ExecutablePathRegex().Match(trimmed);
                return match.Success ? match.Groups["path"].Value : null;
            }
        }
    }

    private static string? Continue(string command, List<CommandToken> tokens, int afterIndex, int depth, bool invocation = false)
    {
        if (afterIndex < 0) return null;
        var next = NextMeaningful(tokens, afterIndex);
        if (next < 0) return null;
        var rest = Unwrap(command[tokens[next].Start..]);
        return ExtractTarget(invocation ? StripInvocationOperator(rest) : rest, depth + 1);
    }

    private static int NextMeaningful(List<CommandToken> tokens, int afterIndex)
    {
        for (var i = afterIndex + 1; i < tokens.Count; i++)
            if (tokens[i].Value.Length > 0) return i;
        return -1;
    }

    private static string HeadName(string value)
    {
        var name = value.Trim().Trim('"');
        try { name = Path.GetFileName(name); } catch { }
        var dot = name.LastIndexOf('.');
        if (dot > 0) name = name[..dot];
        return name.ToLowerInvariant();
    }

    private static int IndexOfSwitch(List<CommandToken> tokens, int afterIndex, params string[] names)
    {
        for (var i = afterIndex + 1; i < tokens.Count; i++)
            if (names.Any(name => string.Equals(tokens[i].Value, name, StringComparison.OrdinalIgnoreCase)))
                return i;
        return -1;
    }

    private static string SplitEntryPoint(string value)
    {
        var comma = value.IndexOf(',');
        return comma < 0 ? value : value[..comma];
    }

    private static string StripInvocationOperator(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 1 && trimmed[0] == '"' && trimmed[^1] == '"' && !trimmed[1..^1].Contains('"'))
            trimmed = trimmed[1..^1].Trim();
        if (trimmed.StartsWith('&')) trimmed = trimmed[1..].Trim();
        if (!trimmed.StartsWith('\'')) return trimmed;
        var end = trimmed.IndexOf('\'', 1);
        return end < 0 ? trimmed[1..] : $"\"{trimmed[1..end]}\"{trimmed[(end + 1)..]}";
    }

    private static string Unwrap(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"') return trimmed;
        var inner = trimmed[1..^1];
        var quotes = inner.Count(c => c == '"');
        return quotes > 0 && quotes % 2 == 0 ? inner.Trim() : trimmed;
    }

    private static string? Resolve(string value, string? installLocation)
    {
        var path = value.Trim().Trim('"');
        if (path.Length == 0) return null;
        try
        {
            if (Path.IsPathFullyQualified(path)) return Path.GetFullPath(path);
            var root = installLocation?.Trim().Trim('"');
            if (string.IsNullOrEmpty(root) || !Path.IsPathFullyQualified(root)) return null;
            return Path.GetFullPath(Path.Combine(root, path));
        }
        catch { return null; }
    }

    private static List<CommandToken> Tokenize(string command)
    {
        var tokens = new List<CommandToken>();
        var buffer = new StringBuilder();
        var quoted = false;
        var inQuotes = false;
        var start = -1;
        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                quoted = true;
                if (start < 0) start = i;
                continue;
            }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (buffer.Length > 0 || quoted) tokens.Add(new CommandToken(buffer.ToString(), quoted, start));
                buffer.Clear();
                quoted = false;
                start = -1;
                continue;
            }
            if (start < 0) start = i;
            buffer.Append(c);
        }
        if (buffer.Length > 0 || quoted) tokens.Add(new CommandToken(buffer.ToString(), quoted, start));
        return tokens;
    }

    private readonly record struct CommandToken(string Value, bool Quoted, int Start);
}
