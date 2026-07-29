using System.Reflection;
using System.Security.Claims;
using IdentityService.Controllers;
using IdentityService.DTOs;
using IdentityService.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace IdentityServiceTests.UnitTests.Controllers;

public class AdminUsersControllerTests
{
    private const string ActorUserId = "super-admin-1";

    private readonly Mock<IAdminUserService> _adminUserService = new();
    private readonly AdminUsersController _controller;

    public AdminUsersControllerTests()
    {
        _controller = new AdminUsersController(_adminUserService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, ActorUserId)],
                            "TestAuth"))
                }
            }
        };
    }

    [Fact]
    public void Controller_RequiresSuperAdminRole()
    {
        var attribute = typeof(AdminUsersController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("SUPER_ADMIN", attribute.Roles);
    }

    [Fact]
    public async Task GetUsers_ReturnsPagedResult()
    {
        var page = new AdminUserPageDto
        {
            Items =
            [
                new AdminUserListItemDto
                {
                    Id = "user-1",
                    UserName = "alice",
                    Email = "alice@example.com"
                }
            ],
            Page = 2,
            Limit = 10,
            TotalCount = 11,
        };
        _adminUserService
            .Setup(service => service.GetUsersAsync("ali", 2, 10))
            .ReturnsAsync(page);

        var result = await _controller.GetUsers("ali", 2, 10);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(page, ok.Value);
    }

    [Fact]
    public async Task DeleteUser_Success_ReturnsNoContentAndPassesActorId()
    {
        _adminUserService
            .Setup(service => service.DeleteUserAsync(ActorUserId, "user-1"))
            .ReturnsAsync(
                ApiResponse<object>.Success(
                    "User deleted successfully.",
                    StatusCodes.Status204NoContent));

        var result = await _controller.DeleteUser("user-1");

        Assert.IsType<NoContentResult>(result);
        _adminUserService.Verify(
            service => service.DeleteUserAsync(ActorUserId, "user-1"),
            Times.Once);
    }

    [Fact]
    public async Task DeleteUser_MissingUser_ReturnsNotFoundResponse()
    {
        var response = ApiResponse<object>.NotFound("User not found.");
        _adminUserService
            .Setup(service => service.DeleteUserAsync(ActorUserId, "missing"))
            .ReturnsAsync(response);

        var result = await _controller.DeleteUser("missing");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        Assert.Same(response, objectResult.Value);
    }
}
