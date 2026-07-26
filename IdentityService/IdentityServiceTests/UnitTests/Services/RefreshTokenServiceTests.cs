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
    public class RefreshTokenServiceTests
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

        private static Mock<ITokenVersionStore> BuildTokenVersionStore()
        {
            var store = new Mock<ITokenVersionStore>();
            store.Setup(x => x.SetAtLeastAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(true);
            store.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync((string _, int databaseVersion) => databaseVersion + 1);
            return store;
        }

        private RefreshTokenService CreateService(
            IdentityDbContext context,
            Mock<ITokenVersionStore> tokenVersionStore = null
        )
        {
            var logger = new Mock<ILogger<RefreshTokenService>>();
            var userManager = BuildUserManager();
            return new RefreshTokenService(
                BuildConfig(),
                context,
                userManager.Object,
                logger.Object,
                (tokenVersionStore ?? BuildTokenVersionStore()).Object
            );
        }

        [Fact]
        public async Task CreateUserRefreshTokenAsync_ReturnsTokens_WhenUserExists()
        {
            var context = BuildContext(nameof(CreateUserRefreshTokenAsync_ReturnsTokens_WhenUserExists));
            var user = new ApplicationUser { Id = "u1", UserName = "john" };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var userManager = BuildUserManager();
            userManager.Setup(x => x.FindByIdAsync("u1")).ReturnsAsync(user);

            var logger = new Mock<ILogger<RefreshTokenService>>();
            var service = new RefreshTokenService(
                BuildConfig(), context, userManager.Object, logger.Object,
                BuildTokenVersionStore().Object
            );

            var result = await service.CreateUserRefreshTokenAsync("u1", "DeviceA", "127.0.0.1");

            Assert.True(result.IsSuccessfull);
            Assert.NotNull(result.Data.AccessToken);
            Assert.NotNull(result.Data.RefreshToken);
            Assert.Single(context.UserRefreshTokens);
            Assert.Equal("u1", context.UserRefreshTokens.First().UserId);
        }

        [Fact]
        public async Task CreateUserRefreshTokenAsync_Returns503_WhenTokenVersionStoreUnavailable()
        {
            var context = BuildContext(
                nameof(CreateUserRefreshTokenAsync_Returns503_WhenTokenVersionStoreUnavailable)
            );
            var user = new ApplicationUser { Id = "u1", UserName = "john" };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var userManager = BuildUserManager();
            userManager.Setup(x => x.FindByIdAsync("u1")).ReturnsAsync(user);
            var tokenVersionStore = BuildTokenVersionStore();
            tokenVersionStore.Setup(x => x.SetAtLeastAsync("u1", 0)).ReturnsAsync(false);
            var logger = new Mock<ILogger<RefreshTokenService>>();
            var service = new RefreshTokenService(
                BuildConfig(), context, userManager.Object, logger.Object,
                tokenVersionStore.Object
            );

            var result = await service.CreateUserRefreshTokenAsync(
                "u1", "DeviceA", "127.0.0.1"
            );

            Assert.False(result.IsSuccessfull);
            Assert.Equal((int)System.Net.HttpStatusCode.ServiceUnavailable, result.StatusCode);
            Assert.Empty(context.UserRefreshTokens);
        }

        [Fact]
        public async Task CreateUserRefreshTokenAsync_Fails_WhenUserNotFound()
        {
            var context = BuildContext(nameof(CreateUserRefreshTokenAsync_Fails_WhenUserNotFound));
            var userManager = BuildUserManager();
            userManager.Setup(x => x.FindByIdAsync("u1")).ReturnsAsync((ApplicationUser)null);

            var logger = new Mock<ILogger<RefreshTokenService>>();
            var service = new RefreshTokenService(
                BuildConfig(), context, userManager.Object, logger.Object,
                BuildTokenVersionStore().Object
            );

            var result = await service.CreateUserRefreshTokenAsync("u1", "DeviceA", "127.0.0.1");

            Assert.False(result.IsSuccessfull);
            Assert.Equal((int)System.Net.HttpStatusCode.NotFound, result.StatusCode);
            Assert.Equal("Acccess token creation failed - User does not exist", result.Message);
        }

        [Fact]
        public async Task CreateUserRefreshTokenAsync_RemovesExpiredTokens()
        {
            var context = BuildContext(nameof(CreateUserRefreshTokenAsync_RemovesExpiredTokens));
            var user = new ApplicationUser { Id = "u1", UserName = "john" };
            context.Users.Add(user);

            // Add expired token
            context.UserRefreshTokens.Add(new UserRefreshToken
            {
                Id = "expired1",
                UserId = "u1",
                RefreshToken = "expired-token",
                RefreshTokenExpiryTime = DateTime.UtcNow.AddMinutes(-1),
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                CreatedByIp = "ip",
                DeviceInfo = "old"
            });
            await context.SaveChangesAsync();

            var userManager = BuildUserManager();
            userManager.Setup(x => x.FindByIdAsync("u1")).ReturnsAsync(user);

            var logger = new Mock<ILogger<RefreshTokenService>>();
            var service = new RefreshTokenService(
                BuildConfig(), context, userManager.Object, logger.Object,
                BuildTokenVersionStore().Object
            );

            var result = await service.CreateUserRefreshTokenAsync("u1", "DeviceB", "127.0.0.1");

            Assert.True(result.IsSuccessfull);
            Assert.Null(await context.UserRefreshTokens.FindAsync("expired1"));
            Assert.Single(context.UserRefreshTokens);
        }

        [Fact]
        public async Task CreateUserRefreshTokenAsync_RemovesOldestToken_WhenExceeds5Active()
        {
            var context = BuildContext(nameof(CreateUserRefreshTokenAsync_RemovesOldestToken_WhenExceeds5Active));
            var user = new ApplicationUser { Id = "u1", UserName = "john" };
            context.Users.Add(user);

            // Add 5 active tokens
            for (int i = 1; i <= 5; i++)
            {
                context.UserRefreshTokens.Add(new UserRefreshToken
                {
                    Id = $"s{i}",
                    UserId = "u1",
                    RefreshToken = $"token{i}",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow.AddHours(-i),
                    CreatedByIp = "ip",
                    DeviceInfo = $"Device{i}"
                });
            }
            await context.SaveChangesAsync();

            var userManager = BuildUserManager();
            userManager.Setup(x => x.FindByIdAsync("u1")).ReturnsAsync(user);

            var logger = new Mock<ILogger<RefreshTokenService>>();
            var service = new RefreshTokenService(
                BuildConfig(), context, userManager.Object, logger.Object,
                BuildTokenVersionStore().Object
            );

            // Creating 6th token should remove the oldest (s5)
            var result = await service.CreateUserRefreshTokenAsync("u1", "Device6", "127.0.0.1");

            Assert.True(result.IsSuccessfull);
            Assert.Null(await context.UserRefreshTokens.FindAsync("s1"));
            Assert.Equal(5, context.UserRefreshTokens.Count());
        }

        [Fact]
        public async Task CreateUserRefreshTokenAsync_ReplacesSameDevice_When5Tokens()
        {
            var context = BuildContext(nameof(CreateUserRefreshTokenAsync_ReplacesSameDevice_When5Tokens));
            var user = new ApplicationUser { Id = "u1", UserName = "john" };
            context.Users.Add(user);

            // Add 5 active tokens with one matching device
            for (int i = 1; i <= 5; i++)
            {
                context.UserRefreshTokens.Add(new UserRefreshToken
                {
                    Id = $"s{i}",
                    UserId = "u1",
                    RefreshToken = $"token{i}",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow.AddHours(-i),
                    CreatedByIp = "ip",
                    DeviceInfo = i == 3 ? "SameDevice" : $"Device{i}"
                });
            }
            await context.SaveChangesAsync();

            var userManager = BuildUserManager();
            userManager.Setup(x => x.FindByIdAsync("u1")).ReturnsAsync(user);

            var logger = new Mock<ILogger<RefreshTokenService>>();
            var service = new RefreshTokenService(
                BuildConfig(), context, userManager.Object, logger.Object,
                BuildTokenVersionStore().Object
            );

            // Creating new token for SameDevice should remove the old one for that device
            var result = await service.CreateUserRefreshTokenAsync("u1", "SameDevice", "127.0.0.1");

            Assert.True(result.IsSuccessfull);
            Assert.Null(await context.UserRefreshTokens.FindAsync("s3"));
            Assert.Equal(5, context.UserRefreshTokens.Count());
        }

        [Fact]
        public async Task GenerateRefreshTokenAsync_ReturnsValidBase64Token()
        {
            var context = BuildContext(nameof(GenerateRefreshTokenAsync_ReturnsValidBase64Token));
            var service = CreateService(context);

            var token = await service.GenerateRefreshTokenAsync();

            Assert.NotNull(token);
            Assert.NotEmpty(token);
            // Should be valid base64
            var bytes = Convert.FromBase64String(token);
            Assert.NotEmpty(bytes);
        }

        [Fact]
        public async Task GetValidRefreshTokenAsync_ReturnsToken_WhenValid()
        {
            var context = BuildContext(nameof(GetValidRefreshTokenAsync_ReturnsToken_WhenValid));
            var user = new ApplicationUser { Id = "u1", UserName = "john" };
            context.Users.Add(user);
            context.UserRefreshTokens.Add(new UserRefreshToken
            {
                Id = "s1",
                UserId = "u1",
                RefreshToken = "valid-token",
                RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = "ip",
                DeviceInfo = "device",
                User = user
            });
            await context.SaveChangesAsync();

            var service = CreateService(context);

            var result = await service.GetValidRefreshTokenAsync("valid-token");

            Assert.NotNull(result);
            Assert.Equal("u1", result.UserId);
            Assert.Equal("valid-token", result.RefreshToken);
        }

        [Fact]
        public async Task GetValidRefreshTokenAsync_ReturnsNull_WhenExpired()
        {
            var context = BuildContext(nameof(GetValidRefreshTokenAsync_ReturnsNull_WhenExpired));
            context.UserRefreshTokens.Add(new UserRefreshToken
            {
                Id = "s1",
                UserId = "u1",
                RefreshToken = "expired-token",
                RefreshTokenExpiryTime = DateTime.UtcNow.AddMinutes(-1),
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = "ip",
                DeviceInfo = "device"
            });
            await context.SaveChangesAsync();

            var service = CreateService(context);

            var result = await service.GetValidRefreshTokenAsync("expired-token");

            Assert.Null(result);
        }

        [Fact]
        public async Task GetValidRefreshTokenAsync_ReturnsNull_WhenNotFound()
        {
            var context = BuildContext(nameof(GetValidRefreshTokenAsync_ReturnsNull_WhenNotFound));
            var service = CreateService(context);

            var result = await service.GetValidRefreshTokenAsync("missing");

            Assert.Null(result);
        }

        [Fact]
        public async Task RevokeRefreshTokenAsync_RemovesToken()
        {
            var context = BuildContext(nameof(RevokeRefreshTokenAsync_RemovesToken));
            context.UserRefreshTokens.Add(new UserRefreshToken
            {
                Id = "s1",
                UserId = "u1",
                RefreshToken = "token-to-revoke",
                RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = "ip",
                DeviceInfo = "device"
            });
            await context.SaveChangesAsync();

            var service = CreateService(context);

            var result = await service.RevokeRefreshTokenAsync("token-to-revoke");

            Assert.True(result);
            Assert.Null(await context.UserRefreshTokens.FindAsync("s1"));
        }

        [Fact]
        public async Task RevokeRefreshTokenAsync_ReturnsFalse_WhenNotFound()
        {
            var context = BuildContext(nameof(RevokeRefreshTokenAsync_ReturnsFalse_WhenNotFound));
            var service = CreateService(context);

            var result = await service.RevokeRefreshTokenAsync("missing");

            Assert.False(result);
        }

        [Fact]
        public async Task RevokeAllUserTokensAsync_RemovesAllUserTokens()
        {
            var context = BuildContext(nameof(RevokeAllUserTokensAsync_RemovesAllUserTokens));
            context.UserRefreshTokens.AddRange(
                new UserRefreshToken
                {
                    Id = "s1",
                    UserId = "u1",
                    RefreshToken = "token1",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByIp = "ip",
                    DeviceInfo = "d1"
                },
                new UserRefreshToken
                {
                    Id = "s2",
                    UserId = "u1",
                    RefreshToken = "token2",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByIp = "ip",
                    DeviceInfo = "d2"
                },
                new UserRefreshToken
                {
                    Id = "s3",
                    UserId = "u2",
                    RefreshToken = "token3",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByIp = "ip",
                    DeviceInfo = "d3"
                });
            await context.SaveChangesAsync();

            var service = CreateService(context);

            var result = await service.RevokeAllUserTokensAsync("u1");

            Assert.True(result);
            Assert.Null(await context.UserRefreshTokens.FindAsync("s1"));
            Assert.Null(await context.UserRefreshTokens.FindAsync("s2"));
            Assert.NotNull(await context.UserRefreshTokens.FindAsync("s3"));
            Assert.Single(context.UserRefreshTokens);
        }

        [Fact]
        public async Task InvalidateAllUserSessionsAsync_IncrementsVersionAndRemovesTokens()
        {
            var context = BuildContext(
                nameof(InvalidateAllUserSessionsAsync_IncrementsVersionAndRemovesTokens)
            );
            context.Users.AddRange(
                new ApplicationUser { Id = "u1", UserName = "john", TokenVersion = 3 },
                new ApplicationUser { Id = "u2", UserName = "jane", TokenVersion = 1 }
            );
            context.UserRefreshTokens.AddRange(
                new UserRefreshToken
                {
                    Id = "s1",
                    UserId = "u1",
                    RefreshToken = "token1",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByIp = "ip",
                    DeviceInfo = "d1"
                },
                new UserRefreshToken
                {
                    Id = "s2",
                    UserId = "u2",
                    RefreshToken = "token2",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByIp = "ip",
                    DeviceInfo = "d2"
                }
            );
            await context.SaveChangesAsync();
            var tokenVersionStore = BuildTokenVersionStore();
            var service = CreateService(context, tokenVersionStore);

            var result = await service.InvalidateAllUserSessionsAsync("u1");

            Assert.True(result);
            Assert.Equal(4, (await context.Users.FindAsync("u1"))!.TokenVersion);
            Assert.DoesNotContain(context.UserRefreshTokens, token => token.UserId == "u1");
            Assert.Contains(context.UserRefreshTokens, token => token.UserId == "u2");
            tokenVersionStore.Verify(x => x.IncrementAsync("u1", 3), Times.Once);
        }

        [Fact]
        public async Task InvalidateAllUserSessionsAsync_DoesNotChangeDatabase_WhenRedisUnavailable()
        {
            var context = BuildContext(
                nameof(InvalidateAllUserSessionsAsync_DoesNotChangeDatabase_WhenRedisUnavailable)
            );
            context.Users.Add(
                new ApplicationUser { Id = "u1", UserName = "john", TokenVersion = 3 }
            );
            context.UserRefreshTokens.Add(
                new UserRefreshToken
                {
                    Id = "s1",
                    UserId = "u1",
                    RefreshToken = "token1",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByIp = "ip",
                    DeviceInfo = "d1"
                }
            );
            await context.SaveChangesAsync();

            var tokenVersionStore = BuildTokenVersionStore();
            tokenVersionStore.Setup(x => x.IncrementAsync("u1", 3))
                .ReturnsAsync((int?)null);
            var service = CreateService(context, tokenVersionStore);

            var result = await service.InvalidateAllUserSessionsAsync("u1");

            Assert.False(result);
            Assert.Equal(3, (await context.Users.FindAsync("u1"))!.TokenVersion);
            Assert.Single(context.UserRefreshTokens);
        }

        [Fact]
        public async Task CleanupExpiredTokensAsync_RemovesOnlyExpiredTokens()
        {
            var context = BuildContext(nameof(CleanupExpiredTokensAsync_RemovesOnlyExpiredTokens));
            context.UserRefreshTokens.AddRange(
                new UserRefreshToken
                {
                    Id = "expired1",
                    UserId = "u1",
                    RefreshToken = "exp-token1",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddMinutes(-1),
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    CreatedByIp = "ip",
                    DeviceInfo = "d1"
                },
                new UserRefreshToken
                {
                    Id = "expired2",
                    UserId = "u2",
                    RefreshToken = "exp-token2",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddMinutes(-30),
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    CreatedByIp = "ip",
                    DeviceInfo = "d2"
                },
                new UserRefreshToken
                {
                    Id = "active1",
                    UserId = "u1",
                    RefreshToken = "active-token",
                    RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByIp = "ip",
                    DeviceInfo = "d3"
                });
            await context.SaveChangesAsync();

            var service = CreateService(context);

            await service.CleanupExpiredTokensAsync();

            Assert.Null(await context.UserRefreshTokens.FindAsync("expired1"));
            Assert.Null(await context.UserRefreshTokens.FindAsync("expired2"));
            Assert.NotNull(await context.UserRefreshTokens.FindAsync("active1"));
            Assert.Single(context.UserRefreshTokens);
        }

        [Fact]
        public async Task CleanupExpiredTokensAsync_DoesNothing_WhenNoExpiredTokens()
        {
            var context = BuildContext(nameof(CleanupExpiredTokensAsync_DoesNothing_WhenNoExpiredTokens));
            context.UserRefreshTokens.Add(new UserRefreshToken
            {
                Id = "active1",
                UserId = "u1",
                RefreshToken = "active-token",
                RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = "ip",
                DeviceInfo = "d1"
            });
            await context.SaveChangesAsync();

            var service = CreateService(context);

            await service.CleanupExpiredTokensAsync();

            Assert.Single(context.UserRefreshTokens);
        }

        [Fact]
        public async Task CreateUserRefreshTokenAsync_Fails_OnException()
        {
            var context = BuildContext(nameof(CreateUserRefreshTokenAsync_Fails_OnException));
            var userManager = BuildUserManager();
            userManager.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
                .ThrowsAsync(new Exception("boom"));

            var logger = new Mock<ILogger<RefreshTokenService>>();
            var service = new RefreshTokenService(
                BuildConfig(), context, userManager.Object, logger.Object,
                BuildTokenVersionStore().Object
            );

            var result = await service.CreateUserRefreshTokenAsync("u1", "DeviceA", "127.0.0.1");

            Assert.False(result.IsSuccessfull);
            Assert.Equal((int)System.Net.HttpStatusCode.InternalServerError, result.StatusCode);
            Assert.Equal("An error occurred while creating refresh token", result.Message);
        }
    }
}
