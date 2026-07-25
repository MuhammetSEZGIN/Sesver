# Plan — Arkadaşlık Aboneliği + Bildirimler + DM Sesli Arama

**Tarih:** 2026-07-25
**Kapsam:** Üç birbirine bağlı iş: (1) Presence'a kişi-bazlı abonelik,
(2) kalıcı bildirim altyapısı (ayrı NotificationService), (3) DM'de zilli sesli arama.

**Karar verilenler:**
- Bildirimler **ayrı NotificationService** olarak kurulacak (kalıcı, kendi DB'si).
- Sesli arama **gerçek çağrı** olacak: zil + kabul/reddet/zaman aşımı.

---

## 0. Ön koşul — mevcut durumun tespiti

Kod okunarak doğrulandı (tahmin değil):

| Bulgu | Durum |
| --- | --- |
| `PresenceHub` abonelikleri | `SubscribeToClans` + `SubscribeToConversations` var; **kişi-bazlı abonelik yok** |
| `PresenceRepository` | Tamamen **in-memory** (`ConcurrentDictionary`), Redis yok |
| PresenceService ölçeklenmesi | **Tek instance** (`container_name`, replica yok, backplane yok) |
| Bildirim altyapısı | **Hiç yok** — sıfırdan kurulacak |
| Arkadaşlık verisi | `IdentityService` / Postgres / EF (`Friendship`, `FriendshipStatus`) |
| RabbitMQ | MassTransit her serviste kurulu, `Shared.Contracts` ortak mesaj tipleri |

### ⚠️ Taşınması gereken kısıt

`PresenceRepository` in-memory olduğu için **PresenceService birden fazla instance'a
çıkarılırsa** hem mevcut presence hem aşağıdaki çağrı state'i bozulur (bir instance'daki
zil, diğerindeki kullanıcıya ulaşmaz). Bugün tek instance olduğu için çalışır.

**Öneri:** Bu plandaki işler in-memory ile yapılsın, ama **Redis backplane** ayrı bir
madde olarak backlog'a yazılsın (aşağıda madde 4). Çağrı state machine'i in-memory
yazılırken arayüzün (`ICallRepository`) arkasına saklanacak ki Redis'e geçiş tek
sınıf değişikliği olsun.

### ⚠️ Ön koşul 0.1 — Presence kullanıcı başına TEK bağlantı tutuyor (önce düzeltilmeli)

**Ölçüm:** `PresenceRepository._userConnections` şu tipte:
```csharp
ConcurrentDictionary<string, string>   // userId → connectionId  (TEK)
```
`SetUserOnline(userId, connectionId)` atama yapıyor (`_userConnections[userId] = connectionId`),
yani **ikinci sekme/cihaz birincinin kaydını ezer.** `SetUserOffline(userId)` ise
kullanıcının tüm kaydını siler.

**Sonuç — Aşama 1 bu haliyle yazılırsa hatalı davranır:**

| Adım | Olan | Olması gereken |
| --- | --- | --- |
| Kullanıcı 2 sekme açar | 2. sekme 1.'nin connectionId'sini ezer | İki bağlantı da tutulmalı |
| **1 sekmeyi kapatır** | `SetUserOffline` → izleyenlere **`UserOffline`** gider | Hâlâ 1 sekme açık → **hiçbir event gitmemeli** |
| Diğer sekme hâlâ açık | Arkadaşlar kullanıcıyı **offline** görür (yanlış) | Online görmeli |

Yani çoklu sekme, mevcut kodda bile arkadaşları yanıltır; watcher push'u eklenince
bu hata **her arkadaşa canlı yayınlanır.**

**Düzeltme (Aşama 1'in ilk işi, watcher'dan önce):**

```csharp
// userId → connectionId KÜMESİ
ConcurrentDictionary<string, HashSet<string>> _userConnections;

Task<bool> SetUserOnline(string userId, string connectionId);
//   → true döner YALNIZCA bu kullanıcının İLK bağlantısıysa  (→ UserOnline yayınla)
Task<bool> SetUserOffline(string userId, string connectionId);
//   → true döner YALNIZCA SON bağlantısı da kapandıysa       (→ UserOffline yayınla)
```

