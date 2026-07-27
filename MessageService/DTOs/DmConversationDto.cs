using System;

namespace MessageService.DTOs;

public class DmConversationCreateDto
{
    public string OtherUserId { get; set; }
}

public class DmConversationDto
{
    /// <summary>Aynı zamanda MessageDto.ChannelId olarak kullanılır.</summary>
    public string ConversationId { get; set; }
    public string OtherUserId { get; set; }
    public string OtherUserName { get; set; }
    public string OtherAvatarUrl { get; set; }
    public string LastMessage { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DmCallContextDto
{
    public string ConversationId { get; set; }
    public string OtherUserId { get; set; }
}
