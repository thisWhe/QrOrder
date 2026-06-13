# QR Siparis Yonetim Sistemi

Restoran ve kafeler icin gelistirilmis coklu isletme destekli QR menu ve siparis yonetim uygulamasidir. Musteriler masaya ozel QR kodu okutarak menuye ulasir, siparis verir, garson cagirir ve siparis durumunu gercek zamanli takip eder.

## Ozellikler

- Coklu isletme ve tenant bazli veri izolasyonu
- Super Admin panelinden isletme, admin, personel ve toplu masa olusturma
- Masaya ozel QR kod ve guvenli masa oturumu
- Mobil oncelikli musteri menusu ve sepet
- Urun gorselleri, stok durumu ve menu siralamasi
- Icerik, porsiyon, kalori, 14 alerjen grubu ve servis sicakligi bilgileri
- Vegan, vejetaryen, alkol ve domuz kaynakli bilesen bildirimleri
- Isletme bazli logo, kapak gorseli, renkler ve calisma saatleri
- SignalR ile gercek zamanli siparis ve durum takibi
- Mutfak ve servis icin ayri operasyon ekranlari
- Ayirt edilebilir sesli siparis ve garson cagrisi bildirimleri
- Musteri siparis iptali ve garson cagirma akisi
- Admin panelinde siparis raporu ve servis cagrisi gecmisi
- Tekrarlanan siparis isteklerine karsi idempotency korumasi
- JWT, ASP.NET Core Identity ve rol bazli yetkilendirme
- Rate limiting, health check, merkezi hata yonetimi ve dosya loglari
- MSSQL yedekleme, geri yukleme testi ve production yayin araclari

## Teknolojiler

- ASP.NET Core 9
- Entity Framework Core
- MSSQL / SQL Server Express
- SignalR
- ASP.NET Core Identity ve JWT
- Razor Pages
- Vanilla JavaScript ve CSS

## Katmanlar

```text
src/
  QrOrder.Domain/          Entity ve enum siniflari
  QrOrder.Application/     DTO, interface ve uygulama sozlesmeleri
  QrOrder.Infrastructure/  EF Core, Identity, servisler ve migration dosyalari
  QrOrder.Web/             API, Razor Pages, SignalR ve arayuz dosyalari
```

## Ekranlar

```text
Super Admin:  http://localhost:5140/super-admin
Admin:        http://localhost:5140/staff/admin
Mutfak:       http://localhost:5140/staff/kitchen
Servis:       http://localhost:5140/staff/service
Health check: http://localhost:5140/health
```

Musteri menu adresleri isletme ve masa icin uretilen QR baglantisindan acilir:

```text
http://localhost:5140/v/{isletme-slug}/t/{masa-kodu}
```

## Lokal Kurulum

1. `src/QrOrder.Web/appsettings.Development.json` dosyasini olusturun.
2. Lokal MSSQL baglantisini ve en az 48 karakterlik gelistirme JWT anahtarini girin.
3. Uygulamayi calistirin:

```powershell
dotnet run --project src\QrOrder.Web\QrOrder.Web.csproj --launch-profile http
```

Development ortaminda migration ve istege bagli demo seed acilista uygulanabilir. Lokal ayar dosyasi Git tarafindan takip edilmez.

## Demo Ortami

Demo kullanicilar yalnizca `Seed:DemoData=true` oldugunda gelistirme ortaminda olusturulur. Production yapilandirmasi demo seed ile calismayi engeller. Demo hesap bilgileri kaynak kodda gercek musteri hesabi olarak kullanilmamalidir.

## Production ve Pilot

- [Production kurulumu](docs/PRODUCTION-KURULUM.md)
- [Yedekleme ve geri yukleme](docs/YEDEKLEME-VE-GERI-YUKLEME.md)

Pilot on kontrolu:



