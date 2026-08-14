# Ghostlist

Windows'ta kaldırma sonrası geride kalan kayıtları kanıta dayanarak bulan ve yedekleyerek
temizleyen masaüstü uygulaması + CLI.

## Klasörler
- `Ghostlist.Core/` — tarama, sınıflandırma, yedekleme motoru. Kullanıcıya görünen metin yok.
- `Ghostlist.App/` — WPF arayüzü, TR/EN çeviri tablosu, ayarlar.
- `Ghostlist.Cli/` — konsol ön yüzü. Metinleri İngilizce, çeviri tablosuna bağlı değil.
- `Ghostlist.Tests/` — xUnit. Gerçek Registry'ye yazan test yok.
- `docs/GUVENLIK.md` — eşikler, korumalar, geri yükleme. Kural değişikliği önce buraya.

## Kurallar
- Kaldırma komutu **çalıştırılmaz**, yalnızca çözümlenir.
- Yedeksiz silme yok; dosya "silme" işlemi yedek klasörüne taşımadır.
- Toplu düzeltme yalnızca güven >= 90 ve iki bağımsız kanıtla; artık klasör ve MSIX asla.
- Erişilemeyen yer "yok" sayılmaz; kanıt belirsizdir, güveni 60'ta tavanlar. Kod yorumu yazılmaz.

## Sınırlar
Girdi: Registry, dosya sistemi, görev XML'i · Çıktı: bulgular, yedekler, GUI/CLI raporu
