# Ghostlist.App

WPF arayüzü. Neon tema, kategori gruplu bulgu listesi, kanıt paneli, TR/EN dil geçişi.

## Dosyalar
- `App.xaml` — neon token sözlüğü (renk, tipografi, buton, kaydırma çubuğu).
- `MainWindow.xaml` — özel başlık çubuğu, gruplu `ListBox`, kanıt paneli, diyalog katmanı.
- `MainWindow.xaml.cs` — yalnız pencere düğmeleri ve köprüler. **İş mantığı girmez.**
- `ViewModels/` — `MainViewModel` tarama/düzeltme/yedek/dil, `FindingViewModel` bulgu sunumu.
- `Localization/{tr,en}.json` — görünen tüm metin. İki dosya aynı anahtar kümesi.
- `Localization/Strings.cs` — yükleyici; `MissingKeys()` açılışta doğrulanır.
- `Settings/AppSettings.cs` — `%LOCALAPPDATA%\Ghostlist\settings.json`.
## Kurallar
- XAML'de sabit kullanıcı metni yok; `{local:Loc anahtar}` kullan.
- Yeni anahtar iki dile birden eklenir, yoksa DEBUG'da açılış patlar.
- `MessageBox.Show` kullanma; pencere içi diyalog var.
- Ölçü ve renk `App.xaml` tokenlarından; teknesyum-ui dışına çıkma.
## Sınırlar
Girdi: `CleanupService` bulguları · Çıktı: kullanıcı eylemleri, `settings.json`
