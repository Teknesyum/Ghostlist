using Microsoft.Win32;
using ProgramFixer.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Navigation;

namespace ProgramFixer.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<UninstallEntry> entries = [];
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
        var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProgramFixer", "Backups");
        service = new CleanupService(new WindowsUninstallRepository(), new EntryClassifier(new PhysicalFileSystem()), backupDir);
        view = CollectionViewSource.GetDefaultView(entries);
        view.Filter = item => BrokenOnlyCheckBox.IsChecked != true || ((UninstallEntry)item).Status == EntryStatus.Broken;
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
        SetBusy(true, "Kaldırma kayıtları taranıyor…");
        try
        {
            var found = await Task.Run(service.Scan);
            entries.Clear();
            foreach (var item in found) entries.Add(item);
            view.Refresh();
            StatusText.Text = $"{entries.Count} uygulama incelendi; {entries.Count(x => x.Status == EntryStatus.Broken)} bozuk kayıt bulundu.";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();
    private void FilterChanged(object sender, RoutedEventArgs e) => view?.Refresh();

    private void Info_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "ProgramFixer, Windows 'Yüklü uygulamalar' listesindeki kaldırıcı dosyası kaybolmuş yetim kayıtları tespit eder.\n\n" +
            "Düzeltme sırasında yalnızca doğrulanmış bozuk kayıt kaldırılır ve önce geri yüklenebilir bir yedek oluşturulur. " +
            "Program klasörleri ile kişisel dosyalarınız silinmez.",
            "ProgramFixer hakkında",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void EntriesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (EntriesGrid.SelectedItem is not UninstallEntry item) return;
        DetailName.Text = item.DisplayName;
        DetailReason.Text = $"{item.StatusText}: {item.Reason}";
        DetailTarget.Text = item.ResolvedTarget is null ? "Kaldırıcı hedefi: —" : $"Kaldırıcı hedefi: {item.ResolvedTarget}";
        DetailRegistry.Text = $"Kayıt: {item.Location.DisplayPath}";
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in view.Cast<UninstallEntry>()) item.IsSelected = true;
        EntriesGrid.Items.Refresh();
        StatusText.Text = $"{view.Cast<UninstallEntry>().Count()} görünür kayıt seçildi.";
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in entries) item.IsSelected = false;
        EntriesGrid.Items.Refresh();
        StatusText.Text = "Seçim temizlendi.";
    }

    private async void FixSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = entries.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Önce listeden en az bir kayıt seçin.", "Seçim gerekli", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var eligible = selected.Where(x => x.Status == EntryStatus.Broken).ToList();
        var protectedCount = selected.Count - eligible.Count;
        if (eligible.Count == 0)
        {
            MessageBox.Show("Seçilen kayıtlar arasında güvenle düzeltilebilecek bozuk kayıt yok. MSI, sistem bileşeni, sağlam ve şüpheli kayıtlar korunur.", "Düzeltilecek kayıt yok", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await FixEntriesAsync(eligible, $"{eligible.Count} seçili bozuk kayıt", protectedCount);
    }

    private async void FixAll_Click(object sender, RoutedEventArgs e)
    {
        var broken = entries.Where(x => x.Status == EntryStatus.Broken).ToList();
        if (broken.Count == 0)
        {
            MessageBox.Show("Düzeltilebilecek bozuk kayıt bulunmuyor.", "ProgramFixer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await FixEntriesAsync(broken, $"Tespit edilen {broken.Count} bozuk kaydın tamamı", 0);
    }

    private async Task FixEntriesAsync(IReadOnlyList<UninstallEntry> targets, string description, int protectedCount)
    {
        var answer = MessageBox.Show(
            $"{description} düzeltilecek.\n\nHer kayıt ayrı ayrı yedeklenecek ve yalnızca Windows 'Yüklü uygulamalar' girdileri kaldırılacak. Program klasörleri ve kullanıcı dosyaları silinmeyecek." +
            (protectedCount > 0 ? $"\n\n{protectedCount} uygun olmayan seçili kayıt güvenlik nedeniyle atlanacak." : string.Empty) +
            "\n\nDevam edilsin mi?",
            "ProgramFixer toplu düzeltme", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        SetBusy(true, $"0/{targets.Count} kayıt düzeltiliyor…");
        var completed = 0;
        var failures = new List<string>();
        try
        {
            foreach (var item in targets)
            {
                try
                {
                    await Task.Run(() => service.RemoveBrokenEntry(item));
                    completed++;
                    StatusText.Text = $"{completed}/{targets.Count} kayıt düzeltildi…";
                }
                catch (Exception ex) { failures.Add($"{item.DisplayName}: {ex.Message}"); }
            }
            await ScanAsync();
            var summary = $"{completed} kayıt düzeltildi ve ayrı ayrı yedeklendi.";
            if (protectedCount > 0) summary += $"\n{protectedCount} korumalı kayıt atlandı.";
            if (failures.Count > 0) summary += $"\n\nBaşarısız işlemler:\n{string.Join("\n", failures.Take(6))}";
            MessageBox.Show(summary, failures.Count == 0 ? "İşlem tamamlandı" : "İşlem kısmen tamamlandı",
                MessageBoxButton.OK, failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProgramFixer", "Backups");
        Directory.CreateDirectory(backupDir);
        var dialog = new OpenFileDialog { Title = "ProgramFixer yedeğini seçin", Filter = "ProgramFixer yedeği (*.json)|*.json", InitialDirectory = backupDir };
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

    private static void ShowError(Exception ex) => MessageBox.Show(ex.Message, "ProgramFixer hatası", MessageBoxButton.OK, MessageBoxImage.Error);
}
