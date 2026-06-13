# Pilot Kabul ve Teslim Plani

Bu belge ilk pilot isletmenin kontrollu kurulumu, kabul testi ve teslimi icin kullanilir.

## Isletmeden alinacak bilgiler

- Isletme adi, adresi ve yetkili iletisim bilgisi
- Alan adi veya kullanilacak alt alan adi
- Logo, kapak gorseli ve kurumsal renkler
- Kategoriler, urunler, fiyatlar ve urun aciklamalari
- Masa numaralari ve basılacak QR kod sayisi
- Admin, mutfak ve servis hesaplari
- Calisma saatleri ve "garson cagir" akisinin sorumlusu

## Canliya cikmadan once

- Production ortam degiskenleri gercek degerlerle doldurulur.
- `PublicBaseUrl` HTTPS adresidir ve `AllowedHosts` ayni alan adini icerir.
- Demo seed ve otomatik migration kapali tutulur.
- Veritabani migration paketi once yedek alindiktan sonra uygulanir.
- Upload, Data Protection anahtarlari ve log klasorleri deployment klasoru disindadir.
- Ilk tam yedek alinir ve ayri bir test veritabanina geri yukleme testi yapilir.
- Super admin ilk giristen sonra gecici sifresini degistirir; bootstrap bilgileri kaldirilir.

## Zorunlu kabul senaryolari

1. Isletme admini yalnizca kendi kategori, urun, masa, personel ve siparislerini gorur.
2. QR kod dogru isletmeyi ve masa numarasini acar.
3. Musteri urun ekler, not girer ve siparisi bir kez olusturur.
4. Gonder butonuna tekrar basilsa veya istek tekrarlansa ayni siparis ikinci kez olusmaz.
5. Siparis mutfak ekranina gelir; sayfa yenilendiginde veritabanindan tekrar yuklenir.
6. Mutfak siparisi hazirlar, servis ekrani sesli ve gorsel bildirim alir.
7. Servis teslim durumuna getirir ve durum musterinin ekranina yansir.
8. Musteri izin verilen asamada iptal talebi verebilir; sonraki asamada engellenir.
9. Garson cagir bildirimi servis ekranina gelir ve tamamlanabilir.
10. Pasif masa yeni oturum acamaz; aktif siparisi olan masa pasife alinamaz.
11. Mevcut degil olarak isaretlenen urun menude siparis edilemez.
12. Silinen veya sifresi degisen personelin mevcut oturumu gecersiz olur.
13. `/health` sonucu `Healthy` doner ve uygulama loglarinda ilgili istek bulunur.
14. 320, 360, 390, 412 ve 430 piksel genislikte yatay tasma ve erisilemeyen buton yoktur.

## Pilot isletim kurallari

- Pilot boyunca menu ve masa degisikliklerini yalnizca isletme admini yapar.
- Her gun servis oncesi mutfak ve servis ekraninda deneme siparisi verilir.
- Loglar gunluk kontrol edilir; hata raporunda tarih, masa ve siparis kimligi bulunur.
- Her gun otomatik yedek, haftada en az bir kez yedek basari kontrolu yapilir.
- Ilk hafta kritik hata destegi ve geri donus sorumlusu onceden belirlenir.

## Ariza ve geri donus

1. Yeni siparis alimi durdurulur ve isletmeye bilgi verilir.
2. Uygulama logu, `/health` sonucu ve SQL erisimi kontrol edilir.
3. Son deployment kaynakliysa onceki uygulama paketi geri alinir.
4. Veritabani degisikligi kaynakliysa veri kaybi riski degerlendirilmeden restore yapilmaz.
5. Restore gerekiyorsa once mevcut bozuk durumun da yedegi alinir.
6. Sistem acildiktan sonra QR, siparis, mutfak ve servis akisi yeniden test edilir.

## Teslim kaydi

- Isletme:
- Alan adi:
- Production surumu:
- Kurulum tarihi:
- Yedek geri yukleme testi tarihi:
- Kabul testini yapan:
- Isletme yetkilisi:
- Acik kalan maddeler:
