using Microsoft.Win32;
using Ghostlist.Core;

namespace Ghostlist.App;

public sealed class ExportCommands(
    CleanupService service,
    Func<IReadOnlyList<Finding>> findings,
    Func<DiagnosticsInput> diagnostics,
    Func<string, string, IReadOnlyList<string>?, bool, Task<bool>> prompt,
    Action<string> report)
{
    public string DefaultReportName => $"ghostlist-report-{DateTime.Now:yyyyMMdd-HHmm}";

    public string DefaultBundleName => $"ghostlist-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}";

    public async Task ExportReportAsync()
    {
        var rows = ScanReport.Rows(service, findings());
        if (rows.Count == 0)
        {
            await prompt(
                Strings.Current.Get("dialog.export.emptyTitle"),
                Strings.Current.Get("dialog.export.emptyBody"), null, false);
            return;
        }

        var picker = new SaveFileDialog
        {
            Title = Strings.Current.Get("dialog.export.title"),
            FileName = $"{DefaultReportName}.csv",
            DefaultExt = ".csv",
            Filter = $"{Strings.Current.Get("dialog.export.csv")} (*.csv)|*.csv|"
                   + $"{Strings.Current.Get("dialog.export.json")} (*.json)|*.json"
        };
        if (picker.ShowDialog() != true) return;

        await Task.Run(() => ScanReport.Write(picker.FileName, rows));
        report(Strings.Current.Format("status.exported", ("count", rows.Count), ("path", picker.FileName)));
    }

    public async Task ExportDiagnosticsAsync()
    {
        var accepted = await prompt(
            Strings.Current.Get("dialog.diagnostics.title"),
            Strings.Current.Get("dialog.diagnostics.body"),
            [
                Strings.Current.Get("dialog.diagnostics.contents"),
                Strings.Current.Get("dialog.diagnostics.masked"),
                Strings.Current.Get("dialog.diagnostics.offline")
            ],
            true);
        if (!accepted) return;

        var picker = new SaveFileDialog
        {
            Title = Strings.Current.Get("dialog.diagnostics.title"),
            FileName = $"{DefaultBundleName}.zip",
            DefaultExt = ".zip",
            Filter = $"{Strings.Current.Get("dialog.diagnostics.filter")} (*.zip)|*.zip"
        };
        if (picker.ShowDialog() != true) return;

        var input = diagnostics();
        await Task.Run(() => DiagnosticsBundle.Write(picker.FileName, input));
        report(Strings.Current.Format("status.diagnosticsWritten", ("path", picker.FileName)));
    }
}