`PresenceHub.OnConnectedAsync`/`OnDisconnectedAsync` yalnızca bu bayrak `true` ise
event yayınlar. **Referans sayımı (reference counting)** — `UserOnline`/`UserOffline`
kullanıcı başına en fazla bir kez gider.

> ⚠️ `SetUserOffline` imzası **değişiyor** (`connectionId` parametresi eklendi).
> Tek çağrı yeri `PresenceHub.OnDisconnectedAsync`; kırılan başka yer yok
> (doğrulandı — repo genelinde başka kullanım yok).

Bu düzeltme yapılmadan Aşama 1'e başlanmamalı.

---

## 1. Aşama 1 — Presence'a kişi-bazlı (arkadaşlık) aboneliği

**Sorun:** Şu an bir kullanıcının arkadaşının online/offline olduğunu öğrenmesinin tek
yolu `GetOnlineUsers(userIds)` ile **elle sorgulamak**. Push yok — arkadaş online olunca
kimse haber almıyor. Frontend bu yüzden ya polling yapıyor ya da hiç göstermiyor.

**Mevcut:** `SubscribeToClans` klan grubuna ekliyor, `UserOnline`/`UserOffline` sadece
**klan gruplarına** yayınlanıyor. Arkadaşın klanın yoksa haberin olmuyor.

### Yapılacak

**1.1 — `PresenceRepository`'ye ters indeks (watcher) ekle**

```
// userId → onu izleyen connectionId'ler
_userWatchers : ConcurrentDictionary<string, HashSet<string>>
// connectionId → izlediği userId'ler (disconnect temizliği için)
_connectionWatchedUsers : ConcurrentDictionary<string, List<string>>
```

Yeni metotlar:
- `SetConnectionWatchedUsers(connectionId, userIds)`
- `GetConnectionWatchedUsers(connectionId)`
- `RemoveConnectionWatchedUsers(connectionId)` → ters indeksten de düşür
- `GetWatchersOfUser(userId) → List<string>` (connectionId listesi)

> Neden ters indeks: online olan kullanıcıyı **kimin izlediğini** O(1) bulmak için.
> Her broadcast'te tüm bağlantıları taramak istemiyoruz.

**1.2 — `PresenceHub.SubscribeToUsers(List<string> userIds)`**

`SubscribeToClans`'ın yanına, aynı desende:

```csharp
public async Task SubscribeToUsers(List<string> userIds)
```

Davranış:
- Her `userId` için connection'ı `user_{userId}` grubuna ekle
- Repository'ye watcher kaydını yaz
- **Anlık snapshot dön:** o an online olanları `Clients.Caller` → `OnlineUsers` ile
  gönder (frontend ayrıca `GetOnlineUsers` çağırmak zorunda kalmasın)

**1.3 — `OnConnectedAsync` / `OnDisconnectedAsync`'i güncelle**

- **Connect:** kullanıcı online olunca, onu izleyen herkese
  `Clients.Group($"user_{userId}")` → `UserOnline` (mevcut klan yayınına **ek olarak**)
- **Disconnect:** aynı şekilde `UserOffline` + `RemoveConnectionWatchedUsers`

**1.4 — ⚠️ Yetkilendirme (atlanmamalı)**

`SubscribeToUsers` filtresiz bırakılırsa **herkes herkesin online durumunu izleyebilir**
(mahremiyet sızıntısı; kullanıcı listesi elde eden biri tüm platformun online haritasını
çıkarır). Bu, madde 1.4'teki DM yetkilendirmesiyle aynı sınıf hata.

Çözüm: `IdentityService`'e iç endpoint:
```
GET /api/Friendship/friend-ids   → ["userId", ...]   (Bearer korumalı)
```
PresenceService `SubscribeToUsers` içinde bu listeyi çeker ve **istenen userId'leri
kesişime indirir** — arkadaş olmayanlar sessizce düşürülür.

Performans notu: her `SubscribeToUsers` çağrısında HTTP gitmesin diye sonuç
connection ömrü boyunca (veya 60sn TTL ile) cache'lenir.

**Dosyalar:** `PresenceService/{Hubs/PresenceHub,Repositories/PresenceRepository,Interfaces/IPresenceRepository}.cs`,
`IdentityService/.../{Controllers/FriendshipController,Services/FriendshipService,Interfaces/IFriendshipService}.cs`

