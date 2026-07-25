namespace MessageService.Interfaces.Services;

public interface IMessageConnectionTracker
{
    void JoinChannel(string userId, string connectionId, string channelId);
    void LeaveChannel(string userId, string connectionId, string channelId);
    void RemoveConnection(string connectionId);
    bool IsUserInChannel(string userId, string channelId);
}
