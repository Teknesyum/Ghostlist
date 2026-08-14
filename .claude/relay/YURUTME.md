# Yürütme — tek ajan modu

Bu projeyi baştan sona **tek başına** bitiriyorsun. Alt ajan açma, iş dağıtma, plan
tartışma. Plan yapıldı; senin işin sözleşmeleri sırayla uygulamak.

## Proje

`C:\Users\Administrator\Desktop\Projeler\Ghostlist` — .NET 8, WPF + Core + Tests.
Eski adı ProgramFixer. Windows'ta kaldırma sonrası geride kalan "hayalet" artıkları
kanıtlayarak ve geri alınabilir şekilde temizleyen araç.

Bağlamı `PLAN.md`'den al. Sözleşmeler `contracts/`, bitenler `contracts/done/`.

## Sıra — bu sırayla, atlamadan

```
T2-denetim (borç)  →  T3  →  T4  →  T5  →  T6  →  T7
```

T1 ve T2 kod olarak bitti, commit'te (`6a0edee`). **T2 denetlenemedi** — denetçi
oturum limitinde düştü. İlk işin bu borcu kapatmak: `contracts/done/T2.md` kabul
kriterlerini koda bakarak doğrula. Kod yeterliyse LOG'a `T2 denetim gecti` yaz ve
T3'e geç; eksik varsa **önce onu düzelt**, T2'nin dosyalarına yazma izni sende.

T5 ve T6 birbirinden bağımsız; hangisini önce yaparsan yap.

## Her sözleşme için döngü

1. Sözleşmeyi oku. `status: active`, `agent_id` alanına kendini yaz.
2. Yalnızca `owns` listesindeki dosyalara yaz. Başka sözleşmenin dosyasına düşen bir
   düzeltme çıkarsa, o dosyayı kendi `owns`una **ekleyip** LOG'a satır yaz — sessizce
   dokunma.
3. Uygula. Her kabul kriterinin karşılığı olan testi yaz.
4. `dotnet build Ghostlist.sln -c Release` ve `dotnet test Ghostlist.sln -c Release`
   yeşil olmadan bitirme.
5. **Kendini denetle.** Ayrı denetçi yok, o rolü de sen taşıyorsun: kabul kriterlerini
   tek tek, koda bakarak işaretle. Kendi raporuna güvenme, dosyayı aç. Bir kriteri
   karşılayamadıysan Çıktı'ya **açıkça yaz** — sessizce "tamam" deme, bu en pahalı hata.
6. Kayıt noktası + Çıktı doldur, `status: done`, dosyayı `contracts/done/`a taşı.
7. `git add -A` + tek commit. Mesaj İngilizce, konu satırı 72 karakter altı, gövdede
   ne değiştiği. Son satır:
   `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`
8. `LOG.md`'ye tek satır: `<tarih> T<n> done · <ne oldu>`.

Sonra bir sonraki sözleşmeye geç. Aralarda durup rapor verme, sonuna kadar git.

## Sözleşme dışına çıkma

Sözleşmede yazmayan özellik ekleme, mimariyi yeniden kurma, dosya taşıma. Kapsam
genişletmek isteyen bir fikir çıkarsa `PLAN.md` sonuna "sonraki tur" başlığı altına
yaz, uygulama.

## Bu üründe asla

Bunlar kullanıcı verisi ve güven meselesi; ihlali ürünü bitirir.

- Kullanıcı belgelerine, oyun kayıtlarına, `Documents`, `Saved Games`, `%APPDATA%` ve
  `%LOCALAPPDATA%` veri klasörlerine dokunma. Artık klasör taraması yalnızca
  `Program Files`, `Program Files (x86)`, `%LOCALAPPDATA%\Programs` birinci seviyesi.
- Kaldırma komutunu **çalıştırma**. Ürün komutu çözümler, asla tetiklemez.
- Yedeksiz silme yok. Registry ağacı, dosya ve görev XML'i — hepsi önce yedeklenir.
  Dosya "silme" işlemi gerçek silme değil, yedek klasörüne taşımadır ve geri alınır.
- Otomatik toplu düzeltme yalnızca `Confidence >= 90` **ve** en az iki bağımsız kanıt
  varken. Artık klasörler ve MSIX toplu düzeltmeye **hiç** girmez.
- MSIX paketlerini kaldırma; `Remove-AppxPackage` komutunu kullanıcıya **göster**.
- Sistem bileşeni ve çözülemeyen komut korumaları gevşetilmez.
- Erişilemeyen bir yer "yok" sayılmaz. `%WINDIR%\Installer` okunamıyorsa kanıt
  "belirsiz"dir; yanlış pozitif üretmektense bulgu üretme.
- Testler gerçek Registry'ye yazmaz. Sahte erişim katmanı zaten var:
  `Ghostlist.Tests/InMemoryRegistryHiveAccessor.cs`.

## Dil

- **Core'da kullanıcı metni yok.** Durum, kanıt ve kategori için dil-bağımsız anahtar
  üret. Çeviri tablosu T5'in işi; Core'a Türkçe dize koyarsan T5 çalışamaz.
- Arayüz varsayılanı **Türkçe**, sol altta `TR / EN` geçişi (T5).
- Depoya giden doküman (README, workflow) **İngilizce**. Proje içi çalışma dosyaları
  (`docs/GUVENLIK.md`, sözleşmeler, LOG) Türkçe.
- CLI çıktısı İngilizce, çeviri tablosuna bağlanmaz.

## Arayüz işi (T5)

T5'e başlamadan **önce** `teknesyum-ui` skill'ini yükle — renk paleti, tipografi ölçeği ve
bileşen kalıpları oradan gelir. Renk veya ölçü **uydurma**, token dışına çıkma.
Mevcut `MainWindow.xaml`'i atma, genişlet.

## Takıldığında

- Kabul kriteri iki türlü okunuyorsa: **dar olanı** seç, seçimini Çıktı'ya yaz.
- Bir kriter üç denemede geçmiyorsa sorun genelde sende değil sözleşmede. Ne
  anlaşılmadığını Çıktı'ya yaz, `status: blocked`, sonraki sözleşmeye geç.
- Geri dönüşü olmayan bir şey (dosya silme, repo işlemi, push) gerekiyorsa yapma,
  Çıktı'ya yaz. Commit serbest, **push yok**.

## Oturum kesilirse

Yeniden başladığında sırayla oku: `LOG.md` son 15 satır → `contracts/` içindeki açık
sözleşmelerin frontmatter'ı → `active` olanın Kayıt noktası. Kaldığın yerden devam et,
baştan başlama.

**Kayıt noktasını iş ilerledikçe güncelle**, sonda değil. Kesilirsen tek bilgi kaynağın o.
