using MessageService.DTOs;
using MessageService.Interfaces.Repositories.IUserRepository;
using MessageService.Models;
using MessageService.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;

namespace FeatureTests;

public class MessageClanBoundaryTests
{
    [Fact]
    public async Task DeleteMessage_RejectsMessageFromDifferentClan()
    {
        var messageId = ObjectId.GenerateNewId();
        var repository = new Mock<IMessageRepository>();
        repository.Setup(x => x.GetByIdAsync(messageId)).ReturnsAsync(
            new Message
            {
                Id = messageId,
                SenderId = "user-1",
                ClanId = "clan-a",
                ChannelId = "channel-a",
                Text = "test"
            }
        );
        var service = CreateService(repository);

        var result = await service.DeleteMessageAsync(messageId, "user-1", "clan-b");

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        repository.Verify(
            x => x.DeleteMessagesByMessageId(It.IsAny<ObjectId>()),
            Times.Never
        );
    }

    [Fact]
    public async Task UpdateMessage_RejectsMessageFromDifferentClan()
    {
        var messageId = ObjectId.GenerateNewId();
        var repository = new Mock<IMessageRepository>();
        repository.Setup(x => x.GetByIdAsync(messageId)).ReturnsAsync(
            new Message
            {
                Id = messageId,
                SenderId = "user-1",
                ClanId = "clan-a",
                ChannelId = "channel-a",
                Text = "old"
            }
        );
        var service = CreateService(repository);

        var result = await service.UpdateMessage(
            messageId,
            "new",
            "user-1",
            "clan-b"
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        repository.Verify(
            x => x.UpdateAsync(It.IsAny<ObjectId>(), It.IsAny<Message>()),
            Times.Never
        );
    }

    [Fact]
    public async Task GetMessages_FiltersByChannelAndClanTogether()
    {
        var repository = new Mock<IMessageRepository>();
        repository.Setup(x => x.GetMessagesInChannelAsync("channel-a", "clan-a", 20, 1))
            .ReturnsAsync(Array.Empty<MessageDto>());
        var service = CreateService(repository);

        var result = await service.GetMessagesInChannelAsync(
            "channel-a",
            "clan-a",
            20,
            1
        );

        Assert.True(result.IsSuccess);
        repository.Verify(
            x => x.GetMessagesInChannelAsync("channel-a", "clan-a", 20, 1),
            Times.Once
        );
    }

    private static MessageService.Services.MessageService CreateService(
        Mock<IMessageRepository> repository
    )
    {
        return new MessageService.Services.MessageService(
            repository.Object,
            Mock.Of<ILogger<MessageService.Services.MessageService>>()
        );
    }
}
