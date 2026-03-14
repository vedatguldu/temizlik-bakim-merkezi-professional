# LicensingServer

Bu proje, Temizlik ve Bakım Merkezi Professional uygulamasının **son kullanıcıdan gizli** lisans yönetim arka servisidir.

## Amaç

- Son kullanıcı uygulaması sadece `Lisans Etkinleştir` ekranını görür.
- Lisans üretme/iptal/cihaz sıfırlama gibi sahip işlemleri bu sunucuda yönetilir.

## Çalıştırma

```powershell
cd ./LicensingServer
dotnet run
```

Varsayılan sağlık kontrolü:

- `GET /health`

Swagger (geliştirme ortamında):

- `https://localhost:<port>/swagger`

## Önemli Ortam Değişkenleri

- `TBM_ADMIN_TOKEN`: Admin endpoint'leri için zorunlu gizli token.
- `ASPNETCORE_URLS`: Sunucu URL ayarı (ör. `http://0.0.0.0:5084`).

## Admin Endpoint'leri

Bu endpoint'ler için `X-Admin-Token` header'ı gerekir.

- `GET /api/v1/admin/licenses`
- `POST /api/v1/admin/licenses`
- `POST /api/v1/admin/licenses/{key}/deactivate`
- `POST /api/v1/admin/licenses/{key}/activate`
- `POST /api/v1/admin/licenses/{key}/reset-machines`

## Son Kullanıcı Endpoint'i

- `POST /api/v1/license/activate`

Uygulama bu endpoint ile anahtarı doğrular ve Pro özellikleri açar.

## Not

Üretimde bu servisi HTTPS arkasında yayınlayın ve `TBM_ADMIN_TOKEN` değerini güçlü bir gizli anahtar ile değiştirin.
