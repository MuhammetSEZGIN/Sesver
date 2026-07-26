using System;
using System.Security.Claims;
using MessageService.DTOs;
using MessageService.Interfaces.Services;
using MessageService.Models;
using MessageService.Services;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MassTransit;
using Shared.Contracts;

namespace MessageService.Hubs;

public class MessageHub : Hub
{
    private readonly IMessageService _messageService;
    private readonly IUserService _userService;
    private readonly IDmConversationService _dmConversationService;
    private readonly ILogger<MessageHub> _logger;
    private readonly IBackgroundTaskQueue _backgroundTaskQueue;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IMessageConnectionTracker _connectionTracker;

    public MessageHub(IMessageService messageService,
        ILogger<MessageHub> logger,
        IServiceScopeFactory serviceScopeFactory,
        IBackgroundTaskQueue backgroundTaskQueue,
        IUserService userService,
        IDmConversationService dmConversationService,
        IMessageConnectionTracker connectionTracker
      )
    {
        _backgroundTaskQueue = backgroundTaskQueue;
        _serviceScopeFactory = serviceScopeFactory;
        _messageService = messageService;
        _userService = userService;
        _dmConversationService = dmConversationService;
        _connectionTracker = connectionTracker;
        _logger = logger;
    }

    private string GetUserId() => Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private string GetConnectionClanId() => Context.User?.FindFirst("clan_id")?.Value;

    private bool TryResolveChannelContext(
        string channelId,
        string requestedClanId,
        out string normalizedClanId,
        out string groupName
    )
    {
        normalizedClanId = null;
        groupName = string.Empty;

        if (_dmConversationService.IsDmConversationId(channelId))
        {
            if (!string.IsNullOrWhiteSpace(requestedClanId)
                || !string.IsNullOrWhiteSpace(GetConnectionClanId()))
            {
                return false;
            }

            groupName = channelId;
            return true;
        }

        if (!Guid.TryParse(channelId, out var parsedChannelId)
            || !Guid.TryParse(requestedClanId, out var parsedRequestedClanId)
            || !Guid.TryParse(GetConnectionClanId(), out var parsedConnectionClanId)
            || parsedRequestedClanId != parsedConnectionClanId
            || !(Context.User?.IsInRole("OWNER") == true
                 || Context.User?.IsInRole("ADMIN") == true
                 || Context.User?.IsInRole("MEMBER") == true))
        {
            return false;
        }

        normalizedClanId = parsedConnectionClanId.ToString("D");
        groupName = $"clan:{normalizedClanId}:channel:{parsedChannelId:D}";
        return true;
    }

    private async Task<bool> CanJoinChannelAsync(string channelId, string clanId)
    {
        if (!TryResolveChannelContext(channelId, clanId, out _, out _))
        {
            return false;
        }

        return !_dmConversationService.IsDmConversationId(channelId)
            || await _dmConversationService.IsParticipantAsync(channelId, GetUserId());
    }

    private bool TryResolveMutationClan(string requestedClanId, out string normalizedClanId)
    {
        normalizedClanId = null;
        var connectionClanId = GetConnectionClanId();

        if (string.IsNullOrWhiteSpace(requestedClanId))
        {
            return string.IsNullOrWhiteSpace(connectionClanId);
        }

        if (!Guid.TryParse(requestedClanId, out var requested)
            || !Guid.TryParse(connectionClanId, out var connected)
            || requested != connected
            || !(Context.User?.IsInRole("OWNER") == true
                 || Context.User?.IsInRole("ADMIN") == true
                 || Context.User?.IsInRole("MEMBER") == true))
        {
            return false;
        }

        normalizedClanId = connected.ToString("D");
        return true;
    }

    public async Task SendMessage(string channelId, string clanId, string message)
    {
        var userId = GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("SendMessage attempted without an authenticated user");
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("Empty message attempted to be sent by {UserId}", userId);
            return;
        }

        if (!TryResolveChannelContext(
                channelId,
                clanId,
                out var normalizedClanId,
                out var groupName)
            || !_connectionTracker.IsConnectionInChannel(
                userId,
                Context.ConnectionId,
                groupName))
        {
            _logger.LogWarning("User {UserId} is not authorized for channel {ChannelId}", userId, channelId);
            await Clients.Caller.SendAsync("MessageSendFailed", "You are not authorized to send messages in this channel");
            return;
        }

