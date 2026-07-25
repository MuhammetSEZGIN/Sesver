using MessageService.Data;
using MessageService.DTOs;
using MessageService.Interfaces.Services;
using MessageService.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MessageService.Services;

public class DmConversationService : IDmConversationService
{
    private readonly IMongoDbContext _context;
    private readonly ILogger<DmConversationService> _logger;

    public DmConversationService(IMongoDbContext context, ILogger<DmConversationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public bool IsDmConversationId(string channelId)
    {
        return ObjectId.TryParse(channelId, out _);
    }

    public async Task<DmConversationDto> GetOrCreateConversationAsync(
        string userId,
        string otherUserId
    )
    {
        if (userId == otherUserId)
        {
            throw new InvalidOperationException("Cannot start a conversation with yourself");
        }

        var (userAId, userBId) = DmConversation.Canonicalize(userId, otherUserId);

        var existing = await _context
            .DmConversations.Find(d => d.UserAId == userAId && d.UserBId == userBId)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            return ToDto(existing, userId);
        }

        var conversation = new DmConversation
        {
            Id = ObjectId.GenerateNewId(),
            UserAId = userAId,
            UserBId = userBId,
            CreatedAt = DateTime.UtcNow,
        };

        try
        {
            await _context.DmConversations.InsertOneAsync(conversation);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Aynı anda oluşturma yarışı — mevcut kaydı tekrar oku
            conversation = await _context
                .DmConversations.Find(d => d.UserAId == userAId && d.UserBId == userBId)
                .FirstOrDefaultAsync();
        }

        return ToDto(conversation, userId);
    }

    public async Task<List<DmConversationDto>> GetConversationsAsync(string userId)
    {
        var conversations = await _context
            .DmConversations.Find(d => d.UserAId == userId || d.UserBId == userId)
            .SortByDescending(d => d.CreatedAt)
            .ToListAsync();

        return conversations.Select(c => ToDto(c, userId)).ToList();
    }

    public async Task<bool> IsParticipantAsync(string conversationId, string userId)
    {
        if (!ObjectId.TryParse(conversationId, out var objectId))
        {
            return false;
        }

        var conversation = await _context
            .DmConversations.Find(d => d.Id == objectId)
            .FirstOrDefaultAsync();

        return conversation != null && conversation.HasParticipant(userId);
    }

    private static DmConversationDto ToDto(DmConversation conversation, string requestingUserId)
    {
        var otherUserId =
            conversation.UserAId == requestingUserId ? conversation.UserBId : conversation.UserAId;

        return new DmConversationDto
        {
            Id = conversation.Id.ToString(),
            OtherUserId = otherUserId,
            CreatedAt = conversation.CreatedAt,
        };
    }
}
