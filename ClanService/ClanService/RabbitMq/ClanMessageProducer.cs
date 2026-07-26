using Shared.Contracts;
using ClanService.Interfaces.Services;
using MassTransit;
using System.Text.Json;
using System.Net.Mime;

namespace ClanService.RabbitMq;

public class ClanMessageProducer : IClanMessageProducer
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ClanMessageProducer> _logger;
    public ClanMessageProducer(IPublishEndpoint publishEndpoint, ILogger<ClanMessageProducer> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishChannelDeletedMessageAsync(
        string channelId,
        string clanId,
        ChannelType channelType
    )
    {
        var message = new ChannelDeletedMessage
        {
            ChannelId = channelId,
            ClanId = clanId,
            ChannelType = channelType
        };

        _logger.LogInformation(
            "Publishing VoiceChannelDeletedMessage for channel: {channelId}, clan: {clanId}",
            channelId,
            clanId
        );
        await _publishEndpoint.Publish(message);
    }

    public async Task PublishClanDeletedMessageAsync(string clanId)
    {
        var message = new ClanDeletedMessage
        {
            ClanId = clanId
        };

        _logger.LogInformation(
            "Publishing ClanDeletedMessage for clan: {clanId}",
            clanId
        );
        await _publishEndpoint.Publish(message);
    }

    public async Task PublishChannelUpsertedMessageAsync(ChannelUpsertedMessage message)
    {
        _logger.LogInformation(
            "Publishing ChannelUpsertedMessage for channel: {ChannelId}, clan: {ClanId}, type: {ChannelType}",
            message.ChannelId,
            message.ClanId,
            message.ChannelType
        );
        await _publishEndpoint.Publish(message);
    }

    public async Task PublishClanMembershipChangedMessageAsync(ClanMembershipChangedMessage message)
    {
        _logger.LogInformation(
            "Publishing ClanMembershipChangedMessage for user: {UserId}, clan: {ClanId}, change: {ChangeType}",
            message.UserId,
            message.ClanId,
            message.ChangeType
        );
        await _publishEndpoint.Publish(message);
    }

    public async Task PublishClanRoleEventAsync(ClanRoleEventDto clanRoleEvent)
    {
        _logger.LogInformation(
            "Publishing ClanRoleEvent for user: {userId}, clan: {clanId}, role: {role}, eventType: {eventType}",
            clanRoleEvent.UserId,
            clanRoleEvent.ClanId,
            clanRoleEvent.Role,
            clanRoleEvent.EventType
        );


        await _publishEndpoint.Publish(clanRoleEvent);
        
    }
}
