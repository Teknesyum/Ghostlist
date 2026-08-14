using System.Diagnostics;
using System.Windows;

namespace Ghostlist.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var missing = Strings.Current.MissingKeys();
        if (missing.Count == 0) return;
        var report = $"Dil tablosu tutarsız: {string.Join(", ", missing)}";
        Debug.WriteLine(report);
#if DEBUG
        throw new InvalidOperationException(report);
#endif
    }
}
