using PresenceService.Interfaces;
using PresenceService.Models;

namespace PresenceService.Repositories;

public class CallRepository : ICallRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, CallSession> _calls = new();
    private readonly Dictionary<string, Guid> _activeCallByUser = new(StringComparer.Ordinal);

    public CallActionResult TryCreate(string conversationId, string callerUserId, string calleeUserId)
    {
        lock (_gate)
        {
            if (_activeCallByUser.ContainsKey(callerUserId) || _activeCallByUser.ContainsKey(calleeUserId))
                return new(false, "busy", null);

            var call = new CallSession(Guid.NewGuid(), conversationId, callerUserId, calleeUserId,
                DateTime.UtcNow, CallStatus.Ringing);
            _calls[call.CallId] = call;
            _activeCallByUser[callerUserId] = call.CallId;
            _activeCallByUser[calleeUserId] = call.CallId;
            return new(true, "ringing", call);
        }
    }

    public CallActionResult Accept(Guid callId, string actorUserId) =>
        Transition(callId, actorUserId, CallStatus.Ringing, CallStatus.Accepted,
            call => call.CalleeUserId == actorUserId, release: false);

    public CallActionResult Reject(Guid callId, string actorUserId) =>
        Transition(callId, actorUserId, CallStatus.Ringing, CallStatus.Rejected,
            call => call.CalleeUserId == actorUserId, release: true);

    public CallActionResult Cancel(Guid callId, string actorUserId) =>
        Transition(callId, actorUserId, CallStatus.Ringing, CallStatus.Cancelled,
            call => call.CallerUserId == actorUserId, release: true);

    public CallActionResult End(Guid callId, string actorUserId) =>
        Transition(callId, actorUserId, CallStatus.Accepted, CallStatus.Ended,
            call => IsParticipant(call, actorUserId), release: true);

    public CallActionResult EndAcceptedForUser(string userId, string conversationId)
    {
        lock (_gate)
        {
            if (!_activeCallByUser.TryGetValue(userId, out var callId) ||
                !_calls.TryGetValue(callId, out var call) || call.Status != CallStatus.Accepted ||
                !string.Equals(call.ConversationId, conversationId, StringComparison.Ordinal))
                return new(false, "not-found", null);
            return Finish(call, CallStatus.Ended);
        }
    }

    public IReadOnlyList<CallSession> TimeoutExpired(DateTime cutoffUtc)
    {
        lock (_gate)
        {
            var expired = _calls.Values
                .Where(x => x.Status == CallStatus.Ringing && x.CreatedAt <= cutoffUtc)
                .ToList();
            return expired.Select(x => Finish(x, CallStatus.TimedOut).Call!).ToList();
        }
    }

    public CallSession? HandleUserOffline(string userId)
    {
        lock (_gate)
        {
            if (!_activeCallByUser.TryGetValue(userId, out var callId) || !_calls.TryGetValue(callId, out var call))
                return null;
            if (call.Status == CallStatus.Ringing && call.CalleeUserId == userId)
                return null;
            var status = call.Status == CallStatus.Ringing ? CallStatus.Cancelled : CallStatus.Ended;
            return Finish(call, status).Call;
        }
    }

    public CallSession? TerminateRelationship(string userAId, string userBId)
    {
        lock (_gate)
        {
            if (!_activeCallByUser.TryGetValue(userAId, out var callId) || !_calls.TryGetValue(callId, out var call))
                return null;
            if (!IsParticipant(call, userBId)) return null;
            return Finish(call, call.Status == CallStatus.Ringing ? CallStatus.Cancelled : CallStatus.Ended).Call;
        }
    }

    private CallActionResult Transition(
        Guid callId,
        string actorUserId,
        CallStatus expected,
        CallStatus target,
        Func<CallSession, bool> authorized,
        bool release)
    {
        lock (_gate)
        {
            if (!_calls.TryGetValue(callId, out var call)) return new(false, "not-found", null);
            if (!authorized(call)) return new(false, "forbidden", null);
            if (call.Status != expected) return new(false, "invalid-state", call);
            var transitioned = call with { Status = target };
            _calls[callId] = transitioned;
            if (release) Release(transitioned);
            return new(true, target.ToString().ToLowerInvariant(), transitioned);
        }
    }

    private CallActionResult Finish(CallSession call, CallStatus status)
    {
        var transitioned = call with { Status = status };
        _calls[call.CallId] = transitioned;
        Release(transitioned);
        return new(true, status.ToString().ToLowerInvariant(), transitioned);
    }

    private void Release(CallSession call)
    {
        _activeCallByUser.Remove(call.CallerUserId);
        _activeCallByUser.Remove(call.CalleeUserId);
        _calls.Remove(call.CallId);
    }

    private static bool IsParticipant(CallSession call, string userId) =>
        call.CallerUserId == userId || call.CalleeUserId == userId;
}
