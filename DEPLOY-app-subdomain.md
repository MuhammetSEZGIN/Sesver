# API'yi app.voxify.com.tr'ye taşıma

Uygulama içi istekler artık `app.voxify.com.tr` üzerinden gider. Kök domain
(`voxify.com.tr`) web uygulaması için Netlify'a bırakılmıştır — sunucudaki
nginx artık kök domaini karşılamaz.

## Ön koşullar

- `app.voxify.com.tr` A kaydı sunucu IP'sine yönlendirilmiş olmalı (yapıldı).
- `voxify.com.tr` / `www` kayıtları Netlify'a taşınmalı. Bu kayıtlar hâlâ
  sunucuyu gösteriyorsa, kök domaine gelen istekler artık karşılıksız kalır.

## 1. Sunucudaki `.env`

`CORS_ORIGIN_3` ve `CORS_ORIGIN_4` yeni eklendi — masaüstü uygulaması artık
sabit kodlu listeden değil buradan besleniyor. Eksik bırakılırsa Tauri
istemcisi CORS'a takılır.

```bash
# ── CORS ──
CORS_ORIGIN_1=https://voxify.com.tr
CORS_ORIGIN_2=https://www.voxify.com.tr
CORS_ORIGIN_3=tauri://localhost
CORS_ORIGIN_4=https://tauri.localhost
```

`SERVER_DOMAIN` değişmez (`voxify.com.tr`); nginx `app.` önekini kendisi ekler.

## 2. SSL sertifikası — bir kez, elle

Sertifika kurulumu **deploy workflow'unun parçası değildir**. Tek seferlik bir
iştir; her deploy'da tekrarlanması hem gereksiz olurdu hem de sertifika
adımındaki bir hata ilgisiz bir kod deploy'unu durdururdu.

Sunucuda, `app.` alan adına geçerken bir kez:

nginx sertifikayı `/etc/letsencrypt/active/app.<domain>/` yolundan okur.
Bu dizin başta self-signed dosyaları içerir, gerçek sertifika alınınca
certbot'un `live/` dizinine symlink'e dönüşür. nginx doğrudan `live/`'ı
okusaydı sertifika alınmadan hiç başlayamaz, certbot da nginx olmadan
doğrulama yapamazdı.

```bash
cd ~/Sesver

# 1) Gecici self-signed sertifika (active/ dizinine).
#    certbot'un live/ dizinine dokunulmaz.
docker compose run --rm cert-bootstrap

# 2) Servisleri baslat — nginx artik ayaga kalkabilir
docker compose up -d --build

# 3) Gercek sertifikayi al.
#    app.voxify.com.tr DNS'te sunucuya cozuluyor olmali (HTTP-01 dogrulamasi).
#    LETSENCRYPT_EMAIL .env'de tanimli degilse basina ekleyin:
#      LETSENCRYPT_EMAIL=... docker compose run --rm certbot-init
docker compose run --rm certbot-init

# 4) active/ dizinini gercek sertifikaya baglayip nginx'i yeniden yukle
docker compose run --rm cert-bootstrap
docker compose exec nginx nginx -s reload
```

Bundan sonraki yenilemeleri `certbot` servisi otomatik devralır; `active/`
artık symlink olduğu için yenilenen sertifika kendiliğinden devreye girer.

## 3. Kod deploy'u

`main`'e push yeterli — `.github/workflows/deploy.yml` çalışır:

1. **Conflict koruması** — sunucuda takip edilen bir dosya elle değişmişse ya
   da geçmiş ayrışmışsa deploy hiç başlamaz. `git pull` yerine
   `fetch` + `merge --ff-only` kullanılır, böylece conflict marker'lı bir
   çalışma ağacı oluşmaz ve eski sürüm çalışmaya devam eder.
   Takip edilmeyen dosyalar (`.env` gibi) deploy'u engellemez.
2. `docker compose down && up -d --build`
3. `docker image prune -f`

Deploy conflict yüzünden durursa sunucuda:

```bash
cd ~/Sesver && git checkout -- <dosya>
```

## 4. Doğrulama

```bash
curl -I https://app.voxify.com.tr/update/windows-x86_64/1.0.0

# CORS: Tauri origin'i icin izin donmeli
curl -s -i -X OPTIONS https://app.voxify.com.tr/identity/login \
  -H "Origin: https://tauri.localhost" \
  -H "Access-Control-Request-Method: POST" | grep -i access-control
```

## Nelerin değiştiği

**CORS artık hiçbir serviste sabit kodlu değil.** Önceden her serviste
`defaultOrigins` dizisi vardı ve ortam değişkeniyle *daraltmak* mümkün
değildi. Artık tüm origin'ler `Cors__AllowedOrigins__N` üzerinden gelir:

| Servis | Durum |
| --- | --- |
| ApiGateway | Sabit liste kaldırıldı |
| MessageService | Sabit liste kaldırıldı |
| PresenceService | Sabit liste kaldırıldı |
| NotificationService | Zaten config'den okuyordu; appsettings temizlendi |
| ClanService | Sabit liste kaldırıldı, compose'a CORS env eklendi |

PresenceService ve NotificationService nginx'te API Gateway'i baypas edip
doğrudan çağrıldığı için kendi CORS ayarları gerçekten gereklidir. Gateway
arkasındaki servislerde (Clan, Message REST) şimdilik ölü koddur; servisler
ayrı sunuculara dağıtılırsa gerekli hale gelir.

**İstemci tarafı:**

- `sesver-react/.env` → tüm adresler `app.voxify.com.tr`
- `sesver-react/src-tauri/tauri.conf.json` → updater endpoint'i `app.`
- `sesver-admin` → `app.` (varsayılan olarak dev proxy üzerinden)

Masaüstü istemcinin yeni adrese geçmesi için **yeni bir sürüm yayınlanması**
gerekir; mevcut kurulumlar `.env`'i build sırasında gömdüğü için hâlâ eski
adrese istek atar.

**Lokal geliştirme:** `run-local.sh` içindeki `LOCAL_CORS_ARGS` dizisi CORS
kullanan beş servise de aynı origin listesini verir (5173, 5174, tauri, 1420).
