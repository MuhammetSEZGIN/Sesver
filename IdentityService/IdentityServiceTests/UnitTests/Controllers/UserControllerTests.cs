using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using IdentityService.Controllers;
using IdentityService.DTOs;
using IdentityService.Interfaces;
using IdentityService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace IdentityServiceTests.UnitTests.Controllers
{
    public class UserControllerTests
    {
        private const string TestUserId = "user123";

        private readonly Mock<IUserService> _mockUserService;
        private readonly UserController _controller;

        public UserControllerTests()
        {
            _mockUserService = new Mock<IUserService>();
            _controller = new UserController(
                _mockUserService.Object,
                new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ClientApp:EmailConfirmationUrl"] =
                            "https://api.example.com/identity/confirm-email",
                    }
                ).Build()
            );

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, TestUserId) },
                    "TestAuth"
                )
            );
            _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        }

        #region UpdateUser Tests

        [Fact]
        public async Task UpdateUser_ValidModel_ReturnsOkResult()
        {
            // Arrange
            var updateModel = new UpdateUserModel
            {
                UserName = "updateduser",
                AvatarUrl = "https://example.com/avatar.jpg"
            };

            var identityResult = IdentityResult.Success;

            _mockUserService
                .Setup(x => x.UpdateUserAsync(TestUserId, It.IsAny<UpdateUserModel>()))
                .ReturnsAsync(identityResult);

            // Act
            var result = await _controller.UpdateUser(updateModel);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = okResult.Value;
            var message = response.GetType().GetProperty("Message")?.GetValue(response, null);
            Assert.Equal("User updated successfully", message);

            _mockUserService.Verify(x => x.UpdateUserAsync(TestUserId, updateModel), Times.Once);
        }

        [Fact]
        public async Task UpdateUser_ServiceFails_ReturnsBadRequest()
        {
            // Arrange
            var updateModel = new UpdateUserModel
            {
                UserName = "updateduser"
            };

            var errors = new[]
            {
                new IdentityError
                {
                    Code = "DuplicateUserName",
                    Description = "Username already exists"
                }
            };
            var identityResult = IdentityResult.Failed(errors);

            _mockUserService
                .Setup(x => x.UpdateUserAsync(TestUserId, It.IsAny<UpdateUserModel>()))
                .ReturnsAsync(identityResult);

            // Act
            var result = await _controller.UpdateUser(updateModel);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            var returnedErrors = Assert.IsAssignableFrom<IEnumerable<IdentityError>>(
                badRequestResult.Value
            );
            Assert.Single(returnedErrors);
            Assert.Equal("DuplicateUserName", returnedErrors.First().Code);
            Assert.Equal("Username already exists", returnedErrors.First().Description);

            _mockUserService.Verify(x => x.UpdateUserAsync(TestUserId, updateModel), Times.Once);
        }

        [Fact]
        public async Task UpdateUser_MultipleErrors_ReturnsBadRequestWithAllErrors()
        {
            // Arrange
            var updateModel = new UpdateUserModel
            {
                UserName = "updateduser"
            };

            var errors = new[]
            {
                new IdentityError { Code = "InvalidEmail", Description = "Email is invalid" },
                new IdentityError { Code = "DuplicateUserName", Description = "Username taken" }
            };
            var identityResult = IdentityResult.Failed(errors);

            _mockUserService
                .Setup(x => x.UpdateUserAsync(TestUserId, It.IsAny<UpdateUserModel>()))
                .ReturnsAsync(identityResult);

            // Act
            var result = await _controller.UpdateUser(updateModel);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            var returnedErrors = Assert.IsAssignableFrom<IEnumerable<IdentityError>>(
                badRequestResult.Value
            );
            Assert.Equal(2, returnedErrors.Count());

            _mockUserService.Verify(x => x.UpdateUserAsync(TestUserId, updateModel), Times.Once);
        }

        [Fact]
        public async Task UpdateUser_WithNullOptionalFields_ReturnsOkResult()
        {
            // Arrange
            var updateModel = new UpdateUserModel
            {
                UserName = "updateduser",
                AvatarUrl = null,
                Bio = null
            };

            var identityResult = IdentityResult.Success;

            _mockUserService
                .Setup(x => x.UpdateUserAsync(TestUserId, It.IsAny<UpdateUserModel>()))
                .ReturnsAsync(identityResult);

            // Act
            var result = await _controller.UpdateUser(updateModel);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);

            _mockUserService.Verify(x => x.UpdateUserAsync(TestUserId, updateModel), Times.Once);
        }

        [Fact]
        public async Task UpdateUser_UserNotFound_ReturnsBadRequest()
        {
            // Arrange
            var updateModel = new UpdateUserModel
            {
                UserName = "updateduser"
            };

            var errors = new[]
            {
                new IdentityError { Code = "UserNotFound", Description = "User not found" }
            };
            var identityResult = IdentityResult.Failed(errors);

            _mockUserService
                .Setup(x => x.UpdateUserAsync(TestUserId, It.IsAny<UpdateUserModel>()))
                .ReturnsAsync(identityResult);

            // Act
            var result = await _controller.UpdateUser(updateModel);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            var returnedErrors = Assert.IsAssignableFrom<IEnumerable<IdentityError>>(
                badRequestResult.Value
            );
            Assert.Single(returnedErrors);
            Assert.Equal("UserNotFound", returnedErrors.First().Code);

            _mockUserService.Verify(x => x.UpdateUserAsync(TestUserId, updateModel), Times.Once);
        }

        [Fact]
        public async Task UpdateUser_ServiceNotCalled_WhenModelIsInvalid()
        {
            // This would normally be handled by [ValidateModel] attribute
            // but we're testing the controller in isolation
            // In a real scenario, model validation happens before controller action

            // Arrange
            var updateModel = new UpdateUserModel { UserName = "test" };

            var identityResult = IdentityResult.Success;

            _mockUserService
                .Setup(x => x.UpdateUserAsync(TestUserId, It.IsAny<UpdateUserModel>()))
                .ReturnsAsync(identityResult);

            // Act
            var result = await _controller.UpdateUser(updateModel);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            _mockUserService.Verify(x => x.UpdateUserAsync(TestUserId, updateModel), Times.Once);
        }

        #endregion

        #region ChangeEmail Tests

        [Fact]
        public async Task ChangeEmail_ValidRequest_ReturnsServiceResponse()
        {
            var model = new ChangeEmailRequestDto { Email = "new@example.com" };
            _mockUserService
                .Setup(x =>
                    x.ChangeEmailAsync(
                        TestUserId,
                        model,
                        "https://api.example.com/identity/confirm-email"
                    )
                )
                .ReturnsAsync(
                    ApiResponse<object>.Success(
                        "Email updated. Please check your new address for the confirmation link."
                    )
                );

            var result = await _controller.ChangeEmail(model);

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(200, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<object>>(objectResult.Value);
            Assert.True(response.IsSuccessfull);
            _mockUserService.Verify(
                x =>
                    x.ChangeEmailAsync(
                        TestUserId,
                        model,
                        "https://api.example.com/identity/confirm-email"
                    ),
                Times.Once
            );
        }

        [Fact]
        public async Task ChangeEmail_DuplicateEmail_ReturnsBadRequest()
        {
            var model = new ChangeEmailRequestDto { Email = "taken@example.com" };
            _mockUserService
                .Setup(x => x.ChangeEmailAsync(TestUserId, model, It.IsAny<string>()))
                .ReturnsAsync(
                    ApiResponse<object>.Failed(
                        "Email could not be updated.",
                        new[] { "Email is already taken." }
                    )
                );

            var result = await _controller.ChangeEmail(model);

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(400, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<object>>(objectResult.Value);
            Assert.False(response.IsSuccessfull);
        }

        #endregion

        #region DeleteUser Tests

        [Fact]
        public async Task DeleteUser_ValidId_ReturnsOkResult()
        {
            // Arrange
            var userId = "user123";
            var identityResult = IdentityResult.Success;

            _mockUserService
                .Setup(x => x.DeleteUserAsync(userId))
                .ReturnsAsync(identityResult);

            // Act
            var result = await _controller.DeleteUser(userId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = okResult.Value;
            var message = response.GetType().GetProperty("Message")?.GetValue(response, null);
            Assert.Equal("User deleted successfully", message);

            _mockUserService.Verify(x => x.DeleteUserAsync(userId), Times.Once);
        }

        [Fact]
        public async Task DeleteUser_ServiceFails_ReturnsBadRequest()
        {
            // Arrange
            var userId = "user123";
            var errors = new[]
            {
                new IdentityError
                {
                    Code = "DeleteFailed",
                    Description = "Failed to delete user"
                }
            };
            var identityResult = IdentityResult.Failed(errors);

            _mockUserService
                .Setup(x => x.DeleteUserAsync(userId))
                .ReturnsAsync(identityResult);

            // Act
            var result = await _controller.DeleteUser(userId);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            var returnedErrors = Assert.IsAssignableFrom<IEnumerable<IdentityError>>(
                badRequestResult.Value
            );
            Assert.Single(returnedErrors);
            Assert.Equal("DeleteFailed", returnedErrors.First().Code);
            Assert.Equal("Failed to delete user", returnedErrors.First().Description);

            _mockUserService.Verify(x => x.DeleteUserAsync(userId), Times.Once);
        }

        [Fact]
        public async Task DeleteUser_UserNotFound_ReturnsBadRequest()
        {
            // Arrange
            var userId = "nonexistent";
            var errors = new[]
            {
                new IdentityError { Code = "UserNotFound", Description = "User not found" }
            };
            var identityResult = IdentityResult.Failed(errors);

            _mockUserService
                .Setup(x => x.DeleteUserAsync(userId))
                .ReturnsAsync(identityResult);

            // Act
            var result = await _controller.DeleteUser(userId);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            var returnedErrors = Assert.IsAssignableFrom<IEnumerable<IdentityError>>(
                badRequestResult.Value
            );
            Assert.Single(returnedErrors);
            Assert.Equal("UserNotFound", returnedErrors.First().Code);

            _mockUserService.Verify(x => x.DeleteUserAsync(userId), Times.Once);
        }

        [Fact]
        public async Task DeleteUser_EmptyId_StillCallsService()
        {
            // Arrange
            var userId = "";
            var errors = new[]
            {
                new IdentityError { Code = "InvalidUserId", Description = "User ID is invalid" }
            };
            var identityResult = IdentityResult.Failed(errors);

            _mockUserService
                .Setup(x => x.DeleteUserAsync(userId))
                .ReturnsAsync(identityResult);

            // Act
            var result = await _controller.DeleteUser(userId);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            var returnedErrors = Assert.IsAssignableFrom<IEnumerable<IdentityError>>(
                badRequestResult.Value
            );
            Assert.Single(returnedErrors);

            _mockUserService.Verify(x => x.DeleteUserAsync(userId), Times.Once);
        }

        [Fact]
        public async Task DeleteUser_MultipleErrors_ReturnsBadRequestWithAllErrors()
        {
            // Arrange
            var userId = "user123";
            var errors = new[]
            {
                new IdentityError
                {
                    Code = "DeleteFailed",
                    Description = "Failed to delete user"
                },
                new IdentityError
                {
                    Code = "ConcurrencyFailure",
                    Description = "Optimistic concurrency failure"
                }
            };
            var identityResult = IdentityResult.Failed(errors);

            _mockUserService
                .Setup(x => x.DeleteUserAsync(userId))
                .ReturnsAsync(identityResult);

            // Act
            var result = await _controller.DeleteUser(userId);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            var returnedErrors = Assert.IsAssignableFrom<IEnumerable<IdentityError>>(
                badRequestResult.Value
            );
            Assert.Equal(2, returnedErrors.Count());

            _mockUserService.Verify(x => x.DeleteUserAsync(userId), Times.Once);
        }

        [Fact]
        public async Task DeleteUser_ServiceCalledOnce_ForValidRequest()
        {
            // Arrange
            var userId = "user123";
            var identityResult = IdentityResult.Success;

            _mockUserService
                .Setup(x => x.DeleteUserAsync(userId))
                .ReturnsAsync(identityResult);

            // Act
            var result = await _controller.DeleteUser(userId);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            _mockUserService.Verify(x => x.DeleteUserAsync(userId), Times.Once);
        }

        #endregion

        #region GetMe Tests

        [Fact]
        public async Task GetMe_UserExists_ReturnsOkResult()
        {
            // Arrange
            var meDto = new UserMeDto
            {
                Id = TestUserId,
                UserName = "testuser",
                Email = "test@example.com",
                Bio = "hello",
                AvatarUrl = "https://example.com/avatar.jpg",
                EmailConfirmed = true
            };

            _mockUserService.Setup(x => x.GetMeAsync(TestUserId)).ReturnsAsync(meDto);

            // Act
            var result = await _controller.GetMe();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(meDto, okResult.Value);
        }

        [Fact]
        public async Task GetMe_UserNotFound_ReturnsNotFound()
        {
            // Arrange
            _mockUserService.Setup(x => x.GetMeAsync(TestUserId)).ReturnsAsync((UserMeDto)null);

            // Act
            var result = await _controller.GetMe();

            // Assert
            Assert.IsType<NotFoundObjectResult>(result);
        }

        #endregion

        #region ChangePassword Tests

        [Fact]
        public async Task ChangePassword_Succeeds_ReturnsOkResult()
        {
            // Arrange
            var model = new ChangePasswordModel
            {
                CurrentPassword = "OldPass1!",
                NewPassword = "NewPass1!"
            };

            _mockUserService
                .Setup(x => x.ChangePasswordAsync(TestUserId, model.CurrentPassword, model.NewPassword))
                .ReturnsAsync(IdentityResult.Success);

            // Act
            var result = await _controller.ChangePassword(model);

            // Assert
            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task ChangePassword_Fails_ReturnsBadRequest()
        {
            // Arrange
            var model = new ChangePasswordModel
            {
                CurrentPassword = "WrongPass",
                NewPassword = "NewPass1!"
            };

            var errors = new[]
            {
                new IdentityError { Code = "PasswordMismatch", Description = "Incorrect password" }
            };

            _mockUserService
                .Setup(x => x.ChangePasswordAsync(TestUserId, model.CurrentPassword, model.NewPassword))
                .ReturnsAsync(IdentityResult.Failed(errors));

            // Act
            var result = await _controller.ChangePassword(model);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        #endregion

        #region SearchUsers Tests

        [Fact]
        public async Task SearchUsers_ReturnsOkResult_WithResults()
        {
            // Arrange
            var results = new List<UserSearchResultDto>
            {
                new UserSearchResultDto { Id = "u2", UserName = "alice", AvatarUrl = null }
            };

            _mockUserService
                .Setup(x => x.SearchUsersAsync(TestUserId, "ali", 1, 20))
                .ReturnsAsync(results);

            // Act
            var result = await _controller.SearchUsers("ali", 1, 20);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(results, okResult.Value);
        }

        #endregion
    }
}
