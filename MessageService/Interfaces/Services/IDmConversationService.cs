using System.Collections.Generic;
using MessageService.DTOs;

namespace MessageService.Interfaces.Services;

public interface IDmConversationService
{
    Task<DmConversationDto> GetOrCreateConversationAsync(string userId, string otherUserId);
    Task<List<DmConversationDto>> GetConversationsAsync(string userId);
    Task<bool> IsParticipantAsync(string conversationId, string userId);
    Task<DmCallContextDto?> GetCallContextAsync(string conversationId, string userId);
    bool IsDmConversationId(string channelId);
}
