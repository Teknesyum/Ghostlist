# Ghostlist.Cli

Konsol ön yüzü. GUI ile **aynı** `CleanupService` üzerinden çalışır, kendi kopyasını tutmaz.

## Dosyalar
- `CommandLine.cs` — saf ayrıştırıcı: `string[]` → `CliPlan`. Yan etkisi yok, testleri buradan.
- `Reporter.cs` — metin ve JSONL çıktısı. `--json` satır satır ayrıştırılabilir olmalı.
- `Program.cs` — komut yürütme, çıkış kodları, onay istemi.

## Kurallar
- **Metinler İngilizce.** `Ghostlist.App`'in çeviri tablosuna bağlanma.
- Ayar dosyasına **yazma**; gerekirse oku.
- Çıkış kodları: `0` temiz, `1` bulgu var, `2` hata. Başka kod ekleme.
- `--dry-run` hiçbir şeyi değiştirmez, geri yükleme noktası bile oluşturmaz.
- `fix --all` terminalde onay ister; girdi yönlendirilmişse `--yes` olmadan çalışmaz.
- Eşik ve kategori kısıtı `ConfidenceRules`'tan gelir; `--min-confidence` yalnızca daraltır.

## Sınırlar
Girdi: komut satırı argümanları · Çıktı: stdout raporu, çıkış kodu, `Ghostlist.Core` yedekleri
