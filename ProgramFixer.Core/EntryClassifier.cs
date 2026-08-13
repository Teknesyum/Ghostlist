namespace ProgramFixer.Core;

public interface IFileSystem { bool FileExists(string path); }
public sealed class PhysicalFileSystem : IFileSystem { public bool FileExists(string path) => File.Exists(path); }

public sealed class EntryClassifier(IFileSystem fileSystem)
{
    public UninstallEntry Classify(UninstallEntry entry)
    {
        if (entry.SystemComponent)
            return entry with { Status = EntryStatus.Unsupported, Reason = "Windows sistem bileşeni; güvenlik nedeniyle temizlenmez." };

        if (entry.WindowsInstaller || IsMsiCommand(entry.UninstallString))
            return entry with { Status = EntryStatus.Unsupported, Reason = "MSI paketi; Windows Installer tarafından yönetiliyor." };

        var target = UninstallCommandParser.ResolveExecutable(entry.UninstallString);
        if (target is null)
            return entry with { Status = EntryStatus.Suspicious, Reason = "Kaldırma komutu eksik veya güvenle çözümlenemedi." };

        if (fileSystem.FileExists(target))
            return entry with { Status = EntryStatus.Healthy, Reason = "Kaldırıcı dosyası mevcut.", ResolvedTarget = target };

        return entry with { Status = EntryStatus.Broken, Reason = "Kaldırıcı dosyası bulunamadı.", ResolvedTarget = target };
    }

    private static bool IsMsiCommand(string? command) =>
        command?.Contains("msiexec", StringComparison.OrdinalIgnoreCase) == true;
}

