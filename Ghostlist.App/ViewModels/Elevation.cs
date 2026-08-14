using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace Ghostlist.App;

public static class Elevation
{
    public static bool IsElevated { get; } = Detect();

    public static bool Restart()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path is null) return false;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" })?.Dispose();
            Application.Current.Shutdown();
            return true;
        }
        catch { return false; }
    }

    private static bool Detect()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
