using ClanService.Models;
using ClanService.Interfaces;
using ClanService.Interfaces.Repositories;
using ClanService.Interfaces.Services;
using Shared.Contracts;

namespace ClanService.Services
{
    public class VoiceChannelService : IVoiceChannelService
    {
        private readonly IClanRepository _clanRepository;
        private readonly IVoiceChannelRepository _voiceChannelRepository;
        private readonly IClanMessageProducer _clanMessageProducer;
        private readonly ILogger<VoiceChannelService> _logger;

        public VoiceChannelService(
            IClanRepository clanRepository, IVoiceChannelRepository voiceChannelRepository, 
            IClanMessageProducer clanMessageProducer, ILogger<VoiceChannelService> logger)
        {
            _clanRepository = clanRepository;
            _voiceChannelRepository = voiceChannelRepository;
            _clanMessageProducer = clanMessageProducer;
            _logger = logger;
        }

        public async Task<(VoiceChannel, string)> CreateVoiceChannelAsync(VoiceChannel voiceChannel)
        {
            var clan = await _clanRepository.GetByIdAsync(voiceChannel.ClanId);
            if (clan == null)
                return (null, "Clan not found");
            await _voiceChannelRepository.AddAsync(voiceChannel);
            await _clanMessageProducer.PublishChannelUpsertedMessageAsync(new ChannelUpsertedMessage
            {
                ChannelId = voiceChannel.VoiceChannelId.ToString(),
                ClanId = voiceChannel.ClanId.ToString(),
                Name = voiceChannel.Name,
                ChannelType = ChannelType.VoiceChannel,
                IsActive = voiceChannel.IsActive,
                MaxParticipants = voiceChannel.MaxParticipants
            });
            return (voiceChannel, "VoiceChannel created successfully");
        }

        public async Task<VoiceChannel> GetVoiceChannelByIdAsync(Guid voiceChannelId)
        {
            return await _voiceChannelRepository.GetByIdAsync(voiceChannelId);
        }

        public async Task<List<VoiceChannel>> GetVoiceChannelsByClanIdAsync(Guid clanId)
        {
            var channels = await _voiceChannelRepository.GetVoiceChannelsByClanIdAsync(clanId);
            return channels.ToList();
        }

        public async Task<VoiceChannel> UpdateVoiceChannelAsync(VoiceChannel voiceChannel)
        {
            await _voiceChannelRepository.UpdateAsync(voiceChannel);
            await _clanMessageProducer.PublishChannelUpsertedMessageAsync(new ChannelUpsertedMessage
            {
                ChannelId = voiceChannel.VoiceChannelId.ToString(),
                ClanId = voiceChannel.ClanId.ToString(),
                Name = voiceChannel.Name,
                ChannelType = ChannelType.VoiceChannel,
                IsActive = voiceChannel.IsActive,
                MaxParticipants = voiceChannel.MaxParticipants
            });
            return voiceChannel;
        }

        public async Task<bool> DeleteVoiceChannelAsync(Guid voiceChannelId)
        {
            var existing = await _voiceChannelRepository.GetByIdAsync(voiceChannelId);
            if (existing == null) return false;
                await _clanMessageProducer.PublishChannelDeletedMessageAsync(
                    existing.VoiceChannelId.ToString(), 
                    existing.ClanId.ToString(), 
                    ChannelType.VoiceChannel
                );
                _logger.LogInformation(
                    "Published ChannelDeletedMessage for voice channel: {channelId}, clan: {clanId}",
                    existing.VoiceChannelId,
                    existing.ClanId
                );
            await _voiceChannelRepository.DeleteAsync(existing);
            return true;
        }
    }
}
