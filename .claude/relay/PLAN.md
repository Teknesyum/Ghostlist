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
