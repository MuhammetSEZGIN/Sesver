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

## 2. Deploy

`main`'e push yeterli — `.github/workflows/deploy.yml` her şeyi yapar. Elle
komut çalıştırmanız gerekmez.

Workflow sırasıyla:

1. **Conflict koruması** — sunucuda commit'lenmemiş değişiklik varsa ya da
   geçmiş ayrışmışsa deploy hiç başlamaz. `git pull` yerine
   `fetch` + `merge --ff-only` kullanılır, böylece conflict marker'lı bir
   çalışma ağacı oluşmaz ve eski sürüm çalışmaya devam eder.
2. **`cert-bootstrap`** — geçici self-signed sertifika. nginx eksik
   `ssl_certificate` ile hiç başlamaz, certbot'un HTTP-01 doğrulaması ise
   nginx'in `:80`'de ayakta olmasını ister; bu adım o kilidi kırar. Gerçek
   sertifika zaten varsa kendini atlar.
3. **`up -d --build`**
4. **`certbot-init`** — yalnızca gerçek sertifika yoksa. Let's Encrypt rate
   limit'ine takılmamak için `renewal/app.<domain>.conf` varlığına bakılır; bu
   dosya yalnızca certbot'un kendi aldığı sertifikada oluşur, self-signed
   bootstrap'ta oluşmaz. Ardından nginx reload edilir.

Sonraki yenilemeleri mevcut `certbot` servisi devralır.

### Elle çalıştırmak gerekirse

```bash
docker compose run --rm cert-bootstrap
docker compose up -d --build
docker compose run --rm certbot-init
docker compose exec nginx nginx -s reload
```

## 3. Doğrulama

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
