using System;

namespace MessageService.DTOs;

public class DmConversationCreateDto
{
    public string OtherUserId { get; set; }
}

public class DmConversationDto
{
    public string Id { get; set; }
    public string OtherUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
