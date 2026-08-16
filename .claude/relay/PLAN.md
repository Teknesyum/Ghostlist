# Ghostlist — plan

**Eski ad:** ProgramFixer (v1.0.0). Klasör `Ghostlist`'e alındı; içerik yeniden adlandırması T1'de.

**Hedef:** yetim kaldırma kayıtlarını temizleyen tek amaçlı araçtan, "Windows'ta geride kalan
hayalet artıkların tamamını kanıtlayarak ve geri alınabilir şekilde temizleyen" araca geçmek.

## Değişmeyen sözleşme

Her silme öncesi tam yedek. Yalnızca kanıtlanmış yetim kayıt silinir. Uygulama klasörleri,
kullanıcı dosyaları, oyun kayıtları asla silinmez. Kaldırma komutu asla çalıştırılmaz.

## Görev grafiği

```
T1 rename ──▶ T2 dogruluk ──▶ T3 siniflandirma ──▶ T4 artik-saglayicilar ──┬─▶ T5 arayuz+i18n ──▶ T7 dokuman
                                                                            └─▶ T6 cli+ci
```

| # | Başlık | Rol | Model | Bağımlı |
|---|---|---|---|---|
| T1 | ProgramFixer → Ghostlist yeniden adlandırma | kayitci | sonnet | — |
| T2 | Doğruluk açıkları: yedek bütünlüğü, yol çözümleme | usta | opus | T1 |
| T3 | Sınıflandırma motoru: kanıt skoru, MSI/MSIX yetimleri | usta | opus | T2 |
| T4 | Artık sağlayıcıları: kısayol, başlangıç, görev, klasör | usta | sonnet | T3 |
| T5 | Arayüz yenilemesi + TR/EN dil geçişi | usta-arayuz | sonnet | T4 |
| T6 | CLI modu + geri yükleme noktası + CI | usta | sonnet | T4 |
| T7 | README (EN), betikler, yönlendirici CLAUDE.md | kayitci | haiku | T5, T6 |

## Kapsam kararları

- **Dil:** varsayılan Türkçe. Alt panelin solunda `TR / EN` tıklanabilir geçiş. Seçim kalıcı.
- **İmzalama:** bu turda yok. CI imzasız yayınlar, SHA256 yayımlanır.
- **Depo:** `github.com/Teknesyum/ProgramFixer` → `Ghostlist` yeniden adlandırması kullanıcıda.
- Yedek klasörü `%LOCALAPPDATA%\Ghostlist\Backups`; eski `ProgramFixer` klasörü ilk açılışta göç eder.

---

# Tur 2 — yayına hazırlık

Tur 1 bitti: T1-T7 `done/`, 153 test yeşil, son commit `4e3091e` (v2.0.0). Push atılmadı.

Tur 1'in kapanışında açık kalanlar bu turun çıkış noktası: sürüm üç yerde elle tutuluyor,
kurulum betiği indirdiğini doğrulamıyor, yedekler yalnızca dosya seçiciyle geri yükleniyor,
tarama iptal edilemiyor, ürünün güncellemeden haberi yok.

```
T8 surum+paketleme ──┬─▶ T12 guncelleme+winget
T9 yedek+gecmis ─────┤
T10 performans+iptal ─┤
T11 rapor+tani ──────┘
```

| # | Başlık | Rol | Model | Bağımlı |
|---|---|---|---|---|
| T8 | Sürüm tek kaynağa, kurulum bütünlük doğrulaması | usta | sonnet | — |
| T9 | Yedek yönetimi ve işlem geçmişi ekranı | usta-arayuz | sonnet | — |
| T10 | Paralel tarama, iptal, kategori ilerlemesi | usta | sonnet | — |
| T11 | Rapor dışa aktarma + tanılama paketi | usta | sonnet | T10 |
| T12 | Güncelleme bildirimi + winget manifest + test kapsamı | usta | sonnet | T8 |

## Bu turda yapılmayacaklar

- **Push, tag, release yayını yok.** Depoya çıkan her şey kullanıcının kararı.
- **Kod imzalama yok** — sertifika yok. SHA256 doğrulaması bunun yerine geçmez, tamamlar.
- Otomatik güncelleme indirme/kurma yok; ürün yalnızca **haber verir**.
- Telemetri yok. Tanılama paketi kullanıcının elinde kalır, hiçbir yere gönderilmez.

## Sonraki tur

- GitHub'daki `ProgramFixer v1.0.0` yayın (release) başlığı hâlâ duruyor
  (`gh release list` → `ProgramFixer v1.0.0  v1.0.0  2026-08-13T18:53:41Z`). T16 bunu
  teşhis etti ama düzeltemedi — bu sözleşmede push/tag/release yasak. Yayın
  koordinatörü başlığı `gh release edit v1.0.0 --title "..."` ile güncellesin veya
  gövdeye "eski ad" notu eklesin.
