# Backend Güncelleme Planı

Bu dosya [guncelleme-plani.md](guncelleme-plani.md)'deki frontend planının backend
karşılığıdır. Kod tabanı taraması ile hazırlandı (2026-07-22), **uygulama 2026-07-24
tarihinde tamamlandı**. Backend tam bir microservice mimarisi olarak bu repoda mevcut:

- **IdentityService** (ASP.NET Identity, JWT, refresh token, email confirmation — Postgres)
- **ClanService** (clan/kanal/rol/üyelik — Postgres, RabbitMQ ile Identity'den user senkronu)
- **MessageService** (mesajlaşma — MongoDB, SignalR `MessageHub`)
- **PresenceService** (online durum, voice presence — Redis muhtemelen, SignalR `PresenceHub`)
- **VoiceService**, **VersionControlService**, **ApiGateway** (Ocelot)
- **AuthenticationService** (Java/Spring — clan rol sorgulama servisi, aşağıya bakın)

Servisler arası kullanıcı senkronu RabbitMQ/MassTransit üzerinden `UserUpdatedMessage`
(`Shared.Contracts`) ile yapılıyor: Identity'de kullanıcı güncellenince ClanService ve
MessageService kendi lokal `User` tablolarını (Id, Username, AvatarUrl) güncelliyor.

## ⚠️ Önemli düzeltme (2026-07-24): önceki "✅ Uygulandı" işaretleri gerçek değildi

Bu dosyanın 2026-07-22 tarihli önceki hâli madde 0/1/3/8'i "✅ Uygulandı" olarak
işaretliyordu, ancak 2026-07-24'te yapılan doğrulamada bu değişikliklerin **hiçbiri
koda yansımamış** olduğu görüldü — muhtemelen önceki oturumda kod hiç commit
edilmeden kaybolmuştu. Tek istisna: yerel Postgres veritabanı önceki oturumun
`Bio` kolonu ve `Friendships` tablosu eklemelerini gerçekten kalıcı olarak
uygulamıştı (ama migration dosyaları/`__EFMigrationsHistory` kaydı yoktu) —
bu 2026-07-24'te modelle eşleştirilip migration history'sine kaydedildi.

**2026-07-24'te tüm madde 0/1/3/8 baştan uygulandı ve bu sefer commit edilmeye
hazır.** Aşağıdaki içerik güncel/gerçek durumu yansıtıyor.

## Mimari keşif: Ocelot zaten clan-channel yetkilendirmesi yapıyor

Uygulama sırasında keşfedildi: `ApiGateway/Program.cs`, path'inde `clanId/{guid}`
geçen her isteğe önce bakıyor, kullanıcının JWT'sinden `userId`'yi çıkarıp ayrı bir
**Java (`AuthenticationService`)** servisine `GET /roles?userId=&clanId=` sorusu
soruyor ve dönen rolü `X-Clan-Role` header'ı olarak alt servise forward ediyor.
`MessageService`deki `GatewayAuthenticationHandler` bu header'ı okuyup
`ClaimTypes.Role` claim'ine çeviriyor; `MessageController`'daki
`[Authorize(Roles = "OWNER,ADMIN,MEMBER")]` buna dayanıyor.

Bu, **clan-channel mesajları için yetkilendirmenin zaten var olduğu** anlamına
geliyor — madde 0'ın "hiçbir yetki kontrolü yok" tespiti eksikti (path'te
`clanId` olmayan DM route'ları için bu mekanizma hiç çalışmıyor, orada gerçekten
boşluk vardı). Bu nedenle DM yetkilendirmesi ClanService'e HTTP çağrısı yapan
ağır bir `ChannelAuthorizationService` yerine, MessageService içinde
`DmConversation`'a bakan hafif bir katmanla çözüldü (aşağıya bakın).