        try
        {
            var userName = await _userService.GetUserNameByIdAsync(userId);
            var objectId = ObjectId.GenerateNewId();
            var messageDto = new MessageDto
            {
                Id = objectId.ToString(),
                ClanId = normalizedClanId,
                UserName = userName ?? "Unknown",
                ChannelId = channelId,
                SenderId = userId,
                Text = message,
                CreatedAt = DateTime.UtcNow
            };

            string notificationRecipientId = null;
            if (normalizedClanId is null && _dmConversationService.IsDmConversationId(channelId))
            {
                var callContext = await _dmConversationService.GetCallContextAsync(channelId, userId);
                if (callContext != null && !_connectionTracker.IsUserInChannel(callContext.OtherUserId, channelId))
                {
                    notificationRecipientId = callContext.OtherUserId;
                }
            }

            await Clients.Group(groupName).SendAsync("ReceiveMessage", messageDto);

            var newMessage = new Message
            {
                Id = objectId,
                ClanId = normalizedClanId,
                ChannelId = channelId,
                SenderId = userId,
                Text = message,
                CreatedAt = DateTime.UtcNow
            };

            await _backgroundTaskQueue.QueueBackgroundWorkItemAsync(async token =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var messageService = scope.ServiceProvider.GetRequiredService<IMessageService>();
                    var saveResult = await messageService.CreateMessage(newMessage);
                    if (!saveResult.IsSuccess)
                    {
                        _logger.LogError("Message {MessageId} could not be saved: {Reason}", newMessage.Id, saveResult.Message);
                        return;
                    }

                    _logger.LogInformation("Message {MessageId} succesfully saved to database", newMessage.Id);

                    if (!string.IsNullOrEmpty(notificationRecipientId))
                    {
                        try
                        {
                            var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
                            await publisher.Publish(new NotificationRequestedMessage
                            {
                                EventId = Guid.NewGuid(),
                                UserId = notificationRecipientId,
                                Type = NotificationType.DirectMessageReceived,
                                Title = $"New message from {messageDto.UserName}",
                                Body = message.Length <= 120 ? message : $"{message[..117]}...",
                                ActorUserId = userId,
                                TargetId = channelId,
                                CreatedAt = messageDto.CreatedAt,
                            }, token);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Could not publish notification for message {MessageId}", newMessage.Id);
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error saving message to database");
                }
            });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in SendMessage for channel {ChannelId} by {UserId}", channelId, userId);
            throw;
        }
    }

    public async Task UpdateMessage(string messageId, string clanId, string newContent)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(newContent))
        {
            _logger.LogWarning("Empty content in UpdateMessage for {MessageId}", messageId);
            return;
        }
        if (string.IsNullOrEmpty(messageId))
        {
            _logger.LogWarning("Empty message id in UpdateMessage");
            return;
        }

        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var messageService = scope.ServiceProvider.GetRequiredService<IMessageService>();
                var objectId = ObjectId.Parse(messageId);
                if (!TryResolveMutationClan(clanId, out var normalizedClanId))
                {
                    await Clients.Caller.SendAsync("MessageUpdateFailed", messageId);
                    return;
                }

                var result = await messageService.UpdateMessage(
                    objectId,
                    newContent,
                    userId,
                    normalizedClanId
                );
                if (!result.IsSuccess)
                {
                    _logger.LogWarning("Message {MessageId} could not be updated: {Reason}", messageId, result.Message);
                    await Clients.Caller.SendAsync("MessageUpdateFailed", messageId);
                    return;
                }

                var userName = await _userService.GetUserNameByIdAsync(result.Data.SenderId);

                var messageDto = new MessageDto
                {
                    Id = result.Data.Id.ToString(),
                    ClanId = result.Data.ClanId,
                    ChannelId = result.Data.ChannelId,
                    UserName = userName ?? "Unknown",
                    SenderId = result.Data.SenderId,
                    Text = result.Data.Text,
                    CreatedAt = result.Data.CreatedAt
                };
                if (!TryResolveChannelContext(
                        result.Data.ChannelId,
                        result.Data.ClanId,
                        out _,
                        out var groupName))
                {
                    await Clients.Caller.SendAsync("MessageUpdateFailed", messageId);
                    return;
                }

                await Clients.Group(groupName).SendAsync("MessageUpdated", messageDto);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error updating message {MessageId}", messageId);
                await Clients.Caller.SendAsync("MessageUpdateFailed", messageId);
            }
        }
    }

    public async Task DeleteMessage(string messageId, string channelId, string clanId)
    {
        var userId = GetUserId();
        var objectId = ObjectId.Parse(messageId);
        if (objectId == ObjectId.Empty)
        {
            _logger.LogWarning("Empty message id in DeleteMessage");
            await Clients.Caller.SendAsync("MessageDeleteFailed", messageId.ToString());
            return;
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            _logger.LogWarning("Empty channel id in DeleteMessage for {MessageId}", messageId);
            await Clients.Caller.SendAsync("MessageDeleteFailed", messageId.ToString());
            return;
        }

        try
        {
            if (!TryResolveChannelContext(
                    channelId,
                    clanId,
                    out var normalizedClanId,
                    out var groupName)
                || !_connectionTracker.IsConnectionInChannel(
                    userId,
                    Context.ConnectionId,
                    groupName))
            {
                await Clients.Caller.SendAsync("MessageDeleteFailed", messageId);
                return;
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var result = await _messageService.DeleteMessageAsync(
                objectId,
                userId,
                normalizedClanId
            );

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Message {MessageId} could not be deleted: {Reason}", messageId, result.Message);
                await Clients.Caller.SendAsync("MessageDeleteFailed", messageId.ToString());
                return;
            }

            await Clients.Group(groupName).SendAsync("MessageDeleted", messageId);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error deleting message {MessageId}", messageId);
            await Clients.Caller.SendAsync("MessageDeleteFailed", messageId);
        }

    }

    public async Task JoinChannel(string channelId, string clanId)
    {
        var userId = GetUserId();
        if (!await CanJoinChannelAsync(channelId, clanId)
            || !TryResolveChannelContext(
                channelId,
                clanId,
                out _,
                out var groupName))
        {
            _logger.LogWarning("User {UserId} is not authorized to join channel {ChannelId}", userId, channelId);
            await Clients.Caller.SendAsync("JoinChannelFailed", channelId);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _connectionTracker.JoinChannel(userId, Context.ConnectionId, groupName);
    }

    public async Task LeaveChannel(string channelId)
    {
        var userId = GetUserId();
        var clanId = GetConnectionClanId();
        if (!TryResolveChannelContext(channelId, clanId, out _, out var groupName))
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        if (!string.IsNullOrEmpty(userId))
        {
            _connectionTracker.LeaveChannel(userId, Context.ConnectionId, groupName);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connectionTracker.RemoveConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
