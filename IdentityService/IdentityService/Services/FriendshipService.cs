using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IdentityService.Data;
using IdentityService.DTOs;
using IdentityService.Interfaces;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Contracts;

namespace IdentityService.Services;

public class FriendshipService : IFriendshipService
{
    private readonly IdentityDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<FriendshipService> _logger;
    private readonly IIdentityProducer _producer;

    public FriendshipService(
        IdentityDbContext context,
        UserManager<ApplicationUser> userManager,
        ILogger<FriendshipService> logger,
        IIdentityProducer producer
    )
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
        _producer = producer;
    }

    public Task<List<string>> GetFriendIdsAsync(string userId) =>
        _context.Friendships.AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Accepted &&
                (f.RequesterId == userId || f.AddresseeId == userId))
            .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId)
            .ToListAsync();

    public async Task<List<FriendshipReadDto>> GetFriendsAsync(string userId)
    {
        var friendships = await _context
            .Friendships.AsNoTracking()
            .Where(f =>
                f.Status == FriendshipStatus.Accepted
                && (f.RequesterId == userId || f.AddresseeId == userId)
            )
            .ToListAsync();

        var result = new List<FriendshipReadDto>();
        foreach (var f in friendships)
        {
            var friendId = f.RequesterId == userId ? f.AddresseeId : f.RequesterId;
            var friend = await _userManager.FindByIdAsync(friendId);
            if (friend == null)
                continue;

            result.Add(
                new FriendshipReadDto
                {
                    Id = f.Id,
                    UserId = friend.Id,
                    UserName = friend.UserName,
                    AvatarUrl = friend.AvatarUrl,
                    Status = f.Status.ToString(),
                    CreatedAt = f.CreatedAt,
                    RespondedAt = f.RespondedAt,
                }
            );
        }
        return result;
    }

    public async Task<List<FriendshipReadDto>> GetPendingRequestsAsync(string userId)
    {
        var requests = await _context
            .Friendships.AsNoTracking()
            .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Pending)
            .ToListAsync();

        var result = new List<FriendshipReadDto>();
        foreach (var f in requests)
        {
            var requester = await _userManager.FindByIdAsync(f.RequesterId);
            if (requester == null)
                continue;

            result.Add(
                new FriendshipReadDto
                {
                    Id = f.Id,
                    UserId = requester.Id,
                    UserName = requester.UserName,
                    AvatarUrl = requester.AvatarUrl,
                    Status = f.Status.ToString(),
                    CreatedAt = f.CreatedAt,
                    RespondedAt = f.RespondedAt,
                }
            );
        }
        return result;
    }

    public async Task<(bool Succeeded, string Message, FriendshipReadDto Data)> SendRequestAsync(
        string requesterId,
        string addresseeId
    )
    {
        if (requesterId == addresseeId)
        {
            return (false, "You cannot send a friend request to yourself", null);
        }

        var addressee = await _userManager.FindByIdAsync(addresseeId);
        if (addressee == null)
        {
            return (false, "User not found", null);
        }

        var existing = await _context.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == requesterId && f.AddresseeId == addresseeId)
            || (f.RequesterId == addresseeId && f.AddresseeId == requesterId)
        );

        if (existing != null)
        {
            if (existing.Status == FriendshipStatus.Accepted)
            {
                return (false, "You are already friends with this user", null);
            }

            if (existing.Status == FriendshipStatus.Pending)
            {
                if (existing.RequesterId == requesterId)
                {
                    return (false, "Friend request already sent", null);
                }

                // Reverse pending request already exists — auto-accept instead of creating a duplicate.
                existing.Status = FriendshipStatus.Accepted;
                existing.RespondedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await PublishSafelyAsync(
                    new NotificationRequestedMessage
                    {
                        EventId = Guid.NewGuid(),
                        UserId = existing.RequesterId,
                        Type = NotificationType.FriendRequestAccepted,
                        Title = "Friend request accepted",
                        Body = "Your friend request was accepted.",
                        ActorUserId = requesterId,
                        TargetId = existing.Id.ToString(),
                        CreatedAt = DateTime.UtcNow,
                    },
                    new FriendshipRelationshipChangedMessage
                    {
                        EventId = Guid.NewGuid(),
                        UserAId = existing.RequesterId,
                        UserBId = existing.AddresseeId,
                        Status = FriendshipRelationshipStatus.Accepted,
                        ChangedAt = DateTime.UtcNow,
                    }
                );

                var requester = await _userManager.FindByIdAsync(existing.RequesterId);
                return (
                    true,
                    "Friend request accepted",
                    new FriendshipReadDto
                    {
                        Id = existing.Id,
                        UserId = requester.Id,
                        UserName = requester.UserName,
                        AvatarUrl = requester.AvatarUrl,
                        Status = existing.Status.ToString(),
                        CreatedAt = existing.CreatedAt,
                        RespondedAt = existing.RespondedAt,
                    }
                );
            }

            // Blocked
            return (false, "Unable to send friend request to this user", null);
        }

        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = requesterId,
            AddresseeId = addresseeId,
            Status = FriendshipStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };
        _context.Friendships.Add(friendship);
        await _context.SaveChangesAsync();

        await PublishNotificationSafelyAsync(new NotificationRequestedMessage
        {
            EventId = Guid.NewGuid(),
            UserId = addresseeId,
            Type = NotificationType.FriendRequestReceived,
            Title = "New friend request",
            Body = "You received a new friend request.",
            ActorUserId = requesterId,
            TargetId = friendship.Id.ToString(),
            CreatedAt = DateTime.UtcNow,
        });

        _logger.LogInformation(
            "Friend request sent from {RequesterId} to {AddresseeId}",
            requesterId,
            addresseeId
        );

        return (
            true,
            "Friend request sent",
            new FriendshipReadDto
            {
                Id = friendship.Id,
                UserId = addressee.Id,
                UserName = addressee.UserName,
                AvatarUrl = addressee.AvatarUrl,
                Status = friendship.Status.ToString(),
                CreatedAt = friendship.CreatedAt,
                RespondedAt = friendship.RespondedAt,
            }
        );
    }

    public async Task<(bool Succeeded, string Message)> AcceptRequestAsync(
        string userId,
        Guid requestId
    )
    {
        var friendship = await _context.Friendships.FirstOrDefaultAsync(f => f.Id == requestId);
        if (friendship == null || friendship.AddresseeId != userId)
        {
            return (false, "Friend request not found");
        }

        if (friendship.Status != FriendshipStatus.Pending)
        {
            return (false, "Friend request is not pending");
        }

        friendship.Status = FriendshipStatus.Accepted;
        friendship.RespondedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await PublishSafelyAsync(
            new NotificationRequestedMessage
            {
                EventId = Guid.NewGuid(),
                UserId = friendship.RequesterId,
                Type = NotificationType.FriendRequestAccepted,
                Title = "Friend request accepted",
                Body = "Your friend request was accepted.",
                ActorUserId = friendship.AddresseeId,
                TargetId = friendship.Id.ToString(),
                CreatedAt = DateTime.UtcNow,
            },
            new FriendshipRelationshipChangedMessage
            {
                EventId = Guid.NewGuid(),
                UserAId = friendship.RequesterId,
                UserBId = friendship.AddresseeId,
                Status = FriendshipRelationshipStatus.Accepted,
                ChangedAt = DateTime.UtcNow,
            }
        );

        return (true, "Friend request accepted");
    }

    public async Task<(bool Succeeded, string Message)> RejectRequestAsync(
        string userId,
        Guid requestId
    )
    {
        var friendship = await _context.Friendships.FirstOrDefaultAsync(f => f.Id == requestId);
        if (friendship == null || friendship.AddresseeId != userId)
        {
            return (false, "Friend request not found");
        }

        if (friendship.Status != FriendshipStatus.Pending)
        {
            return (false, "Friend request is not pending");
        }

        _context.Friendships.Remove(friendship);
        await _context.SaveChangesAsync();

        return (true, "Friend request rejected");
    }

    public async Task<(bool Succeeded, string Message)> RemoveFriendAsync(
        string userId,
        string friendUserId
    )
    {
        var friendship = await _context.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == userId && f.AddresseeId == friendUserId)
            || (f.RequesterId == friendUserId && f.AddresseeId == userId)
        );

        if (friendship == null)
        {
            return (false, "Friendship not found");
        }

        _context.Friendships.Remove(friendship);
        await _context.SaveChangesAsync();

        await PublishRelationshipSafelyAsync(new FriendshipRelationshipChangedMessage
        {
            EventId = Guid.NewGuid(),
            UserAId = friendship.RequesterId,
            UserBId = friendship.AddresseeId,
            Status = FriendshipRelationshipStatus.Removed,
            ChangedAt = DateTime.UtcNow,
        });

        return (true, "Friend removed");
    }

    private async Task PublishSafelyAsync(
        NotificationRequestedMessage notification,
        FriendshipRelationshipChangedMessage relationship)
    {
        await PublishNotificationSafelyAsync(notification);
        await PublishRelationshipSafelyAsync(relationship);
    }

    private async Task PublishNotificationSafelyAsync(NotificationRequestedMessage message)
    {
        try
        {
            await _producer.PublishNotificationAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not publish notification event {EventId}", message.EventId);
        }
    }

    private async Task PublishRelationshipSafelyAsync(FriendshipRelationshipChangedMessage message)
    {
        try
        {
            await _producer.PublishFriendshipRelationshipChangedAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not publish friendship relationship event {EventId}", message.EventId);
        }
    }
}
