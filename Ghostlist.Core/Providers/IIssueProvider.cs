namespace Ghostlist.Core;

public interface IBackupSink
{
    string SaveRegistryTree(RegistryTreeBackup backup, string label);
    string SaveRegistryValue(RegistryValueBackup backup, string label);
    string MoveFileToBackup(string sourcePath, string label);
    string MoveDirectoryToBackup(string sourcePath, string label);
    string SaveText(string content, string label, string extension);
    void Restore(string backupPath);
    IReadOnlyList<string> List();
}

public interface IIssueProvider
{
    string Id { get; }
    string Category { get; }
    IReadOnlyList<Finding> Scan(CancellationToken token = default);
    FixResult Fix(Finding finding, IBackupSink backup);
}
