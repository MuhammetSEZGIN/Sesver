# ClanService — Gerçek Backend Uç Listesi (Gateway Üzerinden)

Kaynak koddan (controller attribute'ları) tek tek çıkarıldı, 2026-07-24.
Gateway (`ApiGateway/ocelot.json` / `ocelot.docker.json`) tüm bu path'leri
`{everything}` catch-all ile birebir (segment eklemeden/çıkarmadan) ClanService'e
forward ediyor — yani buradaki path'ler frontend'in çağırması gereken gerçek uçlar.

Format: `METHOD gateway-path` — `Controller.Action` (kaynak dosya:satır) — rol

---

## /clan (ClanController)

1. `POST /clan` — `ClanController.CreateClan` (ClanController.cs:24) — `[Authorize]` (rol şartı yok, sadece login)
2. `GET /clan/clanId/{clanId}` — `ClanController.GetClanById` (ClanController.cs:42) — `OWNER,ADMIN,MEMBER`
3. `GET /clan` — `ClanController.GetAllClans` (ClanController.cs:54) — `MUHAMMET`
4. `PUT /clan/clanId/{clanId}` — `ClanController.UpdateClan` (ClanController.cs:63) — `OWNER,ADMIN`
5. `DELETE /clan/clanId/{clanId}` — `ClanController.DeleteClan` (ClanController.cs:86) — `OWNER`
6. `GET /clan/user` — `ClanController.GetMyClansAsync` (ClanController.cs:97) — `[Authorize]` (rol şartı yok)

## /clanMembership (ClanMembershipController)

7. `GET /clanMembership/{id}` — `ClanMembershipController.GetMembership` (ClanMembershipController.cs:31) — `MUHAMMET`
8. `GET /clanMembership/clanId/{clanId}` — `ClanMembershipController.GetMembershipsByClanId` (ClanMembershipController.cs:43) — `OWNER,ADMIN,MEMBER`
9. `GET /clanMembership/user/{userId}` — `ClanMembershipController.GetMembershipsByUserId` (ClanMembershipController.cs:52) — `OWNER,ADMIN,MEMBER`
10. `DELETE /clanMembership/member/{userId}/clanId/{clanId}` — `ClanMembershipController.RemoveMember` (ClanMembershipController.cs:61) — `OWNER,ADMIN`
11. `DELETE /clanMembership/user/clanId/{clanId}` — `ClanMembershipController.LeaveClan` (ClanMembershipController.cs:72) — `ADMIN,MEMBER`
12. `POST /clanMembership/invitations/clanId/{clanId}` — `ClanMembershipController.CreateInvitation` (ClanMembershipController.cs:83) — `OWNER,ADMIN`
13. `POST /clanMembership/join` — `ClanMembershipController.JoinClanWithInvite` (ClanMembershipController.cs:101) — `[Authorize]` (rol şartı yok)

## /channel (ChannelController)

14. `POST /channel/clanId/{clanId}` — `ChannelController.CreateChannel` (ChannelController.cs:24) — `OWNER,ADMIN`
15. `GET /channel/channel/{channelId}/clanId/{clanId}/` — `ChannelController.GetChannelById` (ChannelController.cs:45) — `OWNER,ADMIN,MEMBER`
16. `GET /channel/clanId/{clanId}` — `ChannelController.GetChannelsByClanId` (ChannelController.cs:60) — `OWNER,ADMIN,MEMBER`
17. `PUT /channel/clanId/{clanId}` — `ChannelController.UpdateChannel` (ChannelController.cs:75) — `OWNER,ADMIN`
18. `DELETE /channel/channel/{channelId}/clanId/{clanId}` — `ChannelController.DeleteChannel` (ChannelController.cs:98) — `OWNER,ADMIN`

## /voiceChannel (VoiceChannelController)

19. `POST /voiceChannel/clanId/{clanId}` — `VoiceChannelController.CreateVoiceChannel` (VoiceChannelController.cs:23) — `OWNER,ADMIN`
20. `GET /voiceChannel/{voiceChannelId}` — `VoiceChannelController.GetVoiceChannelById` (VoiceChannelController.cs:43) — `MUHAMMET`
21. `GET /voiceChannel/clanId/{clanId}` — `VoiceChannelController.GetVoiceChannelsByClanId` (VoiceChannelController.cs:55) — `OWNER,ADMIN,MEMBER`
22. `PUT /voiceChannel/clanId/{clanId}` — `VoiceChannelController.UpdateVoiceChannel` (VoiceChannelController.cs:64) — `OWNER,ADMIN`
23. `DELETE /voiceChannel/channel/{voiceChannelId}/clanId/{clanId}` — `VoiceChannelController.DeleteVoiceChannel` (VoiceChannelController.cs:87) — `OWNER,ADMIN`

## /role (RoleController)

24. `PUT /role/clanId/{clanId}` — `RoleController.UpdateRoleAsync` (RoleController.cs:20) — `OWNER,ADMIN`

---

## Not: çift "channel" segmenti taşıyan uçlar

Madde 15 ve 18'de path içinde `channel` kelimesi **iki kez** geçiyor:
`/channel/channel/{channelId}/clanId/{clanId}` — controller'ın kendi route prefix'i
(`api/[controller]` → `channel`) + action'ın kendisine ait ek `channel/` segmenti
üst üste biniyor. Kod satırı: `[HttpGet("channel/{channelId}/clanId/{clanId}/")]`
(ChannelController.cs:45) ve `[HttpDelete("channel/{channelId}/clanId/{clanId}")]`
(ChannelController.cs:98). Bu, diğer 22 uçtaki pattern'den (`{resource}/clanId/{clanId}`
veya `{resource}/action`) sapan tek istisna.
