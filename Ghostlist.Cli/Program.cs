using Ghostlist.Core;

namespace Ghostlist.Cli;

public static class Program
{
    public const int ExitClean = 0;
    public const int ExitFindings = 1;
    public const int ExitError = 2;

    public static int Main(string[] args)
    {
        var plan = CommandLine.Parse(args);
        if (plan.Kind == CommandKind.Invalid)
        {
            Console.Error.WriteLine($"ghostlist: {plan.Error}");
            Console.Error.WriteLine("run 'ghostlist help' for usage");
            return ExitError;
        }
        if (plan.Kind == CommandKind.Help)
        {
            WriteUsage(Console.Out);
            return ExitClean;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var service = CleanupService.CreateDefault(BackupPaths.BackupDirectory);
            var reporter = new Reporter(Console.Out, plan.Json);
            return plan.Kind switch
            {
                CommandKind.Scan => RunScan(service, reporter, plan, cancellation.Token),
                CommandKind.Fix => RunFix(service, reporter, plan, cancellation.Token),
                CommandKind.Restore => RunRestore(service, reporter, plan),
                _ => ExitError
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("ghostlist: cancelled, nothing was changed");
            return ExitError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ghostlist: {ex.Message}");
            return ExitError;
        }
    }

    private static IReadOnlyList<Finding> Reportable(
        CleanupService service, Reporter reporter, CliPlan plan, CancellationToken token) =>
        Scanned(service, reporter, token)
            .Where(x => x.Status is EntryStatus.Broken or EntryStatus.Suspicious)
            .Where(x => x.Confidence >= plan.MinConfidence)
            .Where(x => plan.Category is null || service.CategoryOf(x) == plan.Category)
            .OrderByDescending(x => x.Confidence)
            .ToList();

    private static IReadOnlyList<Finding> Scanned(CleanupService service, Reporter reporter, CancellationToken token)
    {
        var outcome = service.Scan(token);
        foreach (var failure in outcome.Failures)
            reporter.Note($"category '{failure.Category}' failed: {failure.Message}");
        return outcome.Findings;
    }

    private static int RunScan(CleanupService service, Reporter reporter, CliPlan plan, CancellationToken token)
    {
        var findings = Reportable(service, reporter, plan, token);
        foreach (var finding in findings) reporter.Finding(finding, service.CategoryOf(finding));
        reporter.Note($"{findings.Count} findings, {findings.Count(x => x.Status == EntryStatus.Broken)} broken");
        return findings.Count == 0 ? ExitClean : ExitFindings;
    }

    private static int RunFix(CleanupService service, Reporter reporter, CliPlan plan, CancellationToken token)
    {
        var scanned = Scanned(service, reporter, token);
        var targets = plan.All
            ? service.AutoFixable(scanned).Where(x => x.Confidence >= plan.MinConfidence).ToList()
            : scanned.Where(x => x.Id == plan.Id).ToList();

        if (!plan.All && targets.Count == 0)
        {
            Console.Error.WriteLine($"ghostlist: no finding with id '{plan.Id}'");
            return ExitError;
        }
        if (targets.Count == 0)
        {
            reporter.Note("nothing clears the automatic threshold");
            return ExitClean;
        }

        if (plan.DryRun)
        {
            foreach (var finding in targets)
                reporter.Outcome(finding, service.CategoryOf(finding), new FixResult(false, "dry_run"), true);
            reporter.Note($"{targets.Count} findings would be fixed, nothing was changed");
            return ExitFindings;
        }

        if (plan.All && !plan.Yes && !Confirm(targets.Count)) return ExitError;

        if (!SystemRestore.TryCreate("Ghostlist bulk fix"))
            reporter.Note("no system restore point was created; continuing");

        var remaining = 0;
        foreach (var finding in targets)
        {
            var result = service.Fix(finding);
            reporter.Outcome(finding, service.CategoryOf(finding), result, false);
            if (!result.Success) remaining++;
        }
        return remaining == 0 ? ExitClean : ExitFindings;
    }

    private static int RunRestore(CleanupService service, Reporter reporter, CliPlan plan)
    {
        if (plan.ListBackups)
        {
            var backups = service.ListBackups();
            foreach (var backup in backups) reporter.Backup(backup);
            reporter.Note($"{backups.Count} backups");
            return ExitClean;
        }

        service.Restore(plan.BackupPath!);
        reporter.Note($"restored {plan.BackupPath}");
        return ExitClean;
    }

    private static bool Confirm(int count)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("ghostlist: fix --all needs --yes when input is not a terminal");
            return false;
        }
        Console.Write($"Fix {count} findings? Every change is backed up first. [y/N] ");
        var answer = Console.ReadLine();
        return string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("ghostlist - find and fix the records Windows leaves behind after an uninstall");
        output.WriteLine();
        output.WriteLine("  ghostlist scan [--category <name>] [--json] [--min-confidence <n>]");
        output.WriteLine("  ghostlist fix --id <finding-id> [--dry-run] [--json]");
        output.WriteLine($"  ghostlist fix --all [--min-confidence {CommandLine.DefaultFixConfidence}] [--dry-run] [--yes] [--json]");
        output.WriteLine("  ghostlist restore --list [--json]");
        output.WriteLine("  ghostlist restore --backup <path>");
        output.WriteLine();
        output.WriteLine("categories: uninstall, shortcut, startup, task, folder, msix");
        output.WriteLine("exit codes: 0 clean, 1 findings remain, 2 error");
        output.WriteLine();
        output.WriteLine("fix --all only touches findings that clear the automatic threshold;");
        output.WriteLine("leftover folders and MSIX packages never take part in it.");
        output.WriteLine();
        output.WriteLine("Ctrl+C cancels a scan and exits with code 2; a fix already under way is");
        output.WriteLine("never cancelled, because stopping between backup and removal is worse.");
    }
}
