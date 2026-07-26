using System;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Shared.Contracts;
using PresenceService.Hubs;
using PresenceService.Interfaces;

namespace PresenceService.RabbitMQ;

public class ClanServiceMessageConsumer :
    IConsumer<ChannelDeletedMessage>,
    IConsumer<ChannelUpsertedMessage>,
    IConsumer<ClanMembershipChangedMessage>,
    IConsumer<ClanDeletedMessage>
{
    ILogger<ClanServiceMessageConsumer> _logger;
    IPresenceRepository _presenceRepository;
    IHubContext<PresenceHub> _hubContext;

    public ClanServiceMessageConsumer(ILogger<ClanServiceMessageConsumer> logger,
     IPresenceRepository presenceRepository, IHubContext<PresenceHub> hubContext)
    {
        _logger = logger;
        _presenceRepository = presenceRepository;
        _hubContext = hubContext;
    }

    public async Task Consume(ConsumeContext<ChannelDeletedMessage> context)
    {
        var message = context.Message;

        _logger.LogInformation("Kanal silme mesajı alındı: {ChannelId} (Klan: {ClanId}, Tip: {Type})",
            message.ChannelId, message.ClanId, message.ChannelType);

        try
        {
            if (string.IsNullOrEmpty(message.ChannelId) || string.IsNullOrEmpty(message.ClanId))
            {
                _logger.LogWarning("Eksik veri içeren kanal silme mesajı alındı.");
                return;
            }

            if (message.ChannelType == ChannelType.VoiceChannel)
            {
                await _presenceRepository.DeleteVoiceChannel(message.ClanId, message.ChannelId);
                _logger.LogInformation("Ses kanalı varlığı ve katılımcıları temizlendi: {ChannelId}", message.ChannelId);

                // Bu mesajı alan frontend, eğer kullanıcı o kanaldaysa LiveKit bağlantısını koparacak.
                await _hubContext.Clients.Group($"clan_{message.ClanId}")
                    .SendAsync("OnVoiceChannelDeleted", message.ChannelId);
            }
            else
            {
                // Metin kanalı silinme bildirimi
                await _hubContext.Clients.Group($"clan_{message.ClanId}")
                    .SendAsync("OnChannelDeleted", message.ChannelId);
            }

            _logger.LogInformation("Kanal silinme bildirimi klan grubuna iletildi: {ClanId}", message.ClanId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kanal silme mesajı işlenirken hata oluştu: {ChannelId}", message.ChannelId);
            throw; // MassTransit'in hata yönetimi (retry) için throw ediyoruz.
        }
    }

    public async Task Consume(ConsumeContext<ChannelUpsertedMessage> context)
    {
        var message = context.Message;
        if (string.IsNullOrWhiteSpace(message.ChannelId)
            || string.IsNullOrWhiteSpace(message.ClanId)
            || string.IsNullOrWhiteSpace(message.Name))
        {
            _logger.LogWarning("Eksik veri içeren kanal upsert mesajı alındı.");
            return;
        }

        var group = _hubContext.Clients.Group($"clan_{message.ClanId}");
        if (message.ChannelType == ChannelType.VoiceChannel)
        {
            await group.SendAsync("OnVoiceChannelUpserted", new
            {
                voiceChannelId = message.ChannelId,
                clanId = message.ClanId,
                name = message.Name,
                isActive = message.IsActive,
                maxParticipants = message.MaxParticipants
            });
        }
        else
        {
            await group.SendAsync("OnChannelUpserted", new
            {
                channelId = message.ChannelId,
                clanId = message.ClanId,
                name = message.Name
            });
        }

        _logger.LogInformation(
            "Kanal upsert bildirimi klan grubuna iletildi: {ChannelId} ({ClanId})",
            message.ChannelId,
            message.ClanId);
    }

    public async Task Consume(ConsumeContext<ClanMembershipChangedMessage> context)
    {
        var message = context.Message;
        if (message.MembershipId == Guid.Empty
            || string.IsNullOrWhiteSpace(message.ClanId)
            || string.IsNullOrWhiteSpace(message.UserId))
        {
            _logger.LogWarning("Eksik veri içeren klan üyeliği değişiklik mesajı alındı.");
            return;
        }

        var connectionIds = await _presenceRepository.GetUserConnections(message.UserId);
        var groupName = $"clan_{message.ClanId}";

        // Yeni üyenin açık sekme/cihazları sonraki klan eventlerini anında
        // alabilsin. Frontend'in yeniden bağlanmasını beklemiyoruz.
        if (message.ChangeType == ClanMembershipChangeType.Joined)
        {
            foreach (var connectionId in connectionIds)
            {
                await _hubContext.Groups.AddToGroupAsync(connectionId, groupName);
                await _presenceRepository.AddConnectionClan(connectionId, message.ClanId);
            }
        }

        await _hubContext.Clients.Group(groupName).SendAsync("OnClanMembershipChanged", new
        {
            membershipId = message.MembershipId,
            clanId = message.ClanId,
            userId = message.UserId,
            userName = message.UserName,
            avatarUrl = message.AvatarUrl,
            role = message.Role,
            changeType = message.ChangeType.ToString()
        });

        // Çıkarılan kullanıcı bildirimi aldıktan sonra klanın SignalR grubundan
        // ve Presence abonelik kaydından düşürülür.
        if (message.ChangeType == ClanMembershipChangeType.Removed)
        {
            foreach (var connectionId in connectionIds)
            {
                await _hubContext.Groups.RemoveFromGroupAsync(connectionId, groupName);
                await _presenceRepository.RemoveConnectionClan(connectionId, message.ClanId);
            }
        }

        _logger.LogInformation(
            "Klan üyeliği değişikliği iletildi: {UserId}, {ClanId}, {ChangeType}",
            message.UserId,
            message.ClanId,
            message.ChangeType);
    }

    public async Task Consume(ConsumeContext<ClanDeletedMessage> context)
    {
        var message = context.Message;
        _logger.LogInformation("Klan silme mesajı alındı: {ClanId}", message.ClanId);
        try
        {
            if (string.IsNullOrEmpty(message.ClanId))
            {
                _logger.LogWarning("Eksik veri içeren klan silme mesajı alındı.");
                return;
            }

            // Klan varlığı ve katılımcıları temizle
            await _presenceRepository.DeleteClan(message.ClanId);
            _logger.LogInformation("Klan varlığı ve katılımcıları temizlendi: {ClanId}", message.ClanId);

            // Bu mesajı alan frontend, eğer kullanıcı o klandaysa LiveKit bağlantısını koparacak.
            await _hubContext.Clients.Group($"clan_{message.ClanId}")
                .SendAsync("OnClanDeleted", message.ClanId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Klan silme mesajı işlenirken hata oluştu: {ClanId}", message.ClanId);
            throw; // MassTransit'in hata yönetimi (retry) için throw ediyoruz.
        }
    }
}
