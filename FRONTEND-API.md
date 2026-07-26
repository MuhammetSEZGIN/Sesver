# Frontend API Sözleşmesi

Bu dosya, **backend kodunu okumaya gerek kalmadan** frontend geliştirmek için gereken
her şeyi içerir: route'lar, gövde şemaları, SignalR hub metotları ve event'leri,
hata davranışları ve tuzaklar.

**Son güncelleme:** 2026-07-25
**Kaynak:** Controller/Hub kodundan doğrudan okunarak yazıldı (swagger tahmini değil).

> Bir şey burada yazılmıyorsa **yok varsayın** ve backend'e sorun. Var olmayan bir
> route'u çağırmak sessiz hatalara yol açar (aşağıda "401 tuzağı"na bakın).

---

## 0. Temel kurallar — önce bunu okuyun

### 0.1 İki farklı giriş kapısı var

| Ne | Nereden gidilir | Auth nasıl |
| --- | --- | --- |
| **REST API** | API Gateway (`/identity`, `/message`, `/clan`, `/voice`) | `Authorization: Bearer <token>` header |
| **SignalR hub'ları** | **Gateway'i BYPASS eder** — nginx doğrudan servise proxy'ler | `?access_token=<token>` **query string** |

⚠️ **Bu en sık yapılan hata.** Hub'lar `Authorization` header'ı ile çalışmaz; SignalR
WebSocket handshake'inde header gönderemediği için token query string'den okunur.

```js
// DOĞRU
new HubConnectionBuilder()
  .withUrl(`${BASE}/messagehub?access_token=${token}`)

// YANLIŞ — 401 alırsınız
  .withUrl(`${BASE}/messagehub`, { headers: { Authorization: `Bearer ${token}` } })
```

### 0.2 Base URL'ler

| Ortam | REST | Hub |
| --- | --- | --- |
| Local | `http://localhost:5000` | `http://localhost:5107` (message), `http://localhost:5241` (presence), `http://localhost:5160` (notification) |
| Prod | `https://voxify.com.tr` | `https://voxify.com.tr` (nginx aynı origin'den proxy'ler) |

Prod'da hepsi aynı origin — local'de hub'lar servise doğrudan gider, gateway portuna değil.

### 0.3 ⚠️ 401 tuzağı (bilinmesi şart)

Gateway, **var olmayan bir route** için de eskiden `401` dönüyordu. Bu düzeltildi:
artık eşleşmeyen route `404` + şu gövdeyi döner:

```json
{ "type": "...", "title": "Route not found", "status": 404, "detail": "No route matches path '/foo'." }
```

**Frontend'in `api.js` interceptor'ı için kritik:** `401` gördüğünde token yenilemeyi
deneyin, ama `404 + "title": "Route not found"` gördüğünüzde **kullanıcıyı login'e
atmayın** — bu bir kod hatası (yanlış route), oturum sorunu değil.

```js
if (err.response?.status === 404 && err.response.data?.title === "Route not found") {
  console.error("Yanlış route çağrıldı:", err.config.url);   // login'e atma!
  return Promise.reject(err);
}
```

### 0.4 Rate limit

Aşılırsa **`429`** + `"Too many requests. Please try again later."`

**İki katman var ve ikisi de geçerli:**

| Katman | Kapsam | Limit |
| --- | --- | --- |
| Gateway | `/identity/*` | saniyede **5** |
| Gateway | `/message/*`, `/clan/*`, `/voice/*` | saniyede **20** |
| **MessageService (kendi içinde)** | `/message/*` **REST** uçları | **30 saniyede 10** |

⚠️ **En sıkı limit MessageService'in kendi limiti: 30 saniyede 10 istek.** Gateway'in
20/sn'sine bakıp rahat olmayın — DM listesi + geçmiş + sayfalama isteklerini üst üste
atarsanız 30 saniyelik pencereyi hızla doldurursunuz. Sohbet açılışında paralel
`GET /message` fırlatmak yerine sıraya alın.

**Kuyruk yok** (`QueueLimit = 0`) — limit dolduğunda istek beklemez, anında `429` olur.

