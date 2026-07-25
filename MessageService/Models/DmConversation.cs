using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MessageService.Models;

[BsonIgnoreExtraElements]
public class DmConversation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public ObjectId Id { get; set; }

    [BsonRepresentation(BsonType.String)]
    public string UserAId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public string UserBId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool HasParticipant(string userId) => UserAId == userId || UserBId == userId;

    public static (string UserAId, string UserBId) Canonicalize(string userId1, string userId2)
    {
        return string.CompareOrdinal(userId1, userId2) <= 0
            ? (userId1, userId2)
            : (userId2, userId1);
    }
}
