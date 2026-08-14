namespace Ghostlist.Core;

public static class BackupPaths
{
    public static string BackupDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ghostlist", "Backups");

    public static void MigrateLegacyBackups()
    {
        try
        {
            var current = BackupDirectory;
            if (Directory.Exists(current)) return;
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProgramFixer", "Backups");
            if (!Directory.Exists(legacy)) return;
            try
            {
                Directory.Move(legacy, current);
            }
            catch
            {
                Directory.CreateDirectory(current);
                foreach (var file in Directory.GetFiles(legacy))
                    File.Copy(file, Path.Combine(current, Path.GetFileName(file)), overwrite: true);
            }
        }
        catch
        {
            // Yedek göçü kullanıcıyı engellemez, hata sessizce yutulur.
        }
    }
}