`429`'u retry etmeden önce bekleyin; interceptor'da otomatik retry yaparsanız
kilitlenirsiniz. **SignalR hub'ları bu limitlere tabi değildir** — yoğun mesajlaşmayı
REST değil hub üzerinden yapmanızın bir nedeni de bu.

---

## 1. Auth — `/identity`

| Metot | Yol | Auth | Gövde |
| --- | --- | --- | --- |
| POST | `/identity/register` | — | `{ userName, email, password, passwordConfirmation, deviceInfo, avatarUrl? }` |
| POST | `/identity/login` | — | `{ userName, password, deviceInfo? }` |
| POST | `/identity/forgot-password` | — | `{ email }` |
| POST | `/identity/reset-password` | — | `{ email, token, newPassword, newPasswordConfirmation }` |
| POST | `/identity/refresh-token` | — | `{ refreshToken, userId }` |
| GET | `/identity/confirm-email?userId=&token=` | — | — |
| POST | `/identity/resend-confirmation-email` | — | `{ email }` |
| GET | `/identity/my-sessions` | ✅ | — |
| POST | `/identity/logout-session/{sessionId}` | ✅ | — |

⚠️ **Login `email` DEĞİL `userName` ister.** `{ email, password }` gönderirseniz
`400` + `"Username is required"` alırsınız. Parola en az 6 karakter.

⚠️ **`register`'da `deviceInfo` ZORUNLU** (`"Device Info is required"`) ve
`passwordConfirmation`, `password` ile birebir aynı olmalı. `deviceInfo`'yu login'de
de göndermek oturum listesinde (`/my-sessions`) cihazın anlamlı görünmesini sağlar —
örn. `"Chrome 120 / macOS"`.

`forgot-password` güvenlik nedeniyle e-posta sistemde kayıtlı olsun veya olmasın her
zaman aynı `200` yanıtını verir. E-postadaki bağlantı frontend'in
`/reset-password?email=...&token=...` sayfasını açar; bu iki query değeri
`reset-password` isteğinin gövdesine taşınmalıdır. Bağlantı tek kullanımlıktır ve 6
saat geçerlidir. Başarılı sıfırlama kullanıcının bütün access ve refresh token
oturumlarını geçersiz kılar. Frontend elindeki token'ları silip login ekranına
yönlendirmelidir.

**Login/refresh yanıtı** `ApiResponse<T>` sarmalayıcısıyla döner — `statusCode` gövdenin
**içinde de** vardır:

```json
{
  "isSuccessfull": true,
  "statusCode": 200,
  "message": "...",
  "data": { "userID": "...", "accessToken": "...", "refreshToken": "..." }
}
```

⚠️ **Alan adlarına dikkat** — kolay karıştırılır:

- `isSuccessfull` — **çift L** (`isSuccess` değil)
- `data.accessToken` — (`token` değil)
- `data.userID` — **büyük ID** (`userId` değil)
- Hata durumunda `errors: string[]` ve/veya `errorsByField: { alan: mesaj }` gelir;
  bunlar `null` ise **gövdede hiç görünmez** (`WhenWritingNull`), `?.` ile erişin.

`data`'ya erişirken `response.data.data` olduğunu unutmayın (axios + sarmalayıcı).

---

## 2. Kullanıcı — `/identity/user`

| Metot | Yol | Açıklama |
| --- | --- | --- |
| GET | `/identity/user/me` | Kendi profilim |
| PUT | `/identity/user/update` | `{ userName, bio?, avatarUrl? }` |
| PUT | `/identity/user/email` | `{ email }` — adresi değiştirir ve doğrulama maili yollar |
| POST | `/identity/user/change-password` | `{ currentPassword, newPassword }` |
| GET | `/identity/user/search?q=&page=&limit=` | Kullanıcı ara (arkadaş eklemek için) |

`POST /change-password` başarılı olduğunda mevcut cihaz dahil bütün oturumlar
kapatılır. Başarı yanıtından hemen sonra frontend access/refresh token'larını ve
oturuma bağlı kullanıcı state'ini temizleyip login ekranına yönlendirmelidir.

