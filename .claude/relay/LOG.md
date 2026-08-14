2026-08-14 T0  plan: 7 sozlesme, zincir T1->T2->T3->T4->{T5,T6}->T7
2026-08-14 T0  klasor ProgramFixer -> Ghostlist yeniden adlandirildi
2026-08-14 T1  done: ProgramFixer->Ghostlist mekanik rename tamamlandi (dosya/klasor git mv, namespace/metin sed, BackupPaths.cs eklendi, build+test yesil)
2026-08-14 T1  denetim gecti · 8/8 kriter, build 0 uyari, test 13/13
2026-08-14 T2  done: ozyinelemeli registry yedegi (RegistryTreeBackup), IRegistryHiveAccessor soyutlamasi, view-duyarli %ProgramFiles% cozumleme, goreli yol/sarmalayici komut parseri, 44 test yesil
2026-08-14 T2  done: ozyinelemeli yedek + view-duyarli yol cozumleme, 44/44 test
2026-08-14 T2  denetci olu · session limit (9pm) — DENETIM YAPILMADI, borc
2026-08-14 T0  mod degisti: tek ajan (sole), YURUTME.md yazildi
2026-08-14 T2  denetim gecti (sole) - 6/6 kriter koda bakilarak dogrulandi, build 0 uyari, test 44/44
2026-08-14 T3  done: kanit tabanli siniflandirma (Evidence/Finding/ConfidenceRules), IIssueProvider + CleanupService saglayici listesi, MSI yetim tespiti (PackedGuid), MSIX saglayicisi, uc durumlu IFileSystem; build 0 uyari, test 92/92
2026-08-14 T3  owns genisletildi: CleanupService.cs (yeni, T2 dosyasindan tasindi), Backup/FileBackupSink.cs (T4 klasoru, iskelet), RegistryRepository.cs (sinif silme), MainWindow.xaml{,.cs} (T5 dosyasi, asgari uyarlama), 3 eski test dosyasi
2026-08-14 T4  done: kisayol/baslangic/gorev/artik-klasor saglayicilari, COM-suz ShellLinkReader, IBackupSink deger+klasor yedegi, IFileSystem 5 yeni uye, DeleteValue; build 0 uyari, test 132/132
2026-08-14 T4  owns genisletildi: EntryClassifier.cs + IIssueProvider.cs + Models.cs + CleanupService.cs (T3), RegistryRepository.cs + InMemoryRegistryHiveAccessor.cs (T2, DeleteValue), Providers/EnvironmentPaths.cs + ShellLinkReader.cs (yeni yardimci)
2026-08-14 T5  done: Localization tabanli TR/EN gecisi (115 anahtar, anlik, settings.json), MVVM ayrimi, gruplu sanallastirilmis liste + sayi rozeti, kanit paneli, yonetici rozeti/kilitli HKLM satirlari, MessageBox yerine uygulama ici diyalog; build 0 uyari, test 132/132
