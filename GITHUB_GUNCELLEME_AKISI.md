# GitHub Güncelleme Akışı

Bu doküman, Temizlik ve Bakım Merkezi Professional uygulamasının GitHub tabanlı dağıtım ve güncelleme akışını açıklar.

## Mimari Özet
- Kaynak kodu GitHub reposunda tutulur.
- Yeni sürümler GitHub Releases üzerinden yayınlanır.
- Uygulama, açılışta `releases/latest` API'sini denetler.
- Yeni sürüm varsa kullanıcıya güncelleme penceresi gösterilir.
- Kullanıcı onaylarsa setup indirme bağlantısı açılır.

## Zorunlu Bileşenler
- GitHub repo
- Release etiketleri (ör. `v3.1.0`)
- Release içinde setup `.exe` dosyası

## Uygulama Tarafı
- `GitHubUpdateService` sınıfı release API sorgusu yapar.
- `MainWindow` açılışında otomatik kontrol tetikler.
- Dashboard üzerinde manuel `_Güncelleme Denetle` butonu vardır.

## Sunucu Gerekli mi?
- Kendi sunucunuz zorunlu değildir.
- GitHub Releases tek başına güncelleme dağıtımı için yeterlidir.
- Daha kurumsal ihtiyaçlar için Azure Blob, S3 veya kendi API'niz kullanılabilir.

## Güvenlik ve Operasyon Notları
- Release dosyalarını imzalamak (code signing) güvenilirliği artırır.
- Her release için değişiklik notu ekleyin.
- Uygulamanın kontrol ettiği repo adı sabit kalmalıdır.
