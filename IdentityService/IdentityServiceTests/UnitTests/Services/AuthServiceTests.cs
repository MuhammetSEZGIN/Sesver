using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IdentityService.Data;
using IdentityService.DTOs;
using IdentityService.Interfaces;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace IdentityServiceTests.UnitTests.Services
{
    public class AuthServiceTests
    {
        private static IConfiguration BuildConfig() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "JWT:Key", "test-signing-key-12345678901234567890" },
                    { "JWT:Issuer", "test-issuer" },
                    { "JWT:Audience", "test-audience" }
                })
                .Build();

        private static IdentityDbContext BuildContext(string name)
        {
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(name)
                .Options;
            return new IdentityDbContext(options);
        }

        private static Mock<UserManager<ApplicationUser>> BuildUserManager()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(
                store.Object, null, null, null, null, null, null, null, null);
        }

        private AuthService CreateService(
            IdentityDbContext context,
            Mock<UserManager<ApplicationUser>> userManagerMock,
            Mock<IRefreshTokenService> refreshTokenServiceMock,
            Mock<IIpAddressService> ipMock)
        {
            var logger = new Mock<ILogger<AuthService>>();
            var producer = new Mock<IdentityService.Messaging.IdentityProducer>(
                Mock.Of<MassTransit.IPublishEndpoint>(),
                Mock.Of<ILogger<IdentityService.Messaging.IdentityProducer>>());

            return new AuthService(
                userManagerMock.Object,
                BuildConfig(),
                producer.Object,
                logger.Object,
                context,
                ipMock.Object,
                refreshTokenServiceMock.Object);
        }

        [Fact]
        public async Task LoginAsync_ReturnsTokens_ForValidUser()
        {
            var context = BuildContext(nameof(LoginAsync_ReturnsTokens_ForValidUser));
            var userManager = BuildUserManager();
            var refreshService = new Mock<IRefreshTokenService>();
            var ipMock = new Mock<IIpAddressService>();

            var user = new ApplicationUser { Id = "u1", UserName = "john" };
            userManager.Setup(x => x.FindByNameAsync("john")).ReturnsAsync(user);
            userManager.Setup(x => x.CheckPasswordAsync(user, "Pass123!")).ReturnsAsync(true);
            userManager.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(false);

            refreshService
                .Setup(x => x.CreateUserRefreshTokenAsync("u1", "DeviceA", "127.0.0.1"))
                .ReturnsAsync(ApiResponse<RefreshTokenResultDto>.Success(
                    new RefreshTokenResultDto { AccessToken = "new-access", RefreshToken = "new-refresh" }));

            ipMock.Setup(x => x.GetClientIpAddress()).Returns("127.0.0.1");

            var service = CreateService(context, userManager, refreshService, ipMock);

            var result = await service.LoginAsync(new LoginRequestModel
            {
                UserName = "john",
                Password = "Pass123!",
                DeviceInfo = "DeviceA"
            });

            Assert.True(result.IsSuccessfull);
            Assert.Equal("u1", result.Data.UserID);
            Assert.False(string.IsNullOrWhiteSpace(result.Data.AccessToken));
            refreshService.Verify(x => x.CreateUserRefreshTokenAsync("u1", "DeviceA", "127.0.0.1"), Times.Once);
        }

        [Fact]
        public async Task LoginAsync_Fails_WhenUserNotFound()
        {
            var context = BuildContext(nameof(LoginAsync_Fails_WhenUserNotFound));
            var userManager = BuildUserManager();
            var refreshService = new Mock<IRefreshTokenService>();
            var ipMock = new Mock<IIpAddressService>();

            userManager.Setup(x => x.FindByNameAsync("ghost")).ReturnsAsync((ApplicationUser)null);

            var service = CreateService(context, userManager, refreshService, ipMock);

            var result = await service.LoginAsync(new LoginRequestModel
            {
                UserName = "ghost",
                Password = "pass"
            });

            Assert.False(result.IsSuccessfull);
            Assert.Equal("There is no user with this username", result.Message);
        }

        [Fact]
        public async Task LoginAsync_Fails_WhenPasswordInvalid()
        {
            var context = BuildContext(nameof(LoginAsync_Fails_WhenPasswordInvalid));
            var userManager = BuildUserManager();
            var refreshService = new Mock<IRefreshTokenService>();
            var ipMock = new Mock<IIpAddressService>();

            var user = new ApplicationUser { Id = "u1", UserName = "john" };
            userManager.Setup(x => x.FindByNameAsync("john")).ReturnsAsync(user);
            userManager.Setup(x => x.CheckPasswordAsync(user, "wrong")).ReturnsAsync(false);

            var service = CreateService(context, userManager, refreshService, ipMock);

            var result = await service.LoginAsync(new LoginRequestModel
            {
                UserName = "john",
                Password = "wrong"
            });

            Assert.False(result.IsSuccessfull);
            Assert.Equal("Invalid username or password", result.Message);
        }

        [Fact]
        public async Task LoginAsync_Fails_WhenLockedOut()
        {
            var context = BuildContext(nameof(LoginAsync_Fails_WhenLockedOut));
            var userManager = BuildUserManager();
            var refreshService = new Mock<IRefreshTokenService>();
            var ipMock = new Mock<IIpAddressService>();

            var user = new ApplicationUser { Id = "u1", UserName = "john" };
            userManager.Setup(x => x.FindByNameAsync("john")).ReturnsAsync(user);
            userManager.Setup(x => x.CheckPasswordAsync(user, "Pass123!")).ReturnsAsync(true);
            userManager.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(true);

            var service = CreateService(context, userManager, refreshService, ipMock);

            var result = await service.LoginAsync(new LoginRequestModel
            {
                UserName = "john",
                Password = "Pass123!"
            });

            Assert.False(result.IsSuccessfull);
            Assert.Equal((int)System.Net.HttpStatusCode.Unauthorized, result.StatusCode);
            Assert.Equal("Account locked", result.Message);
        }

        [Fact]
        public async Task LoginAsync_Fails_WhenRefreshTokenUpdateFails()
        {
            var context = BuildContext(nameof(LoginAsync_Fails_WhenRefreshTokenUpdateFails));
            var userManager = BuildUserManager();
            var refreshService = new Mock<IRefreshTokenService>();
            var ipMock = new Mock<IIpAddressService>();

            var user = new ApplicationUser { Id = "u1", UserName = "john" };
            userManager.Setup(x => x.FindByNameAsync("john")).ReturnsAsync(user);
            userManager.Setup(x => x.CheckPasswordAsync(user, "Pass123!")).ReturnsAsync(true);
            userManager.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(false);

            refreshService
                .Setup(x => x.CreateUserRefreshTokenAsync("u1", "DeviceA", "127.0.0.1"))
                .ReturnsAsync(ApiResponse<RefreshTokenResultDto>.Failed(
                    "fail",
                    null,
                    (int)System.Net.HttpStatusCode.InternalServerError));

            ipMock.Setup(x => x.GetClientIpAddress()).Returns("127.0.0.1");

            var service = CreateService(context, userManager, refreshService, ipMock);

            var result = await service.LoginAsync(new LoginRequestModel
            {
                UserName = "john",
                Password = "Pass123!",
                DeviceInfo = "DeviceA"
            });

            Assert.False(result.IsSuccessfull);
            Assert.Equal((int)System.Net.HttpStatusCode.InternalServerError, result.StatusCode);
            Assert.Equal("Login successful but failed to update refresh token", result.Message);
        }

        [Fact]
        public async Task RefreshTokenAsync_ReturnsNewTokens_WhenValid()
        {
            var context = BuildContext(nameof(RefreshTokenAsync_ReturnsNewTokens_WhenValid));
            var userManager = BuildUserManager();
            var refreshService = new Mock<IRefreshTokenService>();
            var ipMock = new Mock<IIpAddressService>();

            refreshService
                .Setup(x => x.GetValidRefreshTokenAsync("old-token"))
                .ReturnsAsync(new UserRefreshToken
                {
                    UserId = "u1",
                    RefreshToken = "old-token",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddMinutes(5)
                });

            refreshService.Setup(x => x.RevokeRefreshTokenAsync("old-token")).ReturnsAsync(true);
            refreshService
                .Setup(x => x.CreateUserRefreshTokenAsync("u1", "DeviceA", "127.0.0.1"))
                .ReturnsAsync(ApiResponse<RefreshTokenResultDto>.Success(
                    new RefreshTokenResultDto { AccessToken = "new-access", RefreshToken = "new-refresh" }));

            ipMock.Setup(x => x.GetClientIpAddress()).Returns("127.0.0.1");

            var service = CreateService(context, userManager, refreshService, ipMock);

            var result = await service.RefreshTokenAsync(new RefreshTokenDto
            {
                RefreshToken = "old-token",
                DeviceInfo = "DeviceA",
                UserId = "u1"
            });

            Assert.True(result.IsSuccessfull);
            Assert.Equal("new-access", result.Data.AccessToken);
            refreshService.Verify(x => x.RevokeRefreshTokenAsync("old-token"), Times.Once);
            refreshService.Verify(x => x.CreateUserRefreshTokenAsync("u1", "DeviceA", "127.0.0.1"), Times.Once);
        }

        [Fact]
        public async Task RefreshTokenAsync_Fails_WhenTokenInvalid()
        {
            var context = BuildContext(nameof(RefreshTokenAsync_Fails_WhenTokenInvalid));
            var userManager = BuildUserManager();
            var refreshService = new Mock<IRefreshTokenService>();
            var ipMock = new Mock<IIpAddressService>();

            refreshService
                .Setup(x => x.GetValidRefreshTokenAsync("bad"))
                .ReturnsAsync((UserRefreshToken)null);

            var service = CreateService(context, userManager, refreshService, ipMock);

            var result = await service.RefreshTokenAsync(new RefreshTokenDto
            {
                RefreshToken = "bad",
                DeviceInfo = "d",
                UserId = "u1"
            });

            Assert.False(result.IsSuccessfull);
            Assert.Equal((int)System.Net.HttpStatusCode.Unauthorized, result.StatusCode);
            Assert.Equal("Invalid refresh token", result.Message);
        }

        [Fact]
        public async Task RefreshTokenAsync_Fails_OnException()
        {
            var context = BuildContext(nameof(RefreshTokenAsync_Fails_OnException));
            var userManager = BuildUserManager();
            var refreshService = new Mock<IRefreshTokenService>();
            var ipMock = new Mock<IIpAddressService>();

            refreshService
                .Setup(x => x.GetValidRefreshTokenAsync(It.IsAny<string>()))
                .ThrowsAsync(new Exception("boom"));

            var service = CreateService(context, userManager, refreshService, ipMock);

            var result = await service.RefreshTokenAsync(new RefreshTokenDto
            {
                RefreshToken = "t",
                DeviceInfo = "d",
                UserId = "u1"
            });

            Assert.False(result.IsSuccessfull);
            Assert.Equal((int)System.Net.HttpStatusCode.InternalServerError, result.StatusCode);
            Assert.Equal("An error occurred during refresh token", result.Message);
        }

        [Fact]
        public async Task GetMySessionsByUserId_ReturnsActiveSessions()
        {
            var context = BuildContext(nameof(GetMySessionsByUserId_ReturnsActiveSessions));
            context.UserRefreshTokens.AddRange(
                new UserRefreshToken
                {
                    Id = "s1",
                    UserId = "u1",
                    RefreshToken = "r1",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    CreatedByIp = "ip1",
                    DeviceInfo = "d1"
                },
                new UserRefreshToken
                {
                    Id = "s2",
                    UserId = "u1",
                    RefreshToken = "r2",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    CreatedByIp = "ip2",
                    DeviceInfo = "d2"
                });
            await context.SaveChangesAsync();

            var service = CreateService(
                context,
                BuildUserManager(),
                new Mock<IRefreshTokenService>(),
                new Mock<IIpAddressService>());

            var result = await service.GetMySessionsByUserId("u1");

            Assert.True(result.IsSuccessfull);
            Assert.Equal(2, result.Data.Count);
        }

        [Fact]
        public async Task GetMySessionsByUserId_Fails_WhenNoneExist()
        {
            var context = BuildContext(nameof(GetMySessionsByUserId_Fails_WhenNoneExist));
            var service = CreateService(
                context,
                BuildUserManager(),
                new Mock<IRefreshTokenService>(),
                new Mock<IIpAddressService>());

            var result = await service.GetMySessionsByUserId("u1");

            Assert.False(result.IsSuccessfull);
            Assert.Equal((int)System.Net.HttpStatusCode.NotFound, result.StatusCode);
            Assert.Equal("No sessions found for this user", result.Message);
        }

        [Fact]
        public async Task LogoutSessionAsync_RemovesSession_WhenActive()
        {
            var context = BuildContext(nameof(LogoutSessionAsync_RemovesSession_WhenActive));
            context.UserRefreshTokens.Add(new UserRefreshToken
            {
                Id = "s1",
                UserId = "u1",
                RefreshToken = "r1",
                RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = "ip",
                DeviceInfo = "d"
            });
            await context.SaveChangesAsync();

            var service = CreateService(
                context,
                BuildUserManager(),
                new Mock<IRefreshTokenService>(),
                new Mock<IIpAddressService>());

            var result = await service.LogoutSessionAsync("s1", "u1");

            Assert.True(result.IsSuccessfull);
            Assert.Equal((int)System.Net.HttpStatusCode.OK, result.StatusCode);
            Assert.Null(await context.UserRefreshTokens.FindAsync("s1"));
        }

        [Fact]
        public async Task LogoutSessionAsync_Fails_WhenSessionInactive()
        {
            var context = BuildContext(nameof(LogoutSessionAsync_Fails_WhenSessionInactive));
            context.UserRefreshTokens.Add(new UserRefreshToken
            {
                Id = "s1",
                UserId = "u1",
                RefreshToken = "r1",
                RefreshTokenExpiryTime = DateTime.UtcNow.AddMinutes(-1),
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                CreatedByIp = "ip",
                DeviceInfo = "d"
            });
            await context.SaveChangesAsync();

            var service = CreateService(
                context,
                BuildUserManager(),
                new Mock<IRefreshTokenService>(),
                new Mock<IIpAddressService>());

            var result = await service.LogoutSessionAsync("s1", "u1");

            Assert.False(result.IsSuccessfull);
            Assert.Equal((int)System.Net.HttpStatusCode.BadRequest, result.StatusCode);
            Assert.Equal("Session is already inactive", result.Message);
        }

        [Fact]
        public async Task LogoutSessionAsync_Fails_WhenNotFound()
        {
            var context = BuildContext(nameof(LogoutSessionAsync_Fails_WhenNotFound));
            var service = CreateService(
                context,
                BuildUserManager(),
                new Mock<IRefreshTokenService>(),
                new Mock<IIpAddressService>());

            var result = await service.LogoutSessionAsync("missing", "u1");

            Assert.False(result.IsSuccessfull);
            Assert.Equal((int)System.Net.HttpStatusCode.NotFound, result.StatusCode);
            Assert.Equal("Session not found", result.Message);
        }

        [Fact]
        public async Task LogoutSessionAsync_Fails_WhenSessionBelongsToAnotherUser()
        {
            var context = BuildContext(nameof(LogoutSessionAsync_Fails_WhenSessionBelongsToAnotherUser));
            context.UserRefreshTokens.Add(new UserRefreshToken
            {
                Id = "s1",
                UserId = "u1",
                RefreshToken = "r1",
                RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = "ip",
                DeviceInfo = "d"
            });
            await context.SaveChangesAsync();

            var service = CreateService(
                context,
                BuildUserManager(),
                new Mock<IRefreshTokenService>(),
                new Mock<IIpAddressService>());

            var result = await service.LogoutSessionAsync("s1", "someone-else");

            Assert.False(result.IsSuccessfull);
            Assert.Equal((int)System.Net.HttpStatusCode.NotFound, result.StatusCode);
            Assert.NotNull(await context.UserRefreshTokens.FindAsync("s1"));
        }
    }
}