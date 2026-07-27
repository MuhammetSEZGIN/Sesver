using System;
using Shared.Contracts;

namespace ClanService.Interfaces.Services;

public interface IClanMessageProducer
{
  Task PublishChannelDeletedMessageAsync(
    string channelId,
    string clanId,
    ChannelType channelType
  );
  Task PublishChannelUpsertedMessageAsync(ChannelUpsertedMessage message);
  Task PublishClanMembershipChangedMessageAsync(ClanMembershipChangedMessage message);
  Task PublishClanDeletedMessageAsync(string clanId);
  Task PublishClanRoleEventAsync(ClanRoleEventDto clanRoleEvent);
}
