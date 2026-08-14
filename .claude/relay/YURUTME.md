# Yürütme — tek ajan modu

Bu projeyi baştan sona **tek başına** bitiriyorsun. Alt ajan açma, iş dağıtma, plan
tartışma. Plan yapıldı; senin işin sözleşmeleri sırayla uygulamak.

## Proje

`C:\Users\Administrator\Desktop\Projeler\Ghostlist` — .NET 8, WPF + Core + CLI + Tests.
Eski adı ProgramFixer. Windows'ta kaldırma sonrası geride kalan "hayalet" artıkları
kanıtlayarak ve geri alınabilir şekilde temizleyen araç.

Bağlamı `PLAN.md`'den al. Sözleşmeler `contracts/`, bitenler `contracts/done/`.

## Durum

**Tur 1 bitti.** T1-T7 `done/` altında, 153 test yeşil, son commit `4e3091e` (v2.0.0).
Push atılmadı — depoya çıkan her şey kullanıcının kararı.

**Tur 2 senin işin:** T8, T9, T10, T11, T12.

## Sıra

```
T8  →  T12          (T12, T8'in Directory.Build.props'una bağlı)
T9                  (bağımsız)
T10 →  T11          (T11, T10'un imzalarına bağlı)
```

Şu sırayla git: **T8 → T9 → T10 → T11 → T12.** Atlama, sıra değiştirme.

## Her sözleşme için döngü

1. Sözleşmeyi oku. `status: active`, `agent_id` alanına kendini yaz.
2. Yalnızca `owns` listesindeki dosyalara **yaz**. `yan_etki` dosyalarına **asgari** dokun —
   çağrı yerini uyarla, dil anahtarı ekle; yeniden tasarlama.
3. Başka sözleşmenin dosyasına düşen bir düzeltme çıkarsa, o dosyayı kendi `owns`una
   **ekleyip** LOG'a satır yaz — sessizce dokunma.
4. Uygula. Her kabul kriterinin karşılığı olan testi yaz.
5. `dotnet build Ghostlist.sln -c Release` ve `dotnet test Ghostlist.sln -c Release`
   yeşil olmadan bitirme.
6. **Kendini denetle.** Ayrı denetçi yok, o rolü de sen taşıyorsun: kabul kriterlerini
   tek tek, koda bakarak işaretle. Kendi raporuna güvenme, dosyayı aç. Bir kriteri
   karşılayamadıysan Çıktı'ya **açıkça yaz** — sessizce "tamam" deme, bu en pahalı hata.
7. Kayıt noktası + Çıktı doldur, `status: done`, dosyayı `contracts/done/`a taşı.
8. `git add -A` + tek commit. Mesaj İngilizce, konu satırı 72 karakter altı, gövdede
   ne değiştiği. Son satır:
   `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`
9. `LOG.md`'ye tek satır: `<tarih> T<n> done · <ne oldu>`.

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
- Testler gerçek Registry'ye yazmaz, `%LOCALAPPDATA%`'ya yazmaz. Sahte erişim katmanı
  hazır: `Ghostlist.Tests/InMemoryRegistryHiveAccessor.cs`. Dosya testleri geçici klasör kullanır.

## Bu turda ayrıca yasak

- **Push, tag, release yayını yok.** Commit serbest, uzağa hiçbir şey gitmez.
- **Ağa çıkan hiçbir şey varsayılan olarak açık olmaz.** Güncelleme kontrolü kapalı gelir.
- **Telemetri yok.** Tanılama paketi kullanıcının diskinde kalır, hiçbir yere gönderilmez.
- Kod imzalama yok — sertifika yok. SHA256 doğrulaması bunun yerine geçmez, tamamlar.

## Dil

- **Core'da kullanıcı metni yok.** Durum, kanıt ve kategori için dil-bağımsız anahtar üret.
- Arayüz varsayılanı **Türkçe**, sol altta `TR / EN` geçişi. Yeni metinlerin hepsi
  `tr.json` **ve** `en.json`'a eklenir; anahtar kümeleri birebir eşit kalmalı.
- Depoya giden doküman (README, workflow, winget manifest) **İngilizce**. Proje içi
  çalışma dosyaları (`docs/GUVENLIK.md`, sözleşmeler, LOG) Türkçe.
- CLI çıktısı ve rapor sütun başlıkları İngilizce; çeviri tablosuna bağlanmaz.

## Arayüz işi (T9)

T9'a başlamadan **önce** `teknesyum-ui` skill'ini yükle — renk paleti, tipografi ölçeği ve
bileşen kalıpları oradan gelir. Renk veya ölçü **uydurma**, token dışına çıkma.
Mevcut pencereyi atma, genişlet.

## Ortam

- Kabuk **Windows PowerShell 5.1**: `&&`, `||`, ternary yok. Yazdığın `.ps1` dosyaları
  5.1'de çalışmalı.
- Hedef `net8.0-windows`, `win-x64`.

## Takıldığında

- Kabul kriteri iki türlü okunuyorsa: **dar olanı** seç, seçimini Çıktı'ya yaz.
- Bir kriter üç denemede geçmiyorsa sorun genelde sende değil sözleşmede. Ne
  anlaşılmadığını Çıktı'ya yaz, `status: blocked`, sonraki sözleşmeye geç.
- Geri dönüşü olmayan bir şey gerekiyorsa yapma, Çıktı'ya yaz.

## Oturum kesilirse

Yeniden başladığında sırayla oku: `LOG.md` son 15 satır → `contracts/` içindeki açık
sözleşmelerin frontmatter'ı → `active` olanın Kayıt noktası. Kaldığın yerden devam et,
baştan başlama.

**Kayıt noktasını iş ilerledikçe güncelle**, sonda değil. Kesilirsen tek bilgi kaynağın o.