Gateway aşağıdaki `401` yanıtını döndürürse refresh denenmemeli; token'lar doğrudan
silinip login ekranına geçilmelidir:

```json
{ "message": "Session is no longer valid. Please sign in again." }
```

Oturum sürümü doğrulanırken ortak Redis geçici olarak erişilemiyorsa `503`
döner. Bu durumda oturum silinmemeli; kullanıcıya tekrar deneme gösterilmelidir.

`GET /me` → **`UserMeDto`**
```json
{ "id": "...", "userName": "...", "email": "...", "bio": "...",
  "avatarUrl": "...", "emailConfirmed": true }
```

`PUT /email` JWT gerektirir. Başarılı olduğunda `emailConfirmed` değeri `false`
olur ve yeni adrese doğrulama bağlantısı gönderilir. Aynı doğrulanmamış adres tekrar
gönderilirse adres değiştirilmeden doğrulama maili yeniden yollanır.

`GET /search` → **`UserSearchResultDto[]`** (query param adı **`q`**)
```json
[{ "id": "...", "userName": "...", "avatarUrl": "..." }]
```

---

## 3. Arkadaşlık — `/identity/friendship`

| Metot | Yol | Gövde / Not |
| --- | --- | --- |
| GET | `/identity/friendship` | Arkadaş listesi |
| GET | `/identity/friendship/requests` | **Bana gelen** bekleyen istekler |
| POST | `/identity/friendship/requests` | `{ addresseeId }` |
| POST | `/identity/friendship/requests/{id}/accept` | `id` = **friendship Guid'i**, userId değil |
| POST | `/identity/friendship/requests/{id}/reject` | " |
| DELETE | `/identity/friendship/{friendUserId}` | Burada **userId** kullanılır |

Hepsi → **`FriendshipReadDto`**
```json
{ "id": "guid", "userId": "...", "userName": "...", "avatarUrl": "...",
  "status": "Pending" | "Accepted" | "Blocked",
  "createdAt": "...", "respondedAt": null }
```

⚠️ **`id` vs `userId` karışıklığı:** `accept`/`reject` **friendship `id`**'sini
(Guid) ister; `DELETE` ise karşı tarafın **`userId`**'sini ister. `userId`
gönderilirse `404` alırsınız.

**Faydalı davranış:** A→B isteği beklerken B→A istek gönderirse, backend
otomatik **kabul** eder ve `"Friend request accepted"` döner (çift kayıt oluşmaz).

---

## 4. DM (Direct Message) — `/message`

### 4.1 Konuşma yönetimi

| Metot | Yol | Gövde |
| --- | --- | --- |
| POST | `/message/dm/conversations` | `{ otherUserId }` |
| GET | `/message/dm/conversations` | — |

**`DmConversationDto`** (ikisi de bunu döner):
```json
{
  "conversationId": "68a1f...",
  "otherUserId": "...",
  "otherUserName": "ahmet",
  "otherAvatarUrl": "https://...",
  "lastMessage": "görüşürüz",
  "lastMessageAt": "2026-07-25T18:00:00Z",
  "createdAt": "2026-07-20T10:00:00Z"
}
```

> ### ✅ `conversationId` === `channelId`
> **Aynı değerdir.** DM mesajlarını çekerken/gönderirken `conversationId`'yi
> doğrudan `channelId` olarak kullanın. Eşleme katmanı gerekmiyor.
> `lastMessage`/`lastMessageAt` mesaj yoksa `null` gelir.
>
> `POST` idempotenttir — aynı kişi için tekrar çağırmak **yeni konuşma açmaz**,
> mevcut olanı döner. "Zaten var mı?" kontrolü yapmanız gerekmez.

### 4.2 Mesaj geçmişi

```
GET /message?channelId={conversationId}&limit=20&page=1
```

**`MessageDto[]`** — en yeniden eskiye sıralı:
```json
[{ "id": "68a1f...", "clanId": null, "channelId": "...", "userName": "ahmet",
   "senderId": "...", "avatarUrl": "...", "text": "selam",
   "createdAt": "2026-07-25T18:00:00Z" }]
```

