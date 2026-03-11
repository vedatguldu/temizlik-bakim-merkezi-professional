# Temizlik ve Bakım Merkezi Professional v3.1

Bu proje, `temizlik v2.cmd` mirasını modern, erişilebilir ve tam özellikli bir Windows masaüstü uygulamasına dönüştürür.

İthaf: **Vedat Güldü tarafından, Olcay Aşçı anısına geliştirilmiştir.**

## Türkçe ve UTF-8 Durumu

- Arayüz metinleri baştan sona Türkçe olacak şekilde güncellenmiştir.
- Dosyalar UTF-8 ile çalışacak biçimde korunmuştur.
- `temizlik v2.cmd` başlangıçta `chcp 65001` çalıştırır.

## Erişilebilirlik ve WCAG AAA

- Alt menü tabanlı gezinme vardır, sekmeli yapı kullanılmaz.
- Alt tuşu ile erişilebilen menü kısayolları vardır (`_Ana Sayfa`, `_Temizlik`, `S_istem`, `_Raporlar`, `Erişile_bilirlik`, `İt_haf`, `_V3 Özellikler`).
- Aydınlık ve karanlık tema vardır.
- Tema renkleri güçlü kontrastla güncellenmiştir.
- WCAG AAA denetimi için çoklu kontrast ölçümü yapılır:
  - Ana metin / kart
  - Düğme metni / düğme zemini
  - Başlık metni / başlık zemini

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
dotnet build "c:\Users\vedat\OneDrive\Masaüstü\TemizlikMasaUygulamasi\TemizlikMasaUygulamasi.csproj"
dotnet run --project "c:\Users\vedat\OneDrive\Masaüstü\TemizlikMasaUygulamasi\TemizlikMasaUygulamasi.csproj"
```

## Bağımsız Dağıtım (Kurulum Dosyası)

Proje, kullanıcı tarafında ek kurulum gerektirmeyecek şekilde self-contained yayın ve setup üretir.

```powershell
cd "c:\Users\vedat\OneDrive\Masaüstü\TemizlikMasaUygulamasi"
.\build-setup.cmd
```

Üretilen dosyalar:

- Self-contained yayın: `publish/win-x64/TemizlikMasaUygulamasi.exe`
- Kurulum dosyası: `artifacts/setup/TemizlikBakimMerkezi-Professional-v3_1-Setup.exe`

## GitHub Üzerinden Güncelleme

- Uygulama açılışta GitHub Releases üzerinden güncelleme denetimi yapar.
- Dashboard içinde `_Güncelleme Denetle` butonu ile manuel kontrol yapılabilir.
- Yeni sürüm varsa kullanıcıya bildirim penceresi gösterilir ve indirme bağlantısı açılır.
- GitHub release pipeline dosyası: `.github/workflows/release.yml`

## Not

- Bazı sistem bakım işlemleri yönetici yetkisi ister.
- `temizlik v2.cmd` artık eski riskli toplu silme akışını çalıştırmaz; v3 uygulamasını açar.
