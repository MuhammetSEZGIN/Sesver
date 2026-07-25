using PresenceService.Models;

namespace PresenceService.Interfaces;

public interface ICallRepository
{
    CallActionResult TryCreate(string conversationId, string callerUserId, string calleeUserId);
    CallActionResult Accept(Guid callId, string actorUserId);
    CallActionResult Reject(Guid callId, string actorUserId);
    CallActionResult Cancel(Guid callId, string actorUserId);
    CallActionResult End(Guid callId, string actorUserId);
    CallActionResult EndAcceptedForUser(string userId, string conversationId);
    IReadOnlyList<CallSession> TimeoutExpired(DateTime cutoffUtc);
    CallSession? HandleUserOffline(string userId);
    CallSession? TerminateRelationship(string userAId, string userBId);
}
