using System.Reflection;
using AutoMapper;
using ClanService.Controllers;
using ClanService.DTOs;
using ClanService.Interfaces;
using ClanService.Mapping;
using ClanService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace ClanServiceTest.UnitTests.Controllers;

public class AdminClanControllerTest
{
    private readonly Mock<IClanService> _clanServiceMock;
    private readonly AdminClanController _controller;

    public AdminClanControllerTest()
    {
        _clanServiceMock = new Mock<IClanService>();
        var mapper = new MapperConfiguration(configuration =>
        {
            configuration.AddProfile(new MappingProfile());
        }).CreateMapper();

        _controller = new AdminClanController(_clanServiceMock.Object, mapper);
    }

    [Fact]
    public void Controller_Requires_SuperAdmin_Role()
    {
        var authorize = Assert.Single(
            typeof(AdminClanController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal("SUPER_ADMIN", authorize.Roles);
    }

    [Fact]
    public async Task GetAllClans_Returns_Mapped_Clan_List()
    {
        var clans = new List<Clan>
        {
            new()
            {
                ClanId = Guid.NewGuid(),
                Name = "First clan",
                ImagePath = "first.png",
                Description = "First description"
            },
            new()
            {
                ClanId = Guid.NewGuid(),
                Name = "Second clan",
                ImagePath = "second.png",
                Description = "Second description"
            }
        };
        _clanServiceMock
            .Setup(service => service.GetAllClansAsync())
            .ReturnsAsync(clans);

        var result = await _controller.GetAllClans();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<List<ClanReadDto>>(ok.Value);
        Assert.Collection(
            response,
            first =>
            {
                Assert.Equal(clans[0].ClanId, first.ClanId);
                Assert.Equal(clans[0].Name, first.Name);
            },
            second =>
            {
                Assert.Equal(clans[1].ClanId, second.ClanId);
                Assert.Equal(clans[1].Name, second.Name);
            });
    }

    [Fact]
    public async Task DeleteClan_Returns_NotFound_When_Clan_Does_Not_Exist()
    {
        var clanId = Guid.NewGuid();
        _clanServiceMock
            .Setup(service => service.DeleteClanAsync(clanId))
            .ReturnsAsync(false);

        var result = await _controller.DeleteClan(clanId);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var error = Assert.IsType<ErrorDto>(notFound.Value);
        Assert.Equal("Clan not found or already deleted.", error.Message);
    }

    [Fact]
    public async Task DeleteClan_Returns_NoContent_When_Clan_Is_Deleted()
    {
        var clanId = Guid.NewGuid();
        _clanServiceMock
            .Setup(service => service.DeleteClanAsync(clanId))
            .ReturnsAsync(true);

        var result = await _controller.DeleteClan(clanId);

        Assert.IsType<NoContentResult>(result);
    }
}
