# QR Sipariş Yönetim Sistemi

QR Sipariş Yönetim Sistemi, restoran ve kafe işletmeleri için geliştirilmiş çoklu işletme destekli bir sipariş yönetim uygulamasıdır. Müşteriler masaya özel QR kodu okutarak menüye ulaşabilir, sipariş oluşturabilir, garson çağırabilir ve sipariş durumunu gerçek zamanlı takip edebilir. Mutfak, servis ve admin ekranları ayrı rollerle yönetilir.

## Özellikler

- Çoklu işletme mimarisi
- Masaya özel QR menü erişimi
- Mobil öncelikli müşteri menüsü
- Sepet ve müşteri sipariş notu desteği
- SignalR ile gerçek zamanlı sipariş bildirimi
- Mutfak ekranında yeni ve hazırlanan siparişlerin takibi
- Servis ekranında hazır siparişler ve garson çağrıları
- Müşterinin garson çağırabilmesi
- Admin panel üzerinden ürün, kategori, masa, kullanıcı ve işletme ayarları yönetimi
- Sipariş raporu ve servis çağrısı geçmişi
- JWT ile kimlik doğrulama
- Rol bazlı yetkilendirme: Admin, Kitchen, Service
- EF Core migration yapısı
- MSSQL veritabanı
- N-Tier katmanlı mimari

## Kullanılan Teknolojiler

- ASP.NET Core 9
- Entity Framework Core
- MSSQL / SQL Server Express
- SignalR
- ASP.NET Core Identity
- JWT Bearer Authentication
- Razor Pages
- Vanilla JavaScript
- CSS

## Proje Yapısı

```text
src/
  QrOrder.Domain/          Entity ve enum sınıfları
  QrOrder.Application/     DTO, interface ve uygulama sözleşmeleri
  QrOrder.Infrastructure/  EF Core, Identity, servisler ve migration dosyaları
  QrOrder.Web/             API controller, Razor Pages, SignalR hub ve arayüz dosyaları
```

## Ana Ekranlar

```text
Admin panel:
http://localhost:5140/staff/admin

Mutfak ekranı:
http://localhost:5140/staff/kitchen

Servis ekranı:
http://localhost:5140/staff/service

Örnek müşteri QR menüsü:
http://localhost:5140/v/demo-cafe/t/84222936215c466d98c512d3d50947ed
```

## Demo Kullanıcılar

```text
İşletme: demo-cafe

Admin:
admin@demo.com / Admin123!

Mutfak:
kitchen@demo.com / Kitchen123!

Servis:
service@demo.com / Service123!
```


## Notlar
- Demo veriler geliştirme ortamında otomatik oluşturulur.
