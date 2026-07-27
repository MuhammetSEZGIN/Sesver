namespace PresenceService.Models;

public enum CallStatus
{
    Ringing,
    Accepted,
    Rejected,
    Cancelled,
    TimedOut,
    Ended
}

public record CallSession(
    Guid CallId,
    string ConversationId,
    string CallerUserId,
    string CalleeUserId,
    string CallerConnectionId,
    string? CalleeConnectionId,
    DateTime CreatedAt,
    CallStatus Status)
{
    public string RoomId => $"dm-{ConversationId}";
}

public record CallActionResult(bool Succeeded, string Code, CallSession? Call);