- `id` **her zaman düz string**'dir (`{$oid}` gibi bir şey gelmez).
- `limit` üst sınırı **100**'dür (fazlası sessizce 100'e iner); `limit<=0` → 20.
- Katılımcı değilseniz **`403`**. Klan kanalı id'si verirseniz **`400`**
  (bu route yalnızca DM içindir).

### 4.3 Desteklenmeyenler

`PUT`/`DELETE /message/dm/{messageId}` **yoktur.** Mesaj düzenleme/silme
**yalnızca hub üzerinden** yapılır (§5.2).

### 4.4 Çağırmayın: iç endpoint

`GET /message/dm/conversations/{conversationId}/is-participant` gateway üzerinden
erişilebilir ama **servisler arası** kullanım içindir (VoiceService yetki kontrolü).
Frontend'in buna ihtiyacı yok — katılımcı olmadığınızda ilgili uçlar zaten `403` döner.

---

## 5. Mesaj Hub'ı (canlı mesajlaşma) — `/messagehub`

```js
const conn = new HubConnectionBuilder()
  .withUrl(`${HUB_BASE}/messagehub?access_token=${token}`)
  .withAutomaticReconnect()
  .build();
await conn.start();
```

### 5.1 Çağıracağınız metotlar

| Metot | İmza | Not |
| --- | --- | --- |
| `JoinChannel` | `(channelId)` | Mesaj almaya başlamak için **şart** |
| `LeaveChannel` | `(channelId)` | |
| `SendMessage` | `(channelId, clanId, message)` | **DM'de `clanId = null`** |
| `UpdateMessage` | `(messageId, newContent)` | Sadece kendi mesajınız |
| `DeleteMessage` | `(messageId, channelId)` | Sadece kendi mesajınız |

> ### ✅ `SendMessage` ile `clanId = null` desteklenir
> DM için `invoke('SendMessage', conversationId, null, text)` doğru kullanımdır.
> Mesaj `clanId: null` ile kaydedilir, yayın `channelId` grubuna yapılır,
> exception atılmaz. Ayrı bir `SendDirectMessage` metodu **yoktur**, gerekmez.

**Sıra önemli:** `JoinChannel` çağırmadan `SendMessage` yaparsanız mesaj kaydedilir
ama **kendi ekranınıza düşmez** (gruba abone değilsiniz). Sohbet açılırken önce
`JoinChannel`.

### 5.2 Dinleyeceğiniz event'ler

| Event | Payload | Ne zaman |
| --- | --- | --- |
| `ReceiveMessage` | `MessageDto` | Yeni mesaj |
| `MessageUpdated` | `MessageDto` | Mesaj düzenlendi |
| `MessageDeleted` | `messageId` (string) | Mesaj silindi |
| `MessageSendFailed` | `string` (sebep) | Yetkisiz kanal |
| `MessageUpdateFailed` | `messageId` | Sahibi değilsiniz / hata |
| `MessageDeleteFailed` | `messageId` | " |
| `JoinChannelFailed` | `channelId` | DM katılımcısı değilsiniz |

⚠️ **Hata event'leri sessizdir** — hub metodu exception atmaz, bunun yerine
`*Failed` event'i gönderir. Bunları dinlemezseniz hatalar **hiç görünmez.**

### 5.3 Optimistic UI notu

`SendMessage` çağıran kişi de `ReceiveMessage`'ı alır (kendi mesajı dahil). Optimistic
render yapıyorsanız `id` ile dedupe edin, yoksa mesaj iki kez görünür.

---

## 6. Presence Hub'ı — `/hubs/presence`

```js
new HubConnectionBuilder().withUrl(`${HUB_BASE}/hubs/presence?access_token=${token}`)
```

### 6.1 Online durum

| Metot | İmza | Not |
| --- | --- | --- |
| `SubscribeToClans` | `(clanIds[])` | Klan üyelerinin online durumu |
| `SubscribeToConversations` | `(conversationIds[])` | DM ses odası olayları için |
| `SubscribeToUsers` | `(friendUserIds[])` | Arkadaş push aboneliği + anlık snapshot; önceki listeyi değiştirir |
| `GetOnlineUsers` | `(userIds[])` | Yalnızca arkadaşlar için yetkili anlık sorgu |

