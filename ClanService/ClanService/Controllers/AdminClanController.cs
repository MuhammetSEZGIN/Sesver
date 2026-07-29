using AutoMapper;
using ClanService.DTOs;
using ClanService.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClanService.Controllers;

[ApiController]
[Route("api/admin/clans")]
[Authorize(Roles = "SUPER_ADMIN")]
public class AdminClanController : ControllerBase
{
    private readonly IClanService _clanService;
    private readonly IMapper _mapper;

    public AdminClanController(IClanService clanService, IMapper mapper)
    {
        _clanService = clanService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllClans()
    {
        var clans = await _clanService.GetAllClansAsync();
        return Ok(_mapper.Map<List<ClanReadDto>>(clans));
    }

    [HttpDelete("{clanId:guid}")]
    public async Task<IActionResult> DeleteClan(Guid clanId)
    {
        var deleted = await _clanService.DeleteClanAsync(clanId);
        if (!deleted)
        {
            return NotFound(new ErrorDto
            {
                Message = "Clan not found or already deleted."
            });
        }

        return NoContent();
    }
}
