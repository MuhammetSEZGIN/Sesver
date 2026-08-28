using IdentityService.Messaging;
using IdentityServiceTests.Mocks;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Contracts;

namespace IdentityServiceTests.UnitTests.Services;

public class IdentityProducerTests
{
    [Fact]
    public async Task PublishUserDeletedMessageAsync_PublishesUserId()
    {
        var publishEndpoint = new MockPublishEndpoint();
        var producer = new IdentityProducer(
            publishEndpoint,
            Mock.Of<ILogger<IdentityProducer>>());

        await producer.PublishUserDeletedMessageAsync("user-1");

        var message = Assert.IsType<UserDeletedMessage>(
            Assert.Single(publishEndpoint.PublishedMessages));
        Assert.Equal("user-1", message.UserId);
    }
}
