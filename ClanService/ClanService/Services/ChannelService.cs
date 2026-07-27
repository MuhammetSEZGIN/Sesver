using ClanService.Data;
using ClanService.Models;
using ClanService.Interfaces;
using Microsoft.EntityFrameworkCore;
using ClanService.RabbitMq;
using ClanService.DTOs;
using ClanService.Interfaces.Repositories;
using ClanService.Interfaces.Services;
using Shared.Contracts;

namespace ClanService.Services
{
    public class ChannelService : IChannelService
    {
        private readonly IClanRepository _clanRepository;
        private readonly IClanMessageProducer _clanMessageProducer;
        private readonly IChannelRepository _channelRepository;
        private readonly ILogger<ChannelService> _logger;

        public ChannelService(
            IClanRepository clanRepository,
            IChannelRepository channelRepository,
            IClanMessageProducer clanMessageProducer,
            ILogger<ChannelService> logger
           )
        {
            _logger = logger;
            _clanRepository = clanRepository;
            _channelRepository = channelRepository;
            _clanMessageProducer = clanMessageProducer;
        }

        public async Task<(Channel, string)> CreateChannelAsync(Channel channel)
        {
            try
            {
                var clan = await _clanRepository.GetByIdAsync(channel.ClanId);
                if (clan == null)
                    return (null, "Clan not found");

                channel.ChannelId = Guid.NewGuid();
                await _channelRepository.AddAsync(channel);
                await _clanMessageProducer.PublishChannelUpsertedMessageAsync(new ChannelUpsertedMessage
                {
                    ChannelId = channel.ChannelId.ToString(),
                    ClanId = channel.ClanId.ToString(),
                    Name = channel.Name,
                    ChannelType = ChannelType.TextChannel
                });
                _logger.LogInformation("Channel {ChannelId} created successfully for clan {ClanId}", channel.ChannelId, channel.ClanId);
                return (channel, "Channel created successfully");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error creating channel for clan {ClanId}", channel.ClanId);
                return (null, "Error while creating channel");
            }
        }

        public async Task<Channel> GetChannelByIdAsync(Guid channelId)
        {
            return await _channelRepository.GetByIdAsync(channelId);
        }

        public async Task<List<Channel>> GetChannelsByClanIdAsync(Guid clanId)
        {
             var result =await _channelRepository.GetChannelsByClanIdAsync(clanId);
            return result .ToList();
        }

        public async Task<Channel> UpdateChannelAsync(Channel channel)
        {
            try
            {
                await _channelRepository.UpdateAsync(channel);
                await _clanMessageProducer.PublishChannelUpsertedMessageAsync(new ChannelUpsertedMessage
                {
                    ChannelId = channel.ChannelId.ToString(),
                    ClanId = channel.ClanId.ToString(),
                    Name = channel.Name,
                    ChannelType = ChannelType.TextChannel
                });
                _logger.LogInformation("Channel {ChannelId} updated successfully", channel.ChannelId);
                return channel;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error updating channel {ChannelId}", channel.ChannelId);
                return null;
            }
        }

        public async Task<bool> DeleteChannelAsync(Guid channelId)
        {
            try
            {
                var existing = await _channelRepository.GetByIdAsync(channelId);
                if (existing == null) return false;
                
                await _channelRepository.DeleteAsync(existing);
                await _clanMessageProducer.PublishChannelDeletedMessageAsync(
                    channelId.ToString(), existing.ClanId.ToString(), ChannelType.TextChannel);
                _logger.LogInformation("Channel {ChannelId} deleted successfully", channelId);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error deleting channel {ChannelId}", channelId);
                return false;
            }
        }
    }
}
