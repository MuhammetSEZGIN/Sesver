using System;
using MassTransit;
using MessageService.DTOs;
using MessageService.Models;
using MongoDB.Bson;

namespace MessageService.Interfaces.Services;

public interface IMessageService 
{
    Task<ServiceResult<IEnumerable<MessageDto>>> GetMessagesInChannelAsync(string channelId, int limit, int page);
    Task <ServiceResult<Message>>UpdateMessage( ObjectId messageId, string newContent, string requesterId);
    Task<ServiceResult<bool>> DeleteMessageAsync(ObjectId messageId, string requesterId);
    Task<ServiceResult<Message>> CreateMessage(Message message);
}
