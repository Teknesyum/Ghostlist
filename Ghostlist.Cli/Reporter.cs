using Ghostlist.Core;
using System.Text.Json;

namespace Ghostlist.Cli;

public sealed class Reporter(TextWriter output, bool json)
{
    public void Finding(Finding finding, string category)
    {
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                id = finding.Id,
                title = finding.Title,
                subtitle = finding.Subtitle,
                category,
                status = StatusName(finding.Status),
                confidence = finding.Confidence,
                evidence = finding.Evidence.Select(x => new { kind = x.Kind, detail = x.Detail, weight = x.Weight })
            }));
            return;
        }

        output.WriteLine($"[{StatusName(finding.Status),-10} {finding.Confidence,3}] {category,-9} {finding.Title}");
        if (!string.IsNullOrWhiteSpace(finding.Subtitle)) output.WriteLine($"    {finding.Subtitle}");
        foreach (var evidence in finding.Evidence)
            output.WriteLine($"    - {evidence.Kind} ({(evidence.IsConclusive ? $"weight {evidence.Weight}" : "inconclusive")}) {evidence.Detail}");
    }

    public void Outcome(Finding finding, string category, FixResult result, bool dryRun)
    {
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                id = finding.Id,
                title = finding.Title,
                category,
                dryRun,
                success = result.Success,
                result = result.ResultKey,
                backup = result.BackupPath,
                manualCommand = result.ManualCommand
            }));
            return;
        }

        var prefix = dryRun ? "would fix" : result.Success ? "fixed" : result.ResultKey;
        output.WriteLine($"{prefix}: {finding.Title} [{category}]");
        if (result.BackupPath is not null) output.WriteLine($"    backup: {result.BackupPath}");
        if (result.ManualCommand is not null) output.WriteLine($"    run this yourself: {result.ManualCommand}");
    }

    public void Backup(string path)
    {
        if (json) output.WriteLine(JsonSerializer.Serialize(new { backup = path }));
        else output.WriteLine(path);
    }

    public void Note(string message)
    {
        if (json) output.WriteLine(JsonSerializer.Serialize(new { note = message }));
        else output.WriteLine(message);
    }

    public static string StatusName(EntryStatus status) => status switch
    {
        EntryStatus.Healthy => "healthy",
        EntryStatus.Broken => "broken",
        EntryStatus.Unsupported => "unsupported",
        _ => "suspicious"
    };
}
