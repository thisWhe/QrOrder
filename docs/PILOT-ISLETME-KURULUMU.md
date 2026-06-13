# Pilot Isletme Kurulumu

Bu akis her yeni isletmede ayni sirayla uygulanir. Amac eksik personel hesabi, yanlis masa QR'i veya test edilmemis menu ile kullanima baslamamaktir.

## 1. Isletmeyi olustur

Super Admin ekraninda:

1. Isletme adini ve benzersiz slug degerini girin.
2. Gercek masa sayisini girin.
3. Isletme admininin e-posta adresini ve gecici sifresini girin.
4. Mutfak ve servis hesaplarini ayni ekranda olusturun veya daha sonra isletme admininden ekleyin.
5. Olusturma sonrasinda isletmenin aktif ve masa sayisinin dogru oldugunu kontrol edin.

Slug QR adresinin kalici parcasidir. Isletme kullanima acildiktan sonra degistirilmemelidir.

## 2. Admin teslim bilgileri

Isletme yetkilisine guvenli bir kanaldan yalnizca su bilgiler verilir:

- Admin adresi: `https://[DOMAIN]/staff/admin`
- Isletme slug degeri
- Admin e-posta adresi
- Gecici sifre

Gecici sifre ilk giriste degistirilir. Sifreler dokuman, e-posta zinciri veya QR baskisi uzerinde tutulmaz.

## 3. Admin paneli kurulum sirasi

Admin panelindeki `Pilot kurulumu` listesi tamamlanir:

1. Kategoriler eklenir ve siralanir.
2. Urunler, fiyatlar, aciklamalar ve gorseller eklenir.
3. Masa numaralari ve QR linkleri kontrol edilir.
4. Logo, kapak gorseli, renkler, siparis durumu ve calisma saatleri ayarlanir.
5. En az bir `Kitchen` ve bir `Service` kullanicisi olusturulur.
6. QR menuden gercek akisla test siparisi verilir.

## 4. Hesap ve ekran adresleri

- Admin: `https://[DOMAIN]/staff/admin`
- Mutfak: `https://[DOMAIN]/staff/kitchen`
- Servis: `https://[DOMAIN]/staff/service`
- Super Admin: `https://[DOMAIN]/super-admin`
- Sistem kontrolu: `https://[DOMAIN]/health`

Personel hesaplari ortak kullanilacaksa isletmenin bunu bilerek kabul etmesi gerekir. Guvenlik ve izlenebilirlik icin kisiye ozel hesap tercih edilir.

## 5. QR kontrolu

- Her masanin QR kodu farkli URL acmalidir.
- Acilan sayfadaki masa numarasi fiziksel masa ile ayni olmalidir.
- Pasif masa siparis oturumu acmamalidir.
- QR kodlar baskidan once en az iki farkli telefonla taranmalidir.
- QR baskisi su, silinme ve dusuk isik kosullarinda okunabilir boyutta olmalidir.

## 6. Pilot acilis testi

`PILOT-KABUL-VE-TESLIM.md` icindeki zorunlu senaryolar uygulanir. Test siparisinin mutfaga dusmesi tek basina yeterli degildir; hazirlama, servis, teslim, iptal, garson cagir ve sayfa yenileme akislari da kontrol edilir.

## 7. Teslim

- Isletme yetkilisine admin kullanimi gosterilir.
- Mutfak ve servis ekranlari ilgili cihazlarda acilir.
- Bildirim sesi ve ekran uyku ayarlari kontrol edilir.
- QR kodlar masalara yerlestirilir.
- Destek iletisim kanali ve pilot destek suresi yazili olarak belirtilir.
- Kabul belgesindeki acik maddeler kaydedilir.
