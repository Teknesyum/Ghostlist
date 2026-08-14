using System.Xml.Linq;

namespace Ghostlist.Core;

public sealed record ScheduledTaskIssue(string TaskName, string XmlPath, string TargetPath);

public interface ITaskRemover
{
    bool Delete(string taskName);
}

public sealed class SchtasksRemover : ITaskRemover
{
    public bool Delete(string taskName)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("schtasks.exe")
            {
                ArgumentList = { "/Delete", "/TN", taskName, "/F" },
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch { return false; }
    }
}

public sealed class ScheduledTaskProvider(IEnvironmentPaths paths, IFileSystem fileSystem, ITaskRemover remover) : IIssueProvider
{
    private const string MicrosoftBranch = @"\Microsoft\";

    public string Id => Categories.Task;
    public string Category => Categories.Task;

    public IReadOnlyList<Finding> Scan(CancellationToken token = default)
    {
        var root = paths.ScheduledTaskRoot;
        var files = fileSystem.TryListFiles(root, "*", recursive: true);
        if (files is null) return [];

        var findings = new List<Finding>();
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var taskName = TaskName(root, file);
            if (taskName.StartsWith(MicrosoftBranch, StringComparison.OrdinalIgnoreCase)) continue;
            var document = ReadTask(file);
            if (document is null || IsMicrosoftAuthored(document)) continue;
            var command = Command(document);
            if (command is null) continue;
            var target = UninstallCommandParser.ResolveExecutable(command);
            if (target is null || fileSystem.ProbeFile(target) != ProbeResult.Missing) continue;

            var evidence = new List<Evidence>
            {
                new(EvidenceKinds.TaskTargetMissing, target, EvidenceWeights.TaskTargetMissing)
            };
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory) && fileSystem.ProbeDirectory(directory) == ProbeResult.Missing)
                evidence.Add(new Evidence(EvidenceKinds.TargetDirectoryMissing, directory, EvidenceWeights.TargetDirectoryMissing));

            var (status, confidence) = ConfidenceRules.Evaluate(evidence);
            findings.Add(new Finding(
                $"task:{taskName}", taskName, target, status, confidence, evidence, Id,
                new ScheduledTaskIssue(taskName, file, target)));
        }
        return findings;
    }

    public FixResult Fix(Finding finding, IBackupSink backup)
    {
        if (finding.Payload is not ScheduledTaskIssue issue) return FixResult.PayloadMismatch();
        if (finding.Status != EntryStatus.Broken) return FixResult.NotEligible();

        var xml = fileSystem.TryReadText(issue.XmlPath);
        if (xml is null) return FixResult.NotEligible();
        var path = backup.SaveText(xml, SafeLabel(issue.TaskName), ".task.xml");
        return remover.Delete(issue.TaskName) ? FixResult.Fixed(path) : new FixResult(false, FixResultKeys.Failed, path);
    }

    private XDocument? ReadTask(string path)
    {
        var content = fileSystem.TryReadText(path);
        if (content is null || !content.Contains("<Task", StringComparison.OrdinalIgnoreCase)) return null;
        try { return XDocument.Parse(content); }
        catch { return null; }
    }

    private static string? Command(XDocument document)
    {
        var command = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "Command");
        return string.IsNullOrWhiteSpace(command?.Value) ? null : command.Value.Trim();
    }

    private static bool IsMicrosoftAuthored(XDocument document)
    {
        var author = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "Author")?.Value.Trim();
        if (string.IsNullOrEmpty(author)) return false;
        return author.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
            || author.StartsWith("$(@%SystemRoot%", StringComparison.OrdinalIgnoreCase)
            || author.StartsWith("$(@%systemroot%", StringComparison.OrdinalIgnoreCase);
    }

    private static string TaskName(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        return $@"\{relative.Replace(Path.DirectorySeparatorChar, '\\')}";
    }

    private static string SafeLabel(string taskName) => taskName.Trim('\\').Replace('\\', '-');
}