---

## 2. Aşama 2 — NotificationService (yeni mikroservis)

Kalıcı bildirimler: offline'da kaybolmaz, okundu/okunmadı durumu saklanır.

### 2.1 — İskelet

`PresenceService`'in yapısı birebir şablon alınır (en yakın örnek):

```
NotificationService/
├── Program.cs
├── Dockerfile
├── Models/Notification.cs
├── Data/NotificationDbContext.cs        (Postgres + EF)
├── DTOs/NotificationDto.cs
├── Controllers/NotificationController.cs
├── Hubs/NotificationHub.cs              (canlı push)
├── Services/NotificationService.cs
├── Interfaces/INotificationService.cs
├── RabbitMQ/{MassTransitManager,NotificationConsumer}.cs
└── Extensions/{AuthenticationExtensions,CorsExtensions}.cs
```

### 2.2 — Veri modeli

```csharp
public enum NotificationType
{
    FriendRequestReceived, FriendRequestAccepted,
    DirectMessageReceived, ClanInvite,
    MissedCall,                 // madde 3 ile bağlanır
}

public class Notification
{
    public Guid Id { get; set; }
    public string UserId { get; set; }        // alıcı (indeksli)
    public NotificationType Type { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public string ActorUserId { get; set; }   // eylemi yapan
    public string TargetId { get; set; }      // conversationId / clanId / requestId
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
```
İndeks: `(UserId, IsRead, CreatedAt DESC)` — okunmamış sorgusu ve listeleme bunun üstünde.

### 2.3 — REST API

| Metot | Yol | Açıklama |
| --- | --- | --- |
| GET | `/api/Notification?unreadOnly=&limit=&page=` | Listele (sayfalı) |
| GET | `/api/Notification/unread-count` | Badge sayacı |
| POST | `/api/Notification/{id}/read` | Tek tek okundu |
| POST | `/api/Notification/read-all` | Hepsini okundu işaretle |
| DELETE | `/api/Notification/{id}` | Sil |

