using Microsoft.Win32;
using Ghostlist.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Navigation;

namespace Ghostlist.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Finding> findings = [];
    private readonly ICollectionView view;
    private readonly CleanupService service;

    public MainWindow()
    {
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1600, workArea.Width * 0.92);
        Height = Math.Min(1000, workArea.Height * 0.92);
        MinWidth = Math.Min(1280, workArea.Width);
        MinHeight = Math.Min(820, workArea.Height);
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        FooterVersion.Text = $"Sürüm: v{version?.Major ?? 1}.{version?.Minor ?? 0}.{version?.Build ?? 0}";
        BackupPaths.MigrateLegacyBackups();
        service = CleanupService.CreateDefault(BackupPaths.BackupDirectory);
        view = CollectionViewSource.GetDefaultView(findings);
        view.Filter = item => BrokenOnlyCheckBox.IsChecked != true || ((Finding)item).Status == EntryStatus.Broken;
        EntriesGrid.ItemsSource = view;
        Loaded += async (_, _) => await ScanAsync();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (MaximizeButton is null) return;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Geri yükle" : "Büyüt";
    }

    private void FooterLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        e.Handled = true;
    }

    private async Task ScanAsync()
    {
        SetBusy(true, "Artıklar taranıyor…");
        try
        {
            var found = await Task.Run(service.Scan);
            findings.Clear();
            foreach (var item in found) findings.Add(item);
            view.Refresh();
            StatusText.Text = $"{findings.Count} kayıt incelendi; {findings.Count(x => x.Status == EntryStatus.Broken)} bozuk bulgu tespit edildi.";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();
    private void FilterChanged(object sender, RoutedEventArgs e) => view?.Refresh();

    private void Info_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Ghostlist, Windows'ta kaldırma sonrası geride kalan hayalet artıkları kanıtlayarak tespit eder.\n\n" +
            "Her bulgu için hangi kanıtların bulunduğu ve güven yüzdesi gösterilir. Düzeltme öncesi tam yedek alınır, " +
            "program klasörleri ve kişisel dosyalar silinmez.",
            "Ghostlist hakkında",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void EntriesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (EntriesGrid.SelectedItem is not Finding item) return;
        DetailName.Text = item.Title;
        DetailReason.Text = $"{StatusPresenter.Describe(item.Status)} · güven %{item.Confidence} · {service.CategoryOf(item)}";
        DetailTarget.Text = item.Evidence.Count == 0
            ? "Kanıt: —"
            : "Kanıt: " + string.Join("  •  ", item.Evidence.Select(x => $"{x.Kind} ({x.Detail})"));
        DetailRegistry.Text = item.Subtitle ?? string.Empty;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in view.Cast<Finding>()) item.IsSelected = true;
        EntriesGrid.Items.Refresh();
        StatusText.Text = $"{view.Cast<Finding>().Count()} görünür bulgu seçildi.";
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in findings) item.IsSelected = false;
        EntriesGrid.Items.Refresh();
        StatusText.Text = "Seçim temizlendi.";
    }

    private async void FixSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = findings.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Önce listeden en az bir bulgu seçin.", "Seçim gerekli", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var eligible = selected.Where(x => x.Status == EntryStatus.Broken).ToList();
        if (eligible.Count == 0)
        {
            MessageBox.Show("Seçilen bulgular arasında güvenle düzeltilebilecek kayıt yok. Sistem bileşenleri, canlı MSI paketleri ve şüpheli bulgular korunur.", "Düzeltilecek bulgu yok", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await FixFindingsAsync(eligible, $"{eligible.Count} seçili bulgu", selected.Count - eligible.Count);
    }

    private async void FixAll_Click(object sender, RoutedEventArgs e)
    {
        var automatic = service.AutoFixable(findings);
        if (automatic.Count == 0)
        {
            MessageBox.Show($"Otomatik düzeltme eşiğini (güven %{ConfidenceRules.AutoFixThreshold} ve en az {ConfidenceRules.MinimumIndependentEvidence} bağımsız kanıt) geçen bulgu yok.", "Ghostlist", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await FixFindingsAsync(automatic, $"Otomatik eşiği geçen {automatic.Count} bulgu", 0);
    }

    private async Task FixFindingsAsync(IReadOnlyList<Finding> targets, string description, int protectedCount)
    {
        var answer = MessageBox.Show(
            $"{description} düzeltilecek.\n\nHer bulgu ayrı ayrı yedeklenecek. Program klasörleri ve kullanıcı dosyaları silinmeyecek." +
            (protectedCount > 0 ? $"\n\n{protectedCount} uygun olmayan seçili bulgu güvenlik nedeniyle atlanacak." : string.Empty) +
            "\n\nDevam edilsin mi?",
            "Ghostlist toplu düzeltme", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        SetBusy(true, $"0/{targets.Count} bulgu düzeltiliyor…");
        var completed = 0;
        var manual = new List<string>();
        var failures = new List<string>();
        try
        {
            foreach (var item in targets)
            {
                try
                {
                    var result = await Task.Run(() => service.Fix(item));
                    if (result.Success) completed++;
                    else if (result.ManualCommand is not null) manual.Add($"{item.Title}: {result.ManualCommand}");
                    else failures.Add($"{item.Title}: {result.ResultKey}");
                    StatusText.Text = $"{completed}/{targets.Count} bulgu düzeltildi…";
                }
                catch (Exception ex) { failures.Add($"{item.Title}: {ex.Message}"); }
            }
            await ScanAsync();
            var summary = $"{completed} bulgu düzeltildi ve ayrı ayrı yedeklendi.";
            if (protectedCount > 0) summary += $"\n{protectedCount} korumalı bulgu atlandı.";
            if (manual.Count > 0) summary += $"\n\nElle çalıştırılması gereken komutlar:\n{string.Join("\n", manual.Take(6))}";
            if (failures.Count > 0) summary += $"\n\nBaşarısız işlemler:\n{string.Join("\n", failures.Take(6))}";
            MessageBox.Show(summary, failures.Count == 0 ? "İşlem tamamlandı" : "İşlem kısmen tamamlandı",
                MessageBoxButton.OK, failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var backupDir = BackupPaths.BackupDirectory;
        Directory.CreateDirectory(backupDir);
        var dialog = new OpenFileDialog { Title = "Ghostlist yedeğini seçin", Filter = "Ghostlist yedeği (*.json)|*.json", InitialDirectory = backupDir };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await Task.Run(() => service.Restore(dialog.FileName));
            MessageBox.Show("Kayıt başarıyla geri yüklendi.", "Geri yükleme", MessageBoxButton.OK, MessageBoxImage.Information);
            await ScanAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        EntriesGrid.IsEnabled = !busy;
        FixSelectedButton.IsEnabled = !busy;
        FixAllButton.IsEnabled = !busy;
        if (message is not null) StatusText.Text = message;
    }

    private static void ShowError(Exception ex) => MessageBox.Show(ex.Message, "Ghostlist hatası", MessageBoxButton.OK, MessageBoxImage.Error);
}

public sealed class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is EntryStatus status ? StatusPresenter.Describe(status) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public static class StatusPresenter
{
    public static string Describe(EntryStatus status) => status switch
    {
        EntryStatus.Healthy => "Sağlam",
        EntryStatus.Broken => "Bozuk",
        EntryStatus.Unsupported => "Desteklenmiyor",
        _ => "Şüpheli"
    };
}
