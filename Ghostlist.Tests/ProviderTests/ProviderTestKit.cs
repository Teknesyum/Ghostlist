using Ghostlist.Core;

namespace Ghostlist.Tests.ProviderTests;

public sealed class FakeEnvironmentPaths : IEnvironmentPaths
{
    public IReadOnlyList<string> ShortcutDirectories { get; init; } = [];
    public IReadOnlyList<string> StartupDirectories { get; init; } = [];
    public IReadOnlyList<string> ProgramDirectories { get; init; } = [];
    public string ScheduledTaskRoot { get; init; } = @"C:\Windows\System32\Tasks";
}

public sealed class RecordingBackupSink : IBackupSink
{
    public List<string> MovedFiles { get; } = [];
    public List<string> MovedDirectories { get; } = [];
    public List<RegistryValueBackup> SavedValues { get; } = [];
    public List<string> SavedTexts { get; } = [];

    public string SaveRegistryTree(RegistryTreeBackup backup, string label) => $"backup:{label}";

    public string SaveRegistryValue(RegistryValueBackup backup, string label)
    {
        SavedValues.Add(backup);
        return $"value:{label}";
    }

    public string MoveFileToBackup(string sourcePath, string label)
    {
        MovedFiles.Add(sourcePath);
        return $"file:{label}";
    }

    public string MoveDirectoryToBackup(string sourcePath, string label)
    {
        MovedDirectories.Add(sourcePath);
        return $"directory:{label}";
    }

    public string SaveText(string content, string label, string extension)
    {
        SavedTexts.Add(content);
        return $"text:{label}{extension}";
    }

    public void Restore(string backupPath) => throw new NotSupportedException();
    public IReadOnlyList<string> List() => [];
}

public sealed class RecordingTaskRemover(bool succeeds = true) : ITaskRemover
{
    public List<string> Deleted { get; } = [];

    public bool Delete(string taskName)
    {
        Deleted.Add(taskName);
        return succeeds;
    }
}

public static class ShellLinkBuilder
{
    public static byte[] Build(string localBasePath, string commonPathSuffix = "")
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x4C);
        writer.Write(new byte[16]);
        writer.Write(0x02u);
        writer.Write(new byte[0x4C - 24]);

        var basePathBytes = System.Text.Encoding.Default.GetBytes(localBasePath);
        var suffixBytes = System.Text.Encoding.Default.GetBytes(commonPathSuffix);
        const int headerSize = 0x1C;
        var basePathOffset = headerSize;
        var suffixOffset = basePathOffset + basePathBytes.Length + 1;
        var totalSize = suffixOffset + suffixBytes.Length + 1;

        writer.Write(totalSize);
        writer.Write(headerSize);
        writer.Write(0x01u);
        writer.Write(0);
        writer.Write(basePathOffset);
        writer.Write(0);
        writer.Write(suffixOffset);
        writer.Write(basePathBytes);
        writer.Write((byte)0);
        writer.Write(suffixBytes);
        writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] BuildNetworkLink()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x4C);
        writer.Write(new byte[16]);
        writer.Write(0x02u);
        writer.Write(new byte[0x4C - 24]);
        writer.Write(0x1C);
        writer.Write(0x1C);
        writer.Write(0x02u);
        writer.Write(new byte[16]);
        writer.Flush();
        return stream.ToArray();
    }
}
