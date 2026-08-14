namespace Ghostlist.Core;

public interface IEnvironmentPaths
{
    IReadOnlyList<string> ShortcutDirectories { get; }
    IReadOnlyList<string> StartupDirectories { get; }
    IReadOnlyList<string> ProgramDirectories { get; }
    string ScheduledTaskRoot { get; }
}

public sealed class WindowsEnvironmentPaths : IEnvironmentPaths
{
    public IReadOnlyList<string> ShortcutDirectories =>
    [
        Folder(Environment.SpecialFolder.StartMenu),
        Folder(Environment.SpecialFolder.CommonStartMenu),
        Folder(Environment.SpecialFolder.Desktop),
        Folder(Environment.SpecialFolder.CommonDesktopDirectory)
    ];

    public IReadOnlyList<string> StartupDirectories =>
    [
        Folder(Environment.SpecialFolder.Startup),
        Folder(Environment.SpecialFolder.CommonStartup)
    ];

    public IReadOnlyList<string> ProgramDirectories =>
    [
        Folder(Environment.SpecialFolder.ProgramFiles),
        Folder(Environment.SpecialFolder.ProgramFilesX86),
        Path.Combine(Folder(Environment.SpecialFolder.LocalApplicationData), "Programs")
    ];

    public string ScheduledTaskRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");

    private static string Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);
}
