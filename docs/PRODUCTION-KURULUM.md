# Production Kurulumu

Bu yapı Windows Server + IIS için hazırlanmıştır. Production kurulumu lokal geliştirme ortamını değiştirmez. Geliştirme `Development`, canlı uygulama `Production` ortamında çalışır.

## Sunucu Gereksinimleri

- Windows Server 2022 veya güncel desteklenen Windows Server
- IIS, WebSocket Protocol ve ASP.NET Core Hosting Bundle (.NET 9)
- SQL Server/SQL Server Express veya erişilebilir ayrı MSSQL sunucusu
- Gerçek domain ve geçerli TLS sertifikası
- Uygulama, veri ve yedek için ayrı klasörler

Önerilen dizinler:

```text
D:\Sites\QrOrder\releases\
D:\Sites\QrOrder\current\
D:\QrOrderData\uploads\
D:\QrOrderData\keys\
D:\QrOrderData\logs\
D:\QrOrderBackups\
```

`uploads`, `keys`, `logs` ve yedek klasörleri release dizininin dışında olmalıdır. Böylece yeni sürüm yayınlandığında veriler silinmez.

## Yayın Paketi

Geliştirme bilgisayarında:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy\Publish-Production.ps1
```

Paket `artifacts\production` altında oluşur. İçinde yayınlanmış uygulama ve migration bundle bulunur. Paket secret içermez.

## Production Ayarları

`deploy\production.env.example` dosyasındaki değerleri IIS uygulama havuzunun environment variable ayarlarına girin. Bu dosyayı gerçek şifrelerle Git'e göndermeyin.

Zorunlu ayarlar:

```text
ASPNETCORE_ENVIRONMENT=Production
PublicBaseUrl=https://menu.firmaniz.com
AllowedHosts=menu.firmaniz.com
ConnectionStrings__Default=...
Jwt__Key=...
Seed__DemoData=false
Database__ApplyMigrationsOnStartup=false
Storage__UploadsPath=D:\QrOrderData\uploads
DataProtection__KeysPath=D:\QrOrderData\keys
Serilog__WriteTo__1__Args__path=D:\QrOrderData\logs\qrorder-.log
```

`AllowedHosts`, `PublicBaseUrl` icindeki alan adini icermeli ve production ortaminda `*` olmamalidir.

Gercek production degerlerini canliya cikmadan once tek komutla kontrol etmek icin:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\pilot\Test-PilotReadiness.ps1 `
  -EnvironmentFile "D:\QrOrderConfig\production.env"
```

JWT anahtarı en az 48 karakterlik kriptografik rastgele bir değer olmalıdır. Connection string ve JWT anahtarı `appsettings.json` içine yazılmamalıdır.

İlk kurulumda geçici olarak şunları da tanımlayın:

```text
Bootstrap__SuperAdminEmail=owner@example.com
Bootstrap__SuperAdminPassword=GucluBirIlkSifre
```

İlk başarılı girişten sonra Super Admin şifresini değiştirin ve iki bootstrap değişkenini IIS ayarından kaldırın.

## Klasör İzinleri

IIS App Pool hesabına şu izinleri verin:

- `current`: Read & Execute
- `uploads`: Modify
- `keys`: Modify
- `logs`: Modify

SQL Server servis hesabına yalnızca yedek klasöründe Modify izni verin.

## Migration

Production uygulaması migration'ı açılışta otomatik uygulamaz. Önce veritabanı yedeği alın, ardından yayın paketindeki bundle'ı çalıştırın:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy\Apply-ProductionMigrations.ps1 `
  -MigrationBundle "D:\Deploy\QrOrder\database\QrOrder.Migrations.exe" `
  -ConnectionString "PRODUCTION_CONNECTION_STRING"
```

Migration başarılı olmadan IIS sitesini yeni sürüme yönlendirmeyin.

## IIS

1. Uygulama havuzu oluşturun: `QrOrderAppPool`.
2. `.NET CLR Version`: `No Managed Code`.
3. `Start Mode`: `AlwaysRunning`.
4. Siteyi `current\app` klasörüne bağlayın.
5. HTTPS binding ve sertifikayı ekleyin.
6. HTTP trafiğini HTTPS'e yönlendirin.
7. WebSocket Protocol özelliğinin etkin olduğunu doğrulayın.
8. App Pool environment variable değerlerini girin.

IIS ters proxy başlıkları uygulama tarafından işlenir; HTTPS yönlendirmesi ve SignalR WebSocket bağlantıları korunur.

## Yayın Sırası

1. Production yedeği alın. Harici görsel yolu `D:\QrOrderData\uploads` ise yedek scriptine `-WebRoot "D:\QrOrderData"` verin.
2. Yeni paketi yeni bir `releases\tarih-saat` klasörüne açın.
3. Migration bundle'ı çalıştırın.
4. App Pool'u durdurun.
5. IIS physical path değerini yeni release'in `app` klasörüne değiştirin.
6. App Pool'u başlatın.
7. `https://domain/health` adresini kontrol edin.
8. Admin, mutfak, servis ve müşteri sipariş akışını kısa test edin.

## Geri Dönüş

Uygulama hatasında IIS physical path değerini önceki release klasörüne geri alın. Migration geriye uyumlu değilse yalnızca uygulamayı geri almak yeterli değildir; yayından önce alınan MSSQL ve uploads yedeğini kontrollü biçimde geri yükleyin.

## Geliştirmeye Devam Etme

Production yayından sonra lokal geliştirme aynı şekilde devam eder:

```powershell
dotnet run --project src\QrOrder.Web\QrOrder.Web.csproj --launch-profile http
```

Yeni özellikler önce Development ortamında geliştirilir ve test edilir. Her production yayını yeni, tarihli bir release paketi olarak yapılır; canlı klasör üzerinde elle kod değiştirilmez.
