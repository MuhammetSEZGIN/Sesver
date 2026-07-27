using PresenceService.Models;

namespace PresenceService.Interfaces;

public interface ICallRepository
{
    CallActionResult TryCreate(
        string conversationId,
        string callerUserId,
        string calleeUserId,
        string callerConnectionId);
    CallActionResult Accept(Guid callId, string actorUserId, string calleeConnectionId);
    CallActionResult Reject(Guid callId, string actorUserId);
    CallActionResult Cancel(Guid callId, string actorUserId);
    CallActionResult End(Guid callId, string actorUserId);
    CallActionResult EndAcceptedForUser(string userId, string conversationId);
    IReadOnlyList<CallSession> TimeoutExpired(DateTime cutoffUtc);
    CallSession? HandleUserOffline(string userId);
    CallSession? TerminateRelationship(string userAId, string userBId);
}