| Event | Payload |
| --- | --- |
| `UserOnline` | `userId` |
| `UserOffline` | `userId` |
| `OnlineUsers` | `userId[]` (`SubscribeToUsers` ve `GetOnlineUsers` yanıtı) |
| `SubscriptionFailed` | `"authorization-unavailable"` |

Arkadaş olmayan kimlikler sessizce filtrelenir. Çoklu sekme/cihaz desteklenir:
`UserOnline` ilk bağlantıda, `UserOffline` son bağlantı kapandığında yayınlanır.

### 6.2 Ses kanalı presence

| Metot | İmza |
| --- | --- |
| `JoinVoiceChannel` | `(clanId, voiceChannelId, userName)` — **DM'de `clanId = null`** |
| `LeaveVoiceChannel` | `()` — parametresiz, bağlantıdan bulur |
| `GetVoiceChannelParticipants` | `(clanId)` |

| Event | Payload |
| --- | --- |
| `UserJoinedVoice` | `{ clanId, voiceChannelId, userId, userName }` |
| `UserLeftVoice` | `{ clanId, voiceChannelId, userId }` |
| `VoiceChannelParticipants` | `{ clanId, participants: [{voiceChannelId, userId, userName}] }` |

**DM ses odası için:**
```js
// voiceChannelId formatı: "dm-{conversationId}"
conn.invoke('SubscribeToConversations', [conversationId]);   // önce abone ol
conn.invoke('JoinVoiceChannel', null, `dm-${conversationId}`, userName);
```
DM olaylarında yayın `conversation_{conversationId}` grubuna gider ve payload'da
`clanId: null` gelir — klan mantığıyla ayırt etmek için bunu kullanın.

**Bağlantı koparsa** backend `UserLeftVoice`'ı otomatik yayınlar; elle `LeaveVoiceChannel`
çağırmanız gerekmez (ama düzgün çıkışta çağırın).

### 6.3 DM sesli arama sinyalleşmesi

| Metot | İmza |
| --- | --- |
| `CallUser` | `(conversationId)` |
| `AcceptCall` / `RejectCall` / `CancelCall` / `EndCall` | `(callId)` |

Event'ler: `IncomingCall`, `CallRinging`, `CallAccepted`, `CallRejected`,
`CallCancelled`, `CallTimedOut`, `CallBusy`, `CallEnded`, `CallFailed`.
Çağrı payload'ı `callId`, `conversationId`, tarafların userId'leri, `roomId`, `status`,
`createdAt` ve `expiresAt` içerir. `CallAccepted` sonrasında `roomId` için §7'deki
mevcut LiveKit token akışı kullanılır; ses Presence üzerinden taşınmaz.

---

## 7. Sesli görüşme (LiveKit) — `/voice`

```
GET /voice/join-room/{roomId}     →  { "token": "<livekit-jwt>" }
```

| Oda türü | `roomId` |
| --- | --- |
| Klan ses kanalı | `{voiceChannelId}` |
| **DM ses odası** | **`dm-{conversationId}`** |

> ### ✅ DM için `dm-{conversationId}` kullanılır
> Ayrı endpoint **yok**. Backend, `dm-` önekli oda için yetkiyi klan üyeliğinden değil
> **DM katılımcılığından** doğrular; taraflardan biri değilseniz **`403`** alırsınız.

Token **6 saat** geçerli; `canPublish`/`canSubscribe`/`canPublishData` açık.
LiveKit sunucu adresi prod'da `wss://voxify.com.tr/livekit`.

**Akış:**
```js
const { token } = await api.get(`/voice/join-room/dm-${conversationId}`).then(r => r.data);
await room.connect(LIVEKIT_URL, token);
presenceConn.invoke('JoinVoiceChannel', null, `dm-${conversationId}`, userName);
```
LiveKit'in kendi katılımcı listesi odaya **girdikten sonra** kimin bağlı olduğunu verir;
Presence ise **girmeden önce** "karşı taraf seste mi?" bilgisini sağlar. İkisi farklı iş.

---

