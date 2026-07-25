using MessageService.Data;
using MessageService.DTOs;
using MessageService.Interfaces.Repositories.IUserRepository;
using MessageService.Interfaces.Services;
using MessageService.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MessageService.Services;

public class DmConversationService : IDmConversationService
{
    private readonly IMongoDbContext _context;
    private readonly IUserRepository _userRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly ILogger<DmConversationService> _logger;

    public DmConversationService(
        IMongoDbContext context,
        IUserRepository userRepository,
        IMessageRepository messageRepository,
        ILogger<DmConversationService> logger)
    {
        _context = context;
        _userRepository = userRepository;
        _messageRepository = messageRepository;
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
            return await ToDtoAsync(existing, userId);
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

        return await ToDtoAsync(conversation, userId);
    }

    public async Task<List<DmConversationDto>> GetConversationsAsync(string userId)
    {
        var conversations = await _context
            .DmConversations.Find(d => d.UserAId == userId || d.UserBId == userId)
            .SortByDescending(d => d.CreatedAt)
            .ToListAsync();

        if (conversations.Count == 0)
        {
            return new List<DmConversationDto>();
        }

        var channelIds = conversations.Select(c => c.Id.ToString()).ToList();
        var lastMessages = await _messageRepository.GetLastMessagesByChannelIdsAsync(channelIds);

        var dtos = new List<DmConversationDto>();
        foreach (var conversation in conversations)
        {
            var otherUserId = conversation.UserAId == userId ? conversation.UserBId : conversation.UserAId;
            var otherUser = await _userRepository.GetByIdAsync(otherUserId);
            lastMessages.TryGetValue(conversation.Id.ToString(), out var lastMessage);

            dtos.Add(new DmConversationDto
            {
                ConversationId = conversation.Id.ToString(),
                OtherUserId = otherUserId,
                OtherUserName = otherUser?.UserName,
                OtherAvatarUrl = otherUser?.AvatarUrl,
                LastMessage = lastMessage?.Text,
                LastMessageAt = lastMessage?.CreatedAt,
                CreatedAt = conversation.CreatedAt,
            });
        }

        return dtos;
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

    public async Task<DmCallContextDto?> GetCallContextAsync(string conversationId, string userId)
    {
        if (!ObjectId.TryParse(conversationId, out var objectId))
        {
            return null;
        }

        var conversation = await _context.DmConversations
            .Find(d => d.Id == objectId && (d.UserAId == userId || d.UserBId == userId))
            .FirstOrDefaultAsync();

        if (conversation == null)
        {
            return null;
        }

        return new DmCallContextDto
        {
            ConversationId = conversation.Id.ToString(),
            OtherUserId = conversation.UserAId == userId ? conversation.UserBId : conversation.UserAId,
        };
    }

    private async Task<DmConversationDto> ToDtoAsync(DmConversation conversation, string requestingUserId)
    {
        var otherUserId =
            conversation.UserAId == requestingUserId ? conversation.UserBId : conversation.UserAId;
        var otherUser = await _userRepository.GetByIdAsync(otherUserId);

        return new DmConversationDto
        {
            ConversationId = conversation.Id.ToString(),
            OtherUserId = otherUserId,
            OtherUserName = otherUser?.UserName,
            OtherAvatarUrl = otherUser?.AvatarUrl,
            CreatedAt = conversation.CreatedAt,
        };
    }
}
