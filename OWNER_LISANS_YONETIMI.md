# Sahip Lisans Yönetimi Rehberi

Bu rehber, uygulama sahibi olarak lisansları son kullanıcıdan gizli şekilde nasıl yöneteceğinizi anlatır.

## Mimari

- Son kullanıcı uygulaması: yalnızca lisans etkinleştirme/doğrulama.
- Lisans API: `LicensingServer`.
- Sahip yönetimi: `X-Admin-Token` ile korunan `/api/v1/admin/*` endpoint'leri.

## 1) Lisans Sunucusunu Çalıştır

```powershell
cd ./LicensingServer
$env:TBM_ADMIN_TOKEN = "GUCLU_BIR_TOKEN"
dotnet run
```

## 2) Uygulamayı Sunucuya Bağla

İstemci tarafında ortam değişkeni:

```powershell
$env:TBM_LICENSE_API_URL = "https://license.temizlikbakimmerkezi.com"
```

## 3) Yeni Lisans Üret

```powershell
$token = "GUCLU_BIR_TOKEN"
$body = @{
  planName = "PRO_LIFETIME"
  maxMachines = 2
  features = @("pro.smart-plan", "pro.risk-forecast", "pro.analytics", "pro.backup")
} | ConvertTo-Json

Invoke-RestMethod -Method Post \
  -Uri "https://license.temizlikbakimmerkezi.com/api/v1/admin/licenses" \
  -Headers @{ "X-Admin-Token" = $token } \
  -ContentType "application/json" \
  -Body $body
```

## 4) Lisansı Pasifleştir / Aktifleştir

```powershell
Invoke-RestMethod -Method Post \
  -Uri "https://license.temizlikbakimmerkezi.com/api/v1/admin/licenses/TBM-PRO-LIFETIME-AB12-CD34/deactivate" \
  -Headers @{ "X-Admin-Token" = $token }

Invoke-RestMethod -Method Post \
  -Uri "https://license.temizlikbakimmerkezi.com/api/v1/admin/licenses/TBM-PRO-LIFETIME-AB12-CD34/activate" \
  -Headers @{ "X-Admin-Token" = $token }
```

## 5) Cihazları Sıfırla

```powershell
Invoke-RestMethod -Method Post \
  -Uri "https://license.temizlikbakimmerkezi.com/api/v1/admin/licenses/TBM-PRO-LIFETIME-AB12-CD34/reset-machines" \
  -Headers @{ "X-Admin-Token" = $token }
```

## Güvenlik Notları

- `TBM_ADMIN_TOKEN` değerini asla repoya yazmayın.
- API'yı HTTPS arkasında yayınlayın.
- Admin endpoint'leri için IP kısıtı + WAF önerilir.