## 8. Bildirimler — `/notification`, `/hubs/notification`

| Metot | Yol |
| --- | --- |
| GET | `/notification?page=1&limit=20&unreadOnly=false` |
| GET | `/notification/unread-count` |
| POST | `/notification/{id}/read` |
| POST | `/notification/read-all` |
| DELETE | `/notification/{id}` |

Liste yanıtı `{ items, page, limit, total }`, unread-count yanıtı `{ count }` biçimindedir.
Hub bağlantısı `/hubs/notification?access_token={token}` üzerinden kurulur ve
`ReceiveNotification` ile `UnreadCountChanged` event'leri dinlenir.

---

## 9. Klanlar — `/clan`, `/channel`, `/voiceChannel`, `/clanMembership`, `/role`

DM tarafına odaklandıysanız bu bölümü atlayabilirsiniz. Dikkat edilecek tek şey:
**klan route'larının çoğunda `clanId` yol içinde geçer** ve gateway bunu görüp
rol bilgisini (`X-Clan-Role`) arka servise ekler.

| Alan | Örnek route'lar |
| --- | --- |
| Klan | `POST /clan` · `GET /clan` · `GET /clan/user` · `GET`/`PUT`/`DELETE` `/clan/clanId/{clanId}` |
| Metin kanalı | `POST`/`GET`/`PUT` `/channel/clanId/{clanId}` · `GET` ve `DELETE` `/channel/channel/{channelId}/clanId/{clanId}` |
| Ses kanalı | `POST`/`GET`/`PUT` `/voiceChannel/clanId/{clanId}` · `GET /voiceChannel/{voiceChannelId}` · `DELETE /voiceChannel/channel/{voiceChannelId}/clanId/{clanId}` |
| Üyelik | `GET /clanMembership/clanId/{clanId}` · `GET /clanMembership/user/{userId}` · `POST /clanMembership/invitations/clanId/{clanId}` · `POST /clanMembership/join` · `DELETE /clanMembership/member/{userId}/clanId/{clanId}` · `DELETE /clanMembership/user/clanId/{clanId}` (klandan ayrıl) |
| Rol | `PUT /role/clanId/{clanId}` |

⚠️ **`clanId` yoldan düşerse yetki de düşer.** Gateway rolü yalnızca yolda
`/clanId/{guid}` deseni varken çözer. Klan mesaj geçmişi için
`GET /message/channelId/{channelId}/clanId/{clanId}` kullanılır — DM'deki
`GET /message?channelId=` **query** biçiminden farklıdır ve rol gerektirir
(`OWNER`/`ADMIN`/`MEMBER`).

Klan mesajı gönderirken hub'da `clanId` **dolu** geçilir:
`invoke('SendMessage', channelId, clanId, text)`.

---

## 10. Henüz olmayan / planlanan

Bunları **çağırmayın** — yoksa 404 alırsınız. Durum takibi:
`plan-bildirimler-arkadaslik-arama.md`

| Özellik | Durum |
| --- | --- |
| DM mesaj düzenleme/silme (REST) | ❌ Yok — hub kullanın (§5.2) |
| Okunmadı sayacı / okundu bilgisi | ❌ Yok — "aktif sohbet" modeline bağlı (backlog 4.4) |
| Yazıyor… (typing) göstergesi | ❌ Yok |

---

## 11. Hata kodları özeti

| Kod | Anlamı | Ne yapmalı |
| --- | --- | --- |
| `400` | Geçersiz gövde / yanlış route'a doğru olmayan id | Gövdeyi/parametreyi düzelt |
| `401` | Token yok / süresi doldu | Refresh dene, olmazsa login |
| `403` | Yetki yok (DM katılımcısı değil, oda sahibi değil) | **Login'e atma** — erişim yok |
| `404` + `"Route not found"` | **Route yok = kod hatası** | **Login'e atma** — URL'i düzelt |
| `404` | Kayıt bulunamadı | Normal "yok" durumu |
| `429` | Rate limit | Bekle, otomatik retry yapma |

Hub tarafında hatalar HTTP kodu değil **`*Failed` event'i** olarak gelir (§5.2).
