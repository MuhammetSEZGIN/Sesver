using System;
using MessageService.Data;
using MessageService.DTOs;
using MessageService.Interfaces;
using MessageService.Interfaces.Repositories.IUserRepository;
using MessageService.Models;
using MessageService.Repositories.Base;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MessageService.Repositories;

public class MessageRepository : Repository<Message, ObjectId>, IMessageRepository
{
    private readonly IMongoDbContext _context;
    public MessageRepository(IMongoDbContext context, string collectionName)
     : base(context, collectionName)
    {
        _context = context;
    }

    public async Task<IEnumerable<MessageDto>> GetMessagesInChannelAsync(string channelId, int limit, int page)
    {
        var pipeline = _context.Messages.Aggregate()
        .Match(x => x.ChannelId == channelId)
        .SortByDescending(x => x.CreatedAt)
        .Skip((page - 1) * limit)
        .Limit(limit)
        .Lookup<Message, User, DetailedMessage>(
           foreignCollection: _context.Users,
            localField: x => x.SenderId,
            foreignField: x => x.Id,
            @as: DetailedMessage => DetailedMessage.Users
        )
        .Unwind<DetailedMessage, DetailedMessage>(
            x => x.Users,
                new AggregateUnwindOptions<DetailedMessage> { PreserveNullAndEmptyArrays = true }

            )
        .Project(m => new MessageDto
        {
            Id = m.Id.ToString(),
            UserName = m.Users.UserName,
            ChannelId = m.ChannelId,
            AvatarUrl = m.Users.AvatarUrl,
            SenderId = m.SenderId,
            Text = m.Text,
            CreatedAt = m.CreatedAt
        });

        return await pipeline.ToListAsync();

    }

    public async Task<IEnumerable<MessageDto>> SearchInChannelAsync(string channelId, string searchText, int limit, int page)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return await GetMessagesInChannelAsync(channelId, limit, page);
        }

        var filter = Builders<Message>.Filter.And(
            Builders<Message>.Filter.Eq(x => x.ChannelId, channelId),
            Builders<Message>.Filter.Text(searchText)
        );

        var pipeline = _context.Messages.Aggregate()
            .Match(filter)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * limit)
            .Limit(limit)
             .Lookup<Message, User, DetailedMessage>(
           foreignCollection: _context.Users,
            localField: x => x.SenderId,
            foreignField: x => x.Id,
            @as: DetailedMessage => DetailedMessage.Users
        ).Project(m => new MessageDto
        {
            Id = m.Id.ToString(),
            UserName = m.Users.UserName,
            ChannelId = m.ChannelId,
            AvatarUrl = m.Users.AvatarUrl,
            SenderId = m.SenderId,
            Text = m.Text,
            CreatedAt = m.CreatedAt
        });

        return await pipeline.ToListAsync();
    }
    
    public async Task<bool> DeleteMessagesByMessageId(ObjectId messageId)
    {
        var messages = await _context.Messages.Find(x => x.Id == messageId).ToListAsync();
        if (messages.Count == 0)
        {
            return false;
        }
        foreach (var message in messages)
        {
            await _context.Messages.DeleteOneAsync(x => x.Id == message.Id);
        }
        return true;
    }
    public async Task<bool> DeleteMessagesOfChannelByChannelId(string channelId)
    {
        var result = await _context.Messages.DeleteManyAsync(x => x.ChannelId == channelId);
        return result.DeletedCount > 0;
    }

    public async Task<bool> DeleteMessagesByClanId(string clanId)
    {
        var result = await _context.Messages.DeleteManyAsync(x => x.ClanId == clanId);
        return result.IsAcknowledged && result.DeletedCount > 0;
    }

    public async Task<Dictionary<string, Message>> GetLastMessagesByChannelIdsAsync(IEnumerable<string> channelIds)
    {
        var ids = channelIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, Message>();
        }

        var pipeline = _context.Messages.Aggregate()
            .Match(x => ids.Contains(x.ChannelId))
            .SortByDescending(x => x.CreatedAt)
            .Group(x => x.ChannelId, g => g.First());

        var results = await pipeline.ToListAsync();
        return results.ToDictionary(m => m.ChannelId, m => m);
    }

}
