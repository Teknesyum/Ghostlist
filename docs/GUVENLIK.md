# Güvenlik modeli

Bu belge proje içi çalışma dokümanıdır; Ghostlist'in neyi neden silmediğini, bir bulgunun
nasıl "bozuk" sayıldığını ve bir işlemin nasıl geri alındığını anlatır. Dışa açılan özet
README'dedir.

## 1. Temel ilke: kanıtlanmamış bulgu düzeltilmez

Bir bulgu tek bir "var / yok" kontrolü değil, ağırlıklı gözlemler listesidir. Her gözlem bir
`Evidence(Kind, Detail, Weight)` kaydıdır; `Kind` dilden bağımsız bir anahtardır ve arayüzde
çeviri tablosundan okunur. Güven yüzdesi bu ağırlıkların toplamıdır ve arayüzde her bulgunun
altında madde madde gösterilir. Kullanıcı kararın gerekçesini görmeden düzeltme yapamaz.

Eşikler (`Ghostlist.Core/Models.cs`, `ConfidenceRules`):

| Eşik | Değer | Anlamı |
| --- | --- | --- |
| `BrokenThreshold` | 70 | Bu değerin altındaki bulgu "bozuk" sayılmaz |
| `SuspiciousThreshold` | 20 | Şüpheli bulguların taban puanı |
| `AutoFixThreshold` | 90 | Toplu düzeltmeye girebilmek için gereken güven |
| `MinimumIndependentEvidence` | 2 | Toplu düzeltme için gereken bağımsız kanıt sayısı |
| `UncertainCeiling` | 60 | Belirsiz kanıt varsa güvenin çıkabileceği tavan |
| `LeftoverFolderCeiling` | 80 | Artık klasör bulgularının çıkabileceği tavan |

## 2. Okunamayan yer "yok" değildir

Dosya ve klasör sondaları üç durumludur: `Present`, `Missing`, `Unknown`. Erişim reddedilirse
ya da G/Ç hatası olursa sonuç `Unknown`'dır ve ağırlığı **0** olan bir kanıt üretir. Böyle bir
kanıt varsa güven 60'ta tavanlanır — yani `BrokenThreshold`'un altında kalır ve bulgu
matematiksel olarak "bozuk" olamaz. `%WINDIR%\Installer` okunamadığında MSI önbelleği
"yok" değil "belirsiz" sayılır.

Bağımsızlık gerçek olsun diye aynı yolu gösteren kanıtlar tekilleştirilir: `DisplayIcon` ile
kaldırıcı hedefi aynı dosyaysa ya da `InstallLocation` ile hedef klasör aynıysa tek kanıt sayılır.

## 3. Asla yapılmayanlar

- **Kaldırma komutu çalıştırılmaz.** Komut yalnızca çözümlenir; hedef dosyanın durumu okunur.
- **Kullanıcı verisine dokunulmaz.** `Documents`, `Saved Games`, `%APPDATA%` ve
  `%LOCALAPPDATA%` veri klasörleri taranmaz. Artık klasör taraması yalnızca `Program Files`,
  `Program Files (x86)` ve `%LOCALAPPDATA%\Programs` altındaki **birinci seviye** klasörlerdir.
- **MSIX paketi kaldırılmaz.** `Remove-AppxPackage` komutu kullanıcıya gösterilir, çalıştırılmaz.
- **Sistem bileşenleri korunur.** `SystemComponent` işaretli kayıt `Unsupported` olarak
  sınıflanır ve hiçbir düzeltmeye girmez.
- **Çözülemeyen komut korunur.** Ayrıştırılamayan bir kaldırma komutu bulguyu bozuk yapmaz.
- **Yedeksiz silme yoktur.** Registry ağacı, Registry değeri, görev XML'i ve dosya —
  hepsi önce yedeklenir.
- **Düzeltme iptal edilmez.** Tarama iptal edilebilir (GUI'de "Durdur", CLI'da Ctrl+C,
  çıkış kodu 2) ve iptal edilen taramanın yarım bulguları atılır. Düzeltme ise iptal
  kabul etmez: yedek alınmış ve silme başlamışken yarıda kesmek yarım iş bırakır.
  `IIssueProvider.Fix` ve `CleanupService.Fix` bilerek `CancellationToken` almaz;
  bu `ParallelScanTests.FixIsNotCancellable` ile sabitlenmiştir.

## 4. Toplu düzeltmenin kapsamı

"Tümünü düzelt" bir bulguyu ancak şu üçü birden sağlanırsa alır:

1. Güven >= 90,
2. En az iki bağımsız (ağırlığı sıfırdan büyük) kanıt,
3. Kategori `uninstall`, `shortcut`, `startup` veya `task`.

**Artık klasörler ve MSIX paketleri toplu düzeltmeye hiç girmez.** Onlar yalnızca tek tek,
kullanıcının kanıtı okuyup seçmesiyle işlenir. Onay diyaloğu ne yapılacağını kategori kategori
sayar. Yükseltilmemiş oturumda HKLM bulguları kilitli görünür; sessizce başarısız olmaz,
"tümünü seç" de onları almaz.

Toplu düzeltmeden önce `SystemRestore.TryCreate` çağrılır. System Restore kapalıysa, oturum
yükseltilmemişse ya da Windows'un 24 saatlik sıklık limitine takılırsa sessizce `false` döner
ve **işlem engellenmez**.

## 5. Silme değil, taşıma

Dosya ve klasör işlemleri gerçek silme değildir: içerik yedek klasörünün `payload` alt
klasörüne taşınır ve yanına bir manifest yazılır. Registry işlemleri silmedir ama öncesinde
tam ağaç ya da tek değer JSON olarak kaydedilir.

Yedek kökü:

```text
%LOCALAPPDATA%\Ghostlist\Backups
```

Sürüm 1'in eski yedek klasörü ilk açılışta buraya taşınır (`BackupPaths.MigrateLegacyBackups`).

## 6. Geri yükleme adımları

**Arayüzden:** üst şeritteki **Yedeği geri yükle** düğmesi yedek klasörünü açar; `.json`
dosyasını seçtiğinizde kayıt geri yazılır ve liste yeniden taranır.

**Komut satırından:**

```powershell
ghostlist restore --list
ghostlist restore --backup "%LOCALAPPDATA%\Ghostlist\Backups\<dosya>.json"
```

Manifest sonekine göre doğru geri yükleme yolu seçilir: `.value.json` tek Registry değerini,
`.ghost.json` taşınmış dosya veya klasörü, diğerleri tam Registry ağacını geri yazar.

## 7. Testlerin sınırı

Testler gerçek Registry'ye yazmaz; `Ghostlist.Tests/InMemoryRegistryHiveAccessor.cs` sahte
erişim katmanı üzerinden çalışır. Dosya sistemi de sahte bir `IFileSystem` ile beslenir.
Gerçek makinede yapılan doğrulamalar sözleşmelerin **Kayıt noktası** bölümlerinde kayıtlıdır.