**Bilinen sınırlama (kapsam dışı bırakıldı):** `MessageHub.JoinChannel`
(SignalR, path-regex'in hiç göremediği bir WebSocket çağrısı) clan-channel'lar
için hâlâ gerçek bir üyelik kontrolü yapmıyor — sadece DM conversation'lar için
katılımcı kontrolü var. REST tarafı (`MessageController`) gateway + `X-Clan-Role`
ile korunuyor olduğundan risk sınırlı, ama SignalR üzerinden `JoinChannel` çağrısı
teorik olarak üyesi olmadığı bir clan-channel grubuna katılabilir (mesaj
gönderemez, çünkü `SendMessage` de aynı sınırlamaya tabi — sadece dinleyebilir).
İleride ele alınmalı.

---

## 0. Kullanıcı kimliği doğrulanmıyor — ✅ Uygulandı (2026-07-24)

- `UserController` (`UpdateUser`, `DeleteUser`, `ChangePassword`, `GetMe`, `SearchUsers`)
  artık `[Authorize]` altında; `UpdateUser`/`ChangePassword`/`GetMe`/`SearchUsers`
  `userId`'yi `User.FindFirstValue(ClaimTypes.NameIdentifier)` ile JWT'den okuyor.
  `UpdateUserModel.Id` kaldırıldı. `DeleteUser` bilinçli olarak `[Authorize(Roles = "Muhammet")]`
  altında bırakıldı (kendi hesabını silme değil, admin'in başka bir hesabı silmesi
  akışı gibi görünüyor — rol zaten client'ın keyfi id girmesini anlamsız kılıyor).
- `AuthController.GetMySessions` düzeltildi — `User.FindFirstValue(ClaimTypes.NameIdentifier)`
  kullanıyor (önceden `ClaimTypes.NameIdentifier` sabit string olarak geçiyordu — bug).
  `[Authorize]` eklendi. `LogoutSession` de `[Authorize]` altına alındı;
  `AuthService.LogoutSessionAsync(sessionId, userId)` artık session'ın gerçekten o
  kullanıcıya ait olduğunu doğruluyor, aksi halde `NotFound` dönüyor.
- `MessageController` (`DeleteMessageAsync`, `UpdateMessage`) ve `MessageHub`
  (`SendMessage`, `UpdateMessage`, `DeleteMessage`) artık `senderId`/`userName`'i
  client'tan parametre olarak almıyor; `Context.User` (`ClaimTypes.NameIdentifier`)
  kullanılıyor. `MessageService.DeleteMessageAsync`/`UpdateMessage` artık
  `requesterId` alıp mesaj sahibi olmayanları `403 Forbidden` ile reddediyor.
- **Kontrat değişikliği (frontend'e yansıtılmalı, ayrı repo):**
  `MessageHub.SendMessage` imzası `(channelId, clanId, senderId, userName, message)` →
  `(channelId, clanId, message)` olarak değişti.

**Bilinçli olarak ertelenen kısım:** yukarıdaki "Mimari keşif" notuna bakın —
clan-channel REST tarafı gateway ile korunuyor, SignalR `JoinChannel` clan-channel
üyelik kontrolü hâlâ yok.

---

## 1. Şifre değiştirme + e-posta doğrulama + Profil/Avatar — ✅ Uygulandı (2026-07-24)

- `ApplicationUser.Bio` (`[MaxLength(190)]`, nullable) eklendi.
- `IUserService`/`UserService.ChangePasswordAsync(userId, currentPassword, newPassword)`
  eklendi (`UserManager.ChangePasswordAsync` sarmalıyor, `PasswordValidationAttribute`
  ile aynı kurallara tabi). `[Authorize] POST /api/User/change-password` eklendi.
- `[Authorize] GET /api/User/me` eklendi — `userName`, `email`, `bio`, `avatarUrl`,
  `emailConfirmed` dönüyor (`UserMeDto`).
- `UpdateUserModel.Bio` eklendi; `UpdateUserModel.Id` ve kullanılmayan `FullName`
  kaldırıldı (DB'de zaten `FullName` kolonu yoktu — `UpdatedModels` migration'ı
  bunu daha önce düşürmüştü, DTO'da unutulmuş kalıntıydı).
- `UserUpdatedMessage`'a `Bio` eklenmedi (kasıtlı) — ClanService/MessageService'in
  lokal `User` tabloları sadece `Username`/`AvatarUrl` tutuyor.
- **Migration:** `20260723212214_AddUserBio` — hem `Bio` kolonunu hem `Friendships`
  tablosunu tek migration'da ekliyor (bkz. madde 3). Yerel Postgres'e uygulandı.

**Kalan (frontend/route netleştirme, ayrı repo):**

- Gerçek route'lar `POST /api/User/change-password`, `GET /api/User/me`,
  `POST /api/Auth/resend-confirmation-email`, `GET /api/Auth/confirm-email`.
- `resend-confirmation-email` hâlâ `userId`'yi body'den alıyor, JWT'den değil —
  bilinçli olarak böyle bırakıldı (login olmadan, örn. kayıt sonrası çağrılabilir
  olması gerekiyor).

---

## 2. Profil sayfası + profil fotoğrafı — henüz yapılmadı

Ayrı bir "başka kullanıcının profiline bakma" backend ihtiyacı henüz karşılanmadı:
`GET /api/User/{id}/profile` gibi bir endpoint yok. Madde 8 (kullanıcı arama) ile
birlikte ele alınabilir — bir kullanıcının public profilini (username, avatar,
bio, ortak clanlar) dönecek bir endpoint gerekebilir. Bio gizlilik ayarı yoksa
(şu an yok) bio public sayılabilir; ileride bir gizlilik alanı gerekirse
`ApplicationUser`'a eklenir (`IsSearchable` gibi — henüz eklenmedi).

---

## 3. Arkadaşlık (friendship) sistemi + DM — ✅ Uygulandı (2026-07-24)

**IdentityService — Friendship:**

- [Models/Friendship.cs](IdentityService/IdentityService/Models/Friendship.cs) —
  `{ Id, RequesterId, AddresseeId, Status (Pending|Accepted|Blocked), CreatedAt, RespondedAt }`.
  `(RequesterId, AddresseeId)` üzerinde unique index, kendine istek göndermeyi
  engelleyen check constraint (`CK_Friendship_NotSelf`).
- [Controllers/FriendshipController.cs](IdentityService/IdentityService/Controllers/FriendshipController.cs) —
  `GET /api/Friendship`, `GET /api/Friendship/requests`,
  `POST /api/Friendship/requests`, `POST /api/Friendship/requests/{id}/accept`,
  `POST /api/Friendship/requests/{id}/reject`, `DELETE /api/Friendship/{friendUserId}`.
  Hepsi `[Authorize]`, userId JWT claim'inden okunuyor.
- [Services/FriendshipService.cs](IdentityService/IdentityService/Services/FriendshipService.cs) —
  iş kuralları: kendine istek yasak, zaten arkadaşsa hata, **ters yönde zaten
  bekleyen istek varsa yeni istek göndermek yerine otomatik kabul ediyor**
  (Discord benzeri UX — frontend planında belirtilmemişti, eklenen bir davranış).
- Madde 8 (kullanıcı arama): `GET /api/User/search?q=...&page=...&limit=...` —
  `IUserService.SearchUsersAsync`, Postgres `ILIKE` ile case-insensitive,
  sonuçtan çağıran kullanıcı hariç tutuluyor, sadece `id`/`userName`/`avatarUrl`
  dönüyor.

**MessageService — DM:**

- [Models/DmConversation.cs](MessageService/Models/DmConversation.cs) —
  `{ Id (ObjectId), UserAId, UserBId, CreatedAt }`. `UserAId`/`UserBId` her
  zaman `string.CompareOrdinal` ile kanonik sırada tutuluyor
  (`DmConversation.Canonicalize`), Mongo'da `(UserAId, UserBId)` unique index
  (`dm_conversations` collection).
- [Controllers/DmController.cs](MessageService/Controllers/DmController.cs) —
  `POST /api/Dm/conversations { otherUserId }` (idempotent — varsa mevcut
  conversation'ı döner), `GET /api/Dm/conversations`.
- [Services/DmConversationService.cs](MessageService/Services/DmConversationService.cs) —
  `IsDmConversationId(channelId)` bir string'in Mongo `ObjectId` formatında olup
  olmadığına bakarak DM/clan-channel ayrımını yapıyor; `IsParticipantAsync`
  katılımcı kontrolü.
- **DM mesajlaşması:** `MessageController`'a yeni `GET /api/Message?channelId=...`
  (query-string, path'e gömülü değil) route'u eklendi — sadece
  `IsDmConversationId(channelId)` true dönen id'ler için çalışır ve
  `IsParticipantAsync` ile yetkilendirir (`403 Forbid` yetkisiz erişimde).
  Clan-channel mesaj geçmişi hâlâ eski path route'unu
  (`GET /api/Message/channelId/{channelId}/clanId/{clanId}`, gateway
  `X-Clan-Role` ile korunuyor) kullanıyor — iki route da aynı controller'da,
  aynı `IMessageService.GetMessagesInChannelAsync`'e düşüyor.
  `Message` modeline `ConversationType` eklenmedi — gerek kalmadı, `channelId`
  formatından (`ObjectId` mi `Guid` mi) otomatik ayırt ediliyor.

**Gerçek DM yetkilendirmesi:**

- `MessageHub.SendMessage`/`JoinChannel`, DM conversation id'leri için
  `IDmConversationService.IsParticipantAsync` ile katılımcı olmayan kullanıcıları
  reddediyor (SignalR `MessageSendFailed`/`JoinChannelFailed` event'i). Clan-channel
  id'leri için bu kontrol atlanıyor (gateway zaten koruyor, bkz. "Mimari keşif").
- `MessageHub.JoinChannel`/`LeaveChannel` parametre tipi `Guid channelId` idi —
  DM conversation id'lerinin (Mongo ObjectId, 24 hex karakter) hiç geçirilemeyeceği
  anlamına geliyordu. `string channelId`'ye çevrildi.
- ClanService'e senkron HTTP çağrısı **yapılmadı** (plan taslağında önerilmişti) —
  gateway zaten `X-Clan-Role` ile clan-channel yetkilendirmesini sağladığından
  gereksiz bir ikinci ağ çağrısı olurdu; DM tarafı zaten yerel Mongo sorgusu ile
  çözülüyor.

**Arkadaşlık ↔ Presence entegrasyonu:** Değişiklik gerekmedi, plan doğruydu —
`PresenceHub.GetOnlineUsers` zaten clan'dan bağımsız.

**RabbitMQ/senkron etkisi:** Plan doğruydu, `Friendship`/`DmConversation`
`UserUpdatedMessage` senkronuna dahil edilmedi.

---

## Ocelot gateway route'ları — ✅ Uygulandı (2026-07-24)

`ocelot.json` ve `ocelot.docker.json`'a eklenen route'lar:

- `/identity/user/{everything}` → `/api/User/{everything}` (Bearer auth)
- `/identity/friendship/{everything}` → `/api/Friendship/{everything}` (Bearer auth)
- `/message/dm/{everything}` → `/api/Dm/{everything}` (Bearer auth)
- `/message` (segmentsiz, query-string route'u için — `{everything}` placeholder'ı
  boş segmenti eşlemediğinden ayrı eklendi) → `/api/Message` (Bearer auth)

Mevcut `/identity/{everything}` ve `/message/{everything}` route'ları korundu,
Ocelot en spesifik path'i önceliklendiriyor.

---

## `IUserService`/`IFriendshipService` DI kaydı — ✅ Uygulandı (2026-07-24)

`ServiceCollectionExtensions.AddApplicationServices()`'e
`services.AddScoped<IUserService, UserService>()` ve
`services.AddScoped<IFriendshipService, FriendshipService>()` eklendi.
`UserService.cs`'teki "this file just for test purposes" yorumu kaldırıldı.
`MessageService/Program.cs`'e de `IDmConversationService` DI kaydı eklendi.

---

## 8. Kişisel arama (kullanıcı arama) özelliği — ✅ Uygulandı (bkz. madde 3)

`GET /api/User/search?q=...&page=1&limit=20` — `UserName` üzerinde `ILIKE`
(case-insensitive contains), sayfalama (limit max 50), sonuçta sadece
`id`/`userName`/`avatarUrl` dönüyor. Ayrı bir rate limit tanımlanmadı — mevcut
`/identity/user/{everything}` route'u `Limit: 5`/saniye ile sınırlı, yeterli
kabul edildi. Gizlilik alanı (`IsSearchable`) eklenmedi — opsiyonel, istenirse
ayrıca ele alınabilir.

---

## Uygulama sırası (2026-07-24 tamamlandı)

1. ✅ **Madde 0** — JWT tabanlı kimlik doğrulama düzeltmeleri.
2. ✅ **Madde 1** — Şifre değiştirme, `GET /api/User/me`, `Bio` alanı, migration.
3. ✅ **Madde 8** — Kullanıcı arama endpoint'i.
4. ✅ **Madde 3** — Friendship tablosu/endpoint'leri, DmConversation, DM mesaj
   akışı, DM yetkilendirmesi.
5. ✅ **Ocelot gateway route eksikleri** — düzeltildi.
6. ✅ **`IUserService`/`IFriendshipService`/`IDmConversationService` DI kaydı**.
7. ✅ **Test güncellemeleri** — `UserControllerTests`, `AuthControllerTests`,
   `AuthServiceTests` yeni API imzalarına (userId JWT'den, `LogoutSessionAsync`
   sahiplik kontrolü) göre güncellendi, yeni `GetMe`/`ChangePassword`/`SearchUsers`
   testleri eklendi. `IdentityServiceTests`: 82 test yeşil (74 → 82).
   `ClanServiceTest`: 56 test yeşil (değişmedi). `MessageService`'in hiç test
   projesi yok (önceden de yoktu).

**Henüz yapılmadı / kapsam dışı bırakıldı:**

- Madde 2 (başka kullanıcının public profili — `GET /api/User/{id}/profile`).
- `MessageHub.JoinChannel`'da clan-channel için gerçek üyelik kontrolü (SignalR
  path'i gateway'in `X-Clan-Role` regex'inden geçmiyor).
- `ApplicationUser.IsSearchable` / arama gizlilik ayarı.
- Arama endpoint'i için ayrı Ocelot rate limit'i.

---

## Yerel test altyapısı (Docker)

[docker-compose.local.yml](docker-compose.local.yml) — bağımlılık servislerini
(Postgres x2, MongoDB, RabbitMQ, Redis) izole şekilde ayağa kaldırıyor, uygulama
servisleri normal şekilde IDE'den/`dotnet run` ile çalıştırılıyor.

**2026-07-24 notu:** Yerel `local-identity-postgres`'te, önceki (kaybolan) oturumun
uyguladığı `Bio` kolonu ve `Friendships` tablosu bulundu — kod tarafında migration
dosyaları yoktu ama DB şeması gerçekten değişmişti (`__EFMigrationsHistory`'de kayıt
bile yoktu). Yeni `20260723212214_AddUserBio` migration'ı bu gerçek şemayla
(constraint adları, FK cascade yönleri dahil) birebir eşleştirilip oluşturuldu,
sonra DB'ye tekrar uygulanmadan sadece `__EFMigrationsHistory`'ye kaydedildi
(zaten mevcut olduğu için). **Ders:** Bu repoda migration/kod kaybı riski var
(muhtemelen önceki oturumlarda commit edilmeden kapatılmış çalışmalar) — DB
durumu ile kod durumu arasında fark bulunursa önce DB'nin gerçek şemasına bakılmalı.

---

## Frontend'e (ayrı repo) iletilmesi gereken düzeltmeler

1. **Route'lar artık gateway üzerinden gerçekten erişilebilir**: `/identity/user/me`,
   `/identity/user/update`, `/identity/user/change-password`, `/identity/user/search`,
   `/identity/friendship/*`, `/message/dm/*` hepsi Ocelot'ta tanımlı.
   `PUT /identity/user` diye tahmin edilen route **yanlış** — gerçek route
   `PUT /identity/user/update`.
2. **DM mesajlaşması için `/message/dm/channelId/{channelId}` gibi bir route yok.**
   Gerçek kontrat: `GET /message?channelId={dmConversationId}` (query string).
   Clan-channel mesajları için ayrı path route'u (`/message/channelId/{channelId}/clanId/{clanId}`)
   kullanılmaya devam ediyor — frontend hangi tür konuşma olduğuna göre iki
   farklı route çağırmalı.
3. **`MessageHub.SendMessage` imzası değişti:** `(channelId, clanId, message)` —
   `senderId`/`userName` artık gönderilmiyor (gönderilse de kullanılmıyor).
4. **`MessageHub.JoinChannel`/`LeaveChannel` artık `string channelId` alıyor**
   (önceden `Guid`).
5. **DM'de yetkisiz erişim reddediliyor:** `MessageController` DM route'unda 403,
   `MessageHub` ise `MessageSendFailed`/`JoinChannelFailed` SignalR event'i ile.
   Clan-channel'larda yetkilendirme zaten gateway seviyesinde (401/403 Ocelot'tan
   dönebilir).
6. **Arkadaşlık isteği kabul akışında bir davranış eklendi:** İki kullanıcı aynı
   anda birbirine istek gönderirse, backend ikinci isteği otomatik "kabul"e
   çeviriyor. `POST /identity/friendship/requests` bazen
   `"Friend request accepted"` mesajıyla dönebilir.
7. **Kritik: Frontend'in path segment sırası hatası (önceki tarama notu, hâlâ geçerli):**
   - `GET /clan/clanId/{id}` → olması gereken `GET /clan/{id}`
   - `POST /clanMembership/invitations/clanId/{id}` → olması gereken
     `POST /clanMembership/{id}/invitations`
   Backend'de bu route'lar hem gateway hem doğrudan servis üzerinden test edildi,
   ikisi de `200 OK` dönüyor — sorun backend'de değil.
