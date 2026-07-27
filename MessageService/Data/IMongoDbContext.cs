using System;
using MessageService.Models;
using MongoDB.Driver;

namespace MessageService.Data;

public interface IMongoDbContext
{

    IMongoCollection<User> Users { get; }
    IMongoCollection<Message> Messages { get; }
    IMongoCollection<DmConversation> DmConversations { get; }
    // Genel koleksiyon alma metodu
    IMongoCollection<T> GetCollection<T>(string name);
}
