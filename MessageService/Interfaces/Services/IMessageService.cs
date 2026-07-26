using System;
using MassTransit;
using MessageService.DTOs;
using MessageService.Models;
using MongoDB.Bson;

namespace MessageService.Interfaces.Services;

public interface IMessageService 
{
    Task<ServiceResult<IEnumerable<MessageDto>>> GetMessagesInChannelAsync(string channelId, string clanId, int limit, int page);
    Task<ServiceResult<Message>> UpdateMessage(ObjectId messageId, string newContent, string requesterId, string expectedClanId);
    Task<ServiceResult<bool>> DeleteMessageAsync(ObjectId messageId, string requesterId, string expectedClanId);
    Task<ServiceResult<Message>> CreateMessage(Message message);
}
