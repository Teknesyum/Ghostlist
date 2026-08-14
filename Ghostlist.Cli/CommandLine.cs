using Ghostlist.Core;

namespace Ghostlist.Cli;

public enum CommandKind { Help, Scan, Fix, Restore, Invalid }

public sealed record CliPlan(
    CommandKind Kind,
    string? Category = null,
    bool Json = false,
    int MinConfidence = 0,
    string? Id = null,
    bool All = false,
    bool DryRun = false,
    bool Yes = false,
    bool ListBackups = false,
    string? BackupPath = null,
    string? Error = null)
{
    public static CliPlan Invalid(string error) => new(CommandKind.Invalid, Error: error);
}

public static class CommandLine
{
    public const int DefaultFixConfidence = ConfidenceRules.AutoFixThreshold;

    private static readonly string[] KnownCategories =
    [
        Categories.Uninstall, Categories.Shortcut, Categories.Startup,
        Categories.Task, Categories.Folder, Categories.Msix
    ];

    public static CliPlan Parse(string[] args)
    {
        if (args.Length == 0) return new CliPlan(CommandKind.Help);
        return args[0] switch
        {
            "scan" => ParseScan(args[1..]),
            "fix" => ParseFix(args[1..]),
            "restore" => ParseRestore(args[1..]),
            "help" or "--help" or "-h" => new CliPlan(CommandKind.Help),
            var other => CliPlan.Invalid($"unknown command '{other}'")
        };
    }

    private static CliPlan ParseScan(string[] args)
    {
        var plan = new CliPlan(CommandKind.Scan);
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--json":
                    plan = plan with { Json = true };
                    break;
                case "--category":
                    if (!TryValue(args, ref index, out var category)) return CliPlan.Invalid("--category needs a value");
                    if (!KnownCategories.Contains(category)) return CliPlan.Invalid($"unknown category '{category}'");
                    plan = plan with { Category = category };
                    break;
                case "--min-confidence":
                    if (!TryConfidence(args, ref index, out var minimum, out var error)) return CliPlan.Invalid(error);
                    plan = plan with { MinConfidence = minimum };
                    break;
                default:
                    return CliPlan.Invalid($"unknown option '{args[index]}'");
            }
        }
        return plan;
    }

    private static CliPlan ParseFix(string[] args)
    {
        var plan = new CliPlan(CommandKind.Fix, MinConfidence: DefaultFixConfidence);
        var confidenceGiven = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--all":
                    plan = plan with { All = true };
                    break;
                case "--dry-run":
                    plan = plan with { DryRun = true };
                    break;
                case "--yes":
                    plan = plan with { Yes = true };
                    break;
                case "--json":
                    plan = plan with { Json = true };
                    break;
                case "--id":
                    if (!TryValue(args, ref index, out var id)) return CliPlan.Invalid("--id needs a value");
                    plan = plan with { Id = id };
                    break;
                case "--min-confidence":
                    if (!TryConfidence(args, ref index, out var minimum, out var error)) return CliPlan.Invalid(error);
                    plan = plan with { MinConfidence = minimum };
                    confidenceGiven = true;
                    break;
                default:
                    return CliPlan.Invalid($"unknown option '{args[index]}'");
            }
        }

        if (plan.All == (plan.Id is not null)) return CliPlan.Invalid("fix needs exactly one of --id or --all");
        if (confidenceGiven && !plan.All) return CliPlan.Invalid("--min-confidence only applies to fix --all");
        return plan;
    }

    private static CliPlan ParseRestore(string[] args)
    {
        var plan = new CliPlan(CommandKind.Restore);
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--list":
                    plan = plan with { ListBackups = true };
                    break;
                case "--json":
                    plan = plan with { Json = true };
                    break;
                case "--backup":
                    if (!TryValue(args, ref index, out var path)) return CliPlan.Invalid("--backup needs a value");
                    plan = plan with { BackupPath = path };
                    break;
                default:
                    return CliPlan.Invalid($"unknown option '{args[index]}'");
            }
        }

        if (plan.ListBackups == (plan.BackupPath is not null))
            return CliPlan.Invalid("restore needs exactly one of --list or --backup");
        return plan;
    }

    private static bool TryValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = string.Empty;
            return false;
        }
        value = args[++index];
        return true;
    }

    private static bool TryConfidence(string[] args, ref int index, out int value, out string error)
    {
        value = 0;
        if (!TryValue(args, ref index, out var text))
        {
            error = "--min-confidence needs a value";
            return false;
        }
        if (!int.TryParse(text, out value) || value < 0 || value > 100)
        {
            error = $"--min-confidence must be between 0 and 100, got '{text}'";
            return false;
        }
        error = string.Empty;
        return true;
    }
}
