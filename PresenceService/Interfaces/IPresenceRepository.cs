using PresenceService.Models;

namespace PresenceService.Interfaces;

public interface IPresenceRepository
{
    // Online presence
    Task SetUserOnline(string userId, string connectionId);
    Task<string?> SetUserOffline(string userId);
    Task<bool> IsUserOnline(string userId);

    // Clan group subscriptions (so we can notify on disconnect)
    Task SetConnectionClans(string connectionId, List<string> clanIds);
    Task<List<string>> GetConnectionClans(string connectionId);
    Task RemoveConnectionClans(string connectionId);

    // DM conversation group subscriptions (so we can notify on disconnect)
    Task SetConnectionConversations(string connectionId, List<string> conversationIds);
    Task<List<string>> GetConnectionConversations(string connectionId);
    Task RemoveConnectionConversations(string connectionId);

    // Voice channel presence. clanId is null for DM voice rooms (voiceChannelId = "dm-{conversationId}", already globally unique).
    Task JoinVoiceChannel(string connectionId, string userId, string userName, string? clanId, string voiceChannelId);
    Task<(string? ClanId, string ChannelId, string UserId)?> LeaveVoiceChannel(string connectionId);
    Task DeleteVoiceChannel(string clanId, string channelId);
    Task DeleteClan(string clanId);

    Task<Dictionary<string, List<UserInfo>>> GetVoiceChannelParticipants(string clanId);
}
