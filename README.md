# Temizlik ve Bakım Merkezi Professional v3.1.5

Bu proje, `temizlik v2.cmd` mirasını modern, erişilebilir ve tam özellikli bir Windows masaüstü uygulamasına dönüştürür.

İthaf: **Vedat Güldü tarafından, Olcay Aşçı anısına geliştirilmiştir.**

## Türkçe ve UTF-8 Durumu

- Arayüz metinleri baştan sona Türkçe olacak şekilde güncellenmiştir.
- Dosyalar UTF-8 ile çalışacak biçimde korunmuştur.
- `temizlik v2.cmd` başlangıçta `chcp 65001` çalıştırır.

## Erişilebilirlik ve WCAG AAA

- Alt menü tabanlı gezinme vardır, sekmeli yapı kullanılmaz.
- Alt tuşu ile erişilebilen menü kısayolları vardır (`_Ana Sayfa`, `_Temizlik`, `S_istem`, `_Raporlar`, `Erişile_bilirlik`, `İt_haf`, `_V3 Özellikler`).
- Aydınlık ve karanlık tema, `Uygulama > Ayarlar` menüsünden yönetilir.
- Tema tercihi, yüksek kontrast tercihi ve başlangıç ipucu tercihi kalıcı olarak saklanır.
- Yüksek kontrast yalnızca uygulama içinde uygulanır, sistem geneline etki etmez.
- Tema renkleri güçlü kontrastla güncellenmiştir.
- WCAG AAA denetimi için çoklu kontrast ölçümü yapılır:
  - Ana metin / kart
  - Düğme metni / düğme zemini
  - Başlık metni / başlık zemini

## Modül Odak Akışı

- Modüller, seçildiğinde tam odak görünümde açılır.
- Her modülde `_Geri Dön` düğmesi ile ana sayfaya dönüş sağlanır.
- Modül görünümünde gereksiz gezinme öğeleri gizlenerek tab dolaşımı sadeleştirilir.

## Başlangıç İpuçları

- Uygulama açılışında adım adım özellik anlatımı sunan ipucu katmanı vardır.
- `_Önceki` ve `_Sonraki` düğmeleriyle ipuçları arasında gezilebilir.
- `Bu başlangıç ipuçlarını bir daha gösterme` seçeneği ile ipuçları kalıcı olarak kapatılabilir.

## Pro Lisans Modeli

- Pro+ özellikler `Ömür Boyu Lisans` etkinleştirildiğinde açılır.
- Son kullanıcı uygulaması yalnızca lisans etkinleştirme/doğrulama ekranını gösterir.
- Lisans doğrulaması sunucu tarafında yapılır (`LicensingServer`).
- Lisans üretme, iptal etme, cihaz sıfırlama gibi sahip işlemleri uygulamada görünmez; ayrı admin API/panel üzerinden yönetilir.
- Uygulama lisans sunucusunu `TBM_LICENSE_API_URL` ortam değişkeni ile özelleştirebilir.
- Pro+ örnekleri: Akıllı bakım planı üretimi, kritik risk erken uyarı taraması, gelişmiş analiz modülleri.

## Lisans Sunucusu

- Sahip/yönetici işlemleri için ayrı servis: `LicensingServer`
- Son kullanıcı endpoint'i: `POST /api/v1/license/activate`
- Admin endpoint'leri: `/api/v1/admin/*` (`X-Admin-Token` ile korunur)
- Ayrıntı: `LicensingServer/README.md`
- Sahip operasyon rehberi: `OWNER_LISANS_YONETIMI.md`

## Eklenen 9 Yeni Özellik

1. Başlangıç Uygulamaları Raporu
2. RAM ve Süreç Anlık Görünümü
3. Disk Sağlık Özeti
4. Temp Boyut Analizi
5. DNS Çoklu Gecikme Testi
6. Hosts Dosyasını Yedekleme
7. Son 24 Saat Hata Özeti
8. Başlangıç Etki Analizi
9. Bakım Özetini Panoya Kopyalama

## Diğer Güçlü Özellikler

- Seçmeli temizlik görevleri
- Canlı günlük ve JSON raporlama
- Yönetici moduna yeniden başlatma
- Günlük zamanlama
- Büyük dosya radar, klasör boyut analizi
- ZIP yedek ve destek paketi üretimi

## Geliştirici Çalıştırma

```powershell
dotnet build ./TemizlikMasaUygulamasi.csproj
dotnet run --project ./TemizlikMasaUygulamasi.csproj
```

## Bağımsız Dağıtım (Kurulum Dosyası)

Proje, kullanıcı tarafında ek kurulum gerektirmeyecek şekilde self-contained yayın ve setup üretir.

```powershell
cd ./TemizlikMasaUygulamasi
.\build-setup.cmd
```

Üretilen dosyalar:

- Self-contained yayın: `publish/win-x64/TemizlikMasaUygulamasi.exe`
- Kurulum dosyası: `artifacts/setup/TemizlikBakimMerkezi-Professional-v3_1_5-Setup.exe`

## GitHub Üzerinden Güncelleme

- Uygulama açılışta GitHub Releases üzerinden güncelleme denetimi yapar.
- Dashboard içinde `_Güncelleme Denetle` butonu ile manuel kontrol yapılabilir.
- Yeni sürüm varsa kullanıcı onayı ile kurulum paketi otomatik indirilir ve sessiz güncelleme başlatılır.
- Güncelleme tamamlandığında uygulama otomatik olarak yeniden açılır.
- GitHub release pipeline dosyası: `.github/workflows/release.yml`

## Not

- Bazı sistem bakım işlemleri yönetici yetkisi ister.
- `temizlik v2.cmd` artık eski riskli toplu silme akışını çalıştırmaz; v3 uygulamasını açar.
