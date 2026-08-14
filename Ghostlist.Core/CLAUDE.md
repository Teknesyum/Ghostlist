# Ghostlist.Core

Kanıt tabanlı tarama, sınıflandırma ve yedekleme motoru. Arayüzden bağımsız.

## Dosyalar
- `Models.cs` — `Finding`, `Evidence`, `EvidenceKinds/Weights`, `ConfidenceRules`. Puanlamanın tek kaynağı.
- `EntryClassifier.cs` — `IFileSystem` sözleşmesi + kaldırma kaydı sınıflandırması.
- `RegistryRepository.cs` — Registry erişim soyutlaması, yakalama ve silme.
- `CleanupService.cs` — sağlayıcı listesi, `Scan/Fix/Restore`, `CreateDefault`.
- `Providers/` — altı `IIssueProvider`: uninstall, shortcut, startup, task, folder, msix.
- `Msi/`, `Appx/` — MSI packed GUID çözümü ve MSIX paket deposu okuma.
- `Backup/FileBackupSink.cs` — Registry ağacı/değeri, dosya ve klasör yedekleri.
- `SystemRestore.cs` — `TryCreate`; başarısızlıkta sessiz `false`.

## Kurallar
- **Kullanıcıya görünen metin yazma.** Dilden bağımsız anahtar üret; çeviri `Ghostlist.App`'in işi.
- Yeni kanıt türü eklerken `EvidenceKinds` + `EvidenceWeights` + iki dil dosyası birlikte güncellenir.
- Okunamayan yer `ProbeResult.Unknown`; ağırlık 0, güven tavanı 60.
## Sınırlar
Girdi: Registry, dosya sistemi · Çıktı: `Finding` listesi, `FixResult`, yedek yolları
