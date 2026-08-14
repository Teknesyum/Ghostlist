using Ghostlist.Core;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Ghostlist.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel model;

    public MainWindow()
    {
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1600, workArea.Width * 0.92);
        Height = Math.Min(1000, workArea.Height * 0.92);
        MinWidth = Math.Min(1280, workArea.Width);
        MinHeight = Math.Min(860, workArea.Height);
        BackupPaths.MigrateLegacyBackups();
        model = new MainViewModel(CleanupService.CreateDefault(BackupPaths.BackupDirectory), AppSettings.Load());
        DataContext = model;
        Loaded += async (_, _) => await model.StartAsync();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (MaximizeButton is null) return;
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "❐" : "▢";
        MaximizeButton.SetBinding(ToolTipProperty,
            new System.Windows.Data.Binding($"[caption.{(maximized ? "restore" : "maximize")}]")
            {
                Source = Strings.Current,
                Mode = System.Windows.Data.BindingMode.OneWay
            });
    }

    private void DialogConfirm_Click(object sender, RoutedEventArgs e) =>
        (((Button)sender).DataContext as DialogRequest)?.Answer(true);

    private void DialogCancel_Click(object sender, RoutedEventArgs e) =>
        (((Button)sender).DataContext as DialogRequest)?.Answer(false);

    private void FooterLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        e.Handled = true;
    }
}