Hepsi `[Authorize]` + **sadece kendi bildirimine erişim** (`UserId == JWT sub`;
başkasının id'siyle `read`/`delete` denemesi `404`).

### 2.4 — Canlı push: `NotificationHub`

```
/hubs/notification
  → ReceiveNotification(NotificationDto)   // yeni bildirim
  → UnreadCountChanged(int)                // badge güncelle
```
SignalR'ın `Context.UserIdentifier` ile **kullanıcıya** gönderim (`Clients.User(userId)`)
— grup yönetimi gerekmez.

### 2.5 — Üretim yolu: RabbitMQ (senkron HTTP değil)

Bildirim üretenler event yayınlar, NotificationService tüketir. Böylece
NotificationService düşse **ana akış (arkadaşlık, mesaj) bozulmaz**.

`Shared.Contracts`'a eklenecek:
```csharp
public record NotificationRequestedMessage
{
    public string? UserId { get; init; }        // alıcı
    public string? Type { get; init; }
    public string? Title { get; init; }
    public string? Body { get; init; }
    public string? ActorUserId { get; init; }
    public string? TargetId { get; init; }
}
```

Yayınlayanlar:
- `IdentityService.FriendshipService.SendRequestAsync` → `FriendRequestReceived`
- `IdentityService.FriendshipService.AcceptRequestAsync` → `FriendRequestAccepted`
- `MessageService.MessageHub.SendMessage` → `DirectMessageReceived` (`clanId == null` ise; bkz. 2.5.1)
- `PresenceService` (madde 3) → `MissedCall`

### 2.5.1 — ⚠️ "Alıcı aktif odada değilse" koşulu: mevcut sözleşmelerle ölçülemiyor

Planın ilk hâli "alıcı o an DM odasında değilse bildirim üret" diyordu. **Bunu
belirleyecek bir durum/arayüz yok.** Ölçüm:

| Aday kaynak | Neden işe yaramıyor |
| --- | --- |
| `MessageHub.JoinChannel` | Yalnızca `Groups.AddToGroupAsync` çağırıyor. SignalR grup üyeliği **okunamaz** — "bu kullanıcı bu grupta mı?" diye sorulamaz, API'si yok. MessageService'te bunu yansıtan hiçbir state tutulmuyor (doğrulandı: serviste tek bir `ConcurrentDictionary` bile yok). |
| `PresenceRepository._connectionConversations` | "Hangi konuşmalara **abone**" bilgisi — kullanıcı DM listesindeki **tümüne** abone olur. "Şu an hangi sohbet **açık**" demek değil. Bunu kullanmak **her DM mesajında bildirimi bastırır** — yani bildirim hiç çalışmaz. |
| `IsUserOnline` | Online ≠ o sohbete bakıyor. Başka sekmede/klanda olabilir. |

**Karar: kapsamı küçült.** İlk sürümde koşul yalnızca **online/offline** olsun:

- Alıcı **offline** → bildirim üret (kaçırdığı mesajı görsün)
- Alıcı **online** → bildirim üretme; zaten `ReceiveMessage` canlı düşüyor

Bu, "başka sekmede online ama DM'e bakmıyorsa bildirim gelmez" durumunu kabul eder —
kusurlu ama **yanlış yönde kusurlu değil** (spam üretmez, sessiz kalır).

**Doğru çözüm (ayrı iş, backlog 4.4):** "aktif sohbet" kavramı açıkça modellenmeli —
`MessageHub`'a `SetActiveChannel(channelId)` / `ClearActiveChannel()` eklenip
`connectionId → activeChannelId` state'i tutulur; ön yüz sohbeti açıp kapatınca çağırır.
Okunmadı sayacı da (madde 4.2/4.3) aynı state'e dayanacağı için bu iş onlarla
birlikte yapılmalı. **Bu plandaki 3 aşamanın hiçbiri buna bağlı değil.**

### 2.6 — Gateway + Docker

`ocelot.json` **ve** `ocelot.docker.json` (ikisi birlikte, biri atlanırsa
prod'da 404 olur):
```json
{
  "DownstreamPathTemplate": "/api/Notification/{everything}",
  "UpstreamPathTemplate": "/notification/{everything}",
  "AuthenticationOptions": { "AuthenticationProviderKey": "Bearer" }
}
```
> Not: Yeni route eklendiğinde ApiGateway'deki route-matcher middleware'i (madde 0'da
> eklenen 404 düzeltmesi) config'den otomatik okuduğu için **ek iş gerekmez.**

`docker-compose.yml`: yeni `notificationservice` + `notification-db` (Postgres),
`nginx/default.conf.template`'e `/hubs/notification` için WebSocket upstream'i.

---

## 3. Aşama 3 — DM sesli arama (zil + kabul/reddet)

Mevcut durumda DM ses odası **var** ama "çağrı" kavramı yok: bir taraf odaya girer,
diğeri fark etmezse hiçbir şey olmaz. İstenen: gerçek çağrı akışı.

### 3.1 — Durum makinesi

```
        CallUser
   A ──────────────► Ringing ──── AcceptCall ───► Accepted ── EndCall ──► Ended
                        │                            │                     ▲
                        ├──────── RejectCall ───► Rejected                 │
                        ├──────── CancelCall ───► Cancelled                │
                        ├──────── (30 sn) ─────► Missed                    │
                        ├──────── B meşgul ────► Busy                      │
                        └───── taraf disconnect ───────────────────────────┘
```

Aynı anda **tek aktif çağrı**: A zaten bir çağrıdaysa yeni gelen çağrı `Busy` alır.

### 3.2 — `PresenceHub`'a eklenecek metotlar

`ICallRepository` arkasında in-memory state (Redis'e geçiş için soyutlanmış):

| Metot | Kim çağırır | Etki |
| --- | --- | --- |
| `CallUser(conversationId)` | Arayan | Karşı tarafa `IncomingCall`; arayana `CallRinging` |
| `AcceptCall(callId)` | Aranan | İki tarafa `CallAccepted` (+ `roomId = dm-{conversationId}`) |
| `RejectCall(callId)` | Aranan | Arayana `CallRejected` |
| `CancelCall(callId)` | Arayan | Aranana `CallCancelled` (zil kesilir) |
| **`EndCall(callId)`** | **Her iki taraf** | **İki tarafa `CallEnded`; kilit serbest kalır** |

Client event'leri: `IncomingCall`, `CallRinging`, `CallAccepted`, `CallRejected`,
`CallCancelled`, `CallTimedOut`, `CallBusy`, **`CallEnded`**.

**Zaman aşımı:** 30 sn sonra çağrı `Missed`'e düşer → `MissedCall` bildirimi
(madde 2.5) → arayana `CallTimedOut`. In-memory timer değil, **kayıt zamanı + periyodik
tarama** (timer'lar instance restart'ında kaybolur, tarama idempotenttir).

### 3.2.1 — ⚠️ `EndCall` olmadan "tek aktif çağrı" kilidi kalıcı olur

Planın ilk hâlinde `Accepted` durumundan **çıkış yolu yoktu.** Sonuç: görüşme bitse
bile kayıt `Accepted` kalır, "tek aktif çağrı" kuralı yüzünden **o kullanıcı bir daha
hiç arama yapamaz/alamaz** — servis yeniden başlatılana kadar (state in-memory).
Kalıcı kilitlenme, en ciddi bulgu.

Kapatılması gereken **üç** çıkış yolu — üçü de şart:

1. **`EndCall(callId)`** — normal kapatma, her iki taraf çağırabilir.
2. **Disconnect** — `OnDisconnectedAsync` içinde: kullanıcının `Ringing` **veya**
   `Accepted` çağrısı varsa sonlandır ve karşı tarafa `CallEnded` gönder.
   Ağ kopmasında `EndCall` hiç gelmez; bu yol olmazsa kilit yine kalır.
3. **Kendi kendini süpüren tarama** — `Accepted` çağrılara da **azami süre** (ör. 12 saat)
   uygula; periyodik tarama aşanları temizler. 1 ve 2 atlanırsa son emniyet kemeri.

> ⚠️ **Ön koşul 0.1 ile bağlantı:** Disconnect temizliği kullanıcının *son* bağlantısı
> kapandığında yapılmalı. Çoklu sekmede bir sekmenin kapanması görüşmeyi
> düşürmemeli — referans sayımı (0.1) burada da gerekiyor.

**Test edilmeli:** A–B görüşür → `EndCall` → **ikisi de yeniden arayabilmeli.**
Aynısı ağ koparak bittiğinde de geçerli.

### 3.3 — ⚠️ Yetkilendirme + hedef kimliği (mevcut endpoint YETMİYOR)

`CallUser(conversationId)` çağıranın o DM'in **katılımcısı olduğu doğrulanmalı** —
yoksa herhangi biri conversationId tahmin ederek istediği kişiyi arayabilir (taciz
vektörü).

Mevcut endpoint bu iş için **tek başına yetersiz:**
```csharp
// MessageService/Controllers/DmController.cs — bugünkü hâli
if (!isParticipant) return Forbid();
return Ok();          // ← GÖVDE YOK
```
Yetkiyi doğruluyor ama **karşı tarafın userId'sini dönmüyor.** PresenceService zili
kime çalacağını bilemez — `Clients.User(?)` için hedef gerekiyor. Konuşma kaydı
MessageService'in Mongo'sunda; PresenceService'in oraya erişimi yok.

**Düzeltme — endpoint gövdeli hâle getirilecek (geriye dönük uyumlu):**

```
GET /api/Dm/conversations/{conversationId}/participant-info    (Bearer)
→ 200 { "conversationId": "...", "otherUserId": "..." }
→ 403 (katılımcı değil)
```

Tek çağrıda **hem yetki hem hedef** döner. `IDmConversationService`'e karşılık gelen
metot eklenir (bugün böyle bir metot yok — sözleşme doğrulandı):
```csharp
Task<string?> GetOtherParticipantIdAsync(string conversationId, string userId);
// null → istekte bulunan katılımcı değil
```

> **Mevcut `is-participant` silinmeyecek** — VoiceService onu kullanıyor (madde 3.1) ve
> orada hedef kimliğe ihtiyaç yok. İki endpoint yan yana yaşar; yenisi PresenceService
> için. Böylece çalışan ses akışına dokunmadan ilerlenir.

Ayrıca: aranan kişi arayanı **engellemişse** (`FriendshipStatus.Blocked`) çağrı
kurulmaz. Madde 1.4'teki arkadaş listesi çağrısı bu kontrolü de kapsayacak şekilde
`blocked` bilgisini dönmeli.

Ayrıca: aranan kişi arayanı **engellemişse** (`FriendshipStatus.Blocked`) çağrı
kurulmaz. Madde 1.4'teki arkadaş listesi çağrısı bu kontrolü de kapsayacak şekilde
`blocked` bilgisini dönmeli.

### 3.4 — Ses token'ı

Değişiklik **gerekmiyor**. `AcceptCall` sonrası iki taraf da mevcut
`GET /voice/join-room/dm-{conversationId}` ile token alır; DM katılımcılığı
kontrolü orada zaten yapılıyor.

---

## 4. Backlog (bu planın dışında, ama yazılı kalsın)

| # | İş | Neden |
| --- | --- | --- |
| 4.1 | PresenceService'e **Redis backplane** | Tek instance kısıtını kaldırır; presence + çağrı state'i instance'lar arası paylaşılır. Ölçeklenmeden **önce** şart. |
| 4.2 | Bildirim tercihleri (kullanıcı bazlı aç/kapa) | "DM bildirimi istemiyorum" gibi ayarlar |
| 4.3 | Bildirim gruplama ("3 yeni mesaj") | Çok mesajda badge spam'ini önler |
| 4.4 | **"Aktif sohbet" modeli** (`SetActiveChannel`/`ClearActiveChannel`) + okunmadı sayacı | Madde 2.5.1'in doğru çözümü. Bugün "alıcı odaya bakıyor mu?" ölçülemiyor; 2.5.1 bu yüzden online/offline'a indirgendi. Okunmadı sayacı (4.2/4.3) da aynı state'e dayanacağı için **birlikte** yapılmalı. |

---

## Uygulama sırası ve bağımlılıklar

```
Ön koşul 0.1 (çoklu bağlantı / referans sayımı)
       │  ← Aşama 1 ve 3 bunun üstüne oturuyor
       ├──► Aşama 1 (Presence/arkadaşlık)  ─┐
       │                                    ├─► Aşama 3 (arama)
       └──► Aşama 3 (disconnect temizliği)  │   + 3.3 endpoint düzeltmesi
                                            │
            Aşama 2 (NotificationService)  ─┘   (MissedCall'ın gideceği yer)
```

**Önerilen sıra: 0.1 → 1 → 2 → 3.**

- **0.1 önce** — hem Aşama 1'in `UserOnline`/`UserOffline` push'u hem Aşama 3'ün
  disconnect temizliği doğru olması için referans sayımına bağlı. Atlanırsa iki
  aşama da hatalı davranır.
- **3 en son** — 2'nin `MissedCall` bildirimini üretiyor ve 3.3'teki
  `participant-info` endpoint'ine ihtiyacı var.

Her aşama bağımsız test edilebilir ve ayrı ayrı canlıya çıkabilir (0.1 hariç —
o tek başına davranış değiştirmez, mevcut hatayı düzeltir).

## Doğrulama ölçütleri

| Aşama | Nasıl doğrulanır |
| --- | --- |
| **0.1** | **Aynı kullanıcıyla 2 sekme aç → 1'ini kapat → izleyen arkadaşa `UserOffline` gitmemeli.** Son sekme de kapanınca **tam bir kez** gitmeli. |
| 1 | İki oturum aç; A `SubscribeToUsers([B])` çağırsın, B bağlanınca A `UserOnline` almalı. Arkadaş **olmayan** C için `SubscribeToUsers([C])` sessizce düşmeli. |
| 2 | Arkadaşlık isteği gönder → alıcı offline olsa bile `GET /notification` listesinde görünmeli; `unread-count` artmalı; başkasının bildirimini `read` denemesi 404. DM'de: alıcı **offline** iken mesaj → bildirim var; **online** iken → bildirim yok, sadece `ReceiveMessage`. |
| 3 | A arar → B'de `IncomingCall`; B kabul → iki taraf `dm-{id}` odasında. B cevaplamaz → 30sn sonra A `CallTimedOut`, B'de `MissedCall`. Katılımcı olmayan D'nin `CallUser` denemesi reddedilmeli. |
| **3 (kilit)** | **`EndCall` sonrası iki taraf da yeniden arayabilmeli.** Aynısı ağ koparak bittiğinde de geçerli — kilit kalmamalı. |
