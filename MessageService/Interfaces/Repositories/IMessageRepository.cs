using System;
using MessageService.DTOs;
using MessageService.Models;
using MongoDB.Bson;

namespace MessageService.Interfaces.Repositories.IUserRepository;

public interface IMessageRepository : IRepository<Message, ObjectId>
{
    Task<IEnumerable<MessageDto>> GetMessagesInChannelAsync(string channelId, int limit, int page);
    Task<IEnumerable<MessageDto>> SearchInChannelAsync(string channelId, string searchText, int limit, int page);
    Task<bool> DeleteMessagesOfChannelByChannelId(string channelId);
    Task<bool> DeleteMessagesByMessageId(ObjectId messageId);
    Task<bool> DeleteMessagesByClanId(string clanId);
    Task<Dictionary<string, Message>> GetLastMessagesByChannelIdsAsync(IEnumerable<string> channelIds);
}
