# Yedekleme ve Geri Yükleme

QrOrder yedeği iki parçadan oluşur:

1. MSSQL veritabanı (`QrOrderDb.bak`)
2. Ürün ve işletme görselleri (`uploads.zip`)

Üretilen pakette ayrıca `manifest.json` ve dosyaların bozulmadığını doğrulayan `checksums.sha256` bulunur.

## Üretim Politikası

- Her gece en az bir tam yedek alın.
- En az 30 günlük yedek saklayın.
- Yedeğin ikinci kopyasını uygulama sunucusundan farklı bir cihazda veya güvenli bulut depolamada tutun.
- Haftada en az bir kez yedekten ayrı bir test ortamına geri yükleme deneyin.
- Yedek klasörünü yalnızca SQL Server servis hesabı ve yetkili yönetici okuyabilmelidir.
- JWT anahtarı ve production bağlantı bilgileri bu yedek paketine dahil değildir; bunları ayrı bir secret manager içinde saklayın.

## Ön Koşullar

- `sqlcmd` kurulu olmalıdır.
- Script Windows Authentication (`-E`) kullanır.
- `BackupRoot` klasörüne hem komutu çalıştıran kullanıcı hem SQL Server servis hesabı yazabilmelidir.
- Uzak SQL Server kullanılıyorsa verilen yol SQL Server makinesinin gördüğü bir UNC paylaşımı olmalıdır.

## Yedek Alma

PowerShell'i proje klasöründe açın:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\backup\Backup-QrOrder.ps1 `
  -ServerInstance "localhost\SQLEXPRESS" `
  -Database "QrOrderDb" `
  -BackupRoot "C:\QrOrderBackups" `
  -RetentionDays 30
```

Başarılı sonuç örneği:

```text
C:\QrOrderBackups\QrOrder_20260612-180000\
  QrOrderDb.bak
  uploads.zip
  manifest.json
  checksums.sha256
```

Script SQL Server Express sürümünü otomatik algılar ve sıkıştırmayı kapatır. Diğer sürümlerde sıkıştırmayı elle kapatmak gerekirse `-DisableCompression` parametresini kullanın.

## Zamanlama

Windows Görev Zamanlayıcı içinde günlük bir görev oluşturun:

```text
Program: powershell.exe
Arguments: -NoProfile -ExecutionPolicy Bypass -File "C:\QrOrder\scripts\backup\Backup-QrOrder.ps1" -ServerInstance "localhost\SQLEXPRESS" -Database "QrOrderDb" -BackupRoot "D:\QrOrderBackups" -RetentionDays 30
```

Görevi SQL Server yedek klasörüne erişebilen ayrı bir servis kullanıcısıyla çalıştırın.

## Geri Yükleme

Geri yükleme mevcut veritabanını değiştirir. Önce uygulamayı kapatın ve doğru paketi seçtiğinizden emin olun.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\backup\Restore-QrOrder.ps1 `
  -BackupPackage "C:\QrOrderBackups\QrOrder_20260612-180000" `
  -ServerInstance "localhost\SQLEXPRESS" `
  -Database "QrOrderDb" `
  -ConfirmDatabaseReplacement
```

Script önce SHA-256 ve SQL `RESTORE VERIFYONLY` kontrollerini çalıştırır. Mevcut `uploads` klasörünü aynı yedek paketinin içine güvenlik arşivi olarak kaydeder.

Geri yükleme bittikten sonra uygulamayı başlatın ve kontrol edin:

```text
http://localhost:5140/health
```

Yanıt `Healthy` olmalı. Ardından admin panelinden işletme, ürün, masa ve sipariş kayıtlarını kontrol edin.

## Önemli

Yedek dosyasının oluşması tek başına yeterli değildir. Satışa çıkmadan önce en az bir yedeği test veritabanına geri yükleyerek gerçekten açıldığını doğrulayın.
