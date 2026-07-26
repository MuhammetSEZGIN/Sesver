using Microsoft.AspNetCore.Mvc;
using ClanService.Interfaces;
using ClanService.Models;
using ClanService.DTOs;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;

namespace ClanService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VoiceChannelController : ControllerBase
    {
        private readonly IVoiceChannelService _voiceChannelService;
        private readonly IMapper _mapper;
        
        public VoiceChannelController(IVoiceChannelService voiceChannelService, IMapper mapper)
        {
            _voiceChannelService = voiceChannelService;
            _mapper = mapper;
        }
       
        [HttpPost("clanId/{clanId}")]
        [Authorize(Roles = "OWNER,ADMIN")]
        public async Task<IActionResult> CreateVoiceChannel([FromBody] VoiceChannelCreateDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(new ErrorDto 
                { 
                    Message = "Invalid voice channel data.", 
                    Errors = ModelState 
                });

            var voiceChannel = _mapper.Map<VoiceChannel>(dto);
            var created = await _voiceChannelService.CreateVoiceChannelAsync(voiceChannel);
            if (created.Item1 == null)
                return NotFound(new ErrorDto { Message = created.Item2 });
            var readDto = _mapper.Map<VoiceChannelReadDto>(created.Item1);    

            return Ok(readDto);
        }

        [HttpGet("{voiceChannelId}")]
        [Authorize(Roles = "OWNER,ADMIN,MEMBER")]
        public async Task<IActionResult> GetVoiceChannelById(Guid voiceChannelId)
        {
            var channel = await _voiceChannelService.GetVoiceChannelByIdAsync(voiceChannelId);
            if (channel == null)
                return NotFound(new ErrorDto { Message = "VoiceChannel not found." });

            var readDto = _mapper.Map<VoiceChannelReadDto>(channel);    
            return Ok(readDto);
        }

        [HttpGet("clanId/{clanId}")]
        [Authorize(Roles = "OWNER,ADMIN,MEMBER")]
        public async Task<IActionResult> GetVoiceChannelsByClanId(Guid clanId)
        {
            var channels = await _voiceChannelService.GetVoiceChannelsByClanIdAsync(clanId);
            var readDtoList = _mapper.Map<List<VoiceChannelReadDto>>(channels); 
            return Ok(readDtoList);
        }

        [HttpPut("clanId/{clanId}")]
        [Authorize(Roles = "OWNER,ADMIN")]
        public async Task<IActionResult> UpdateVoiceChannel([FromBody] VoiceChannelUpdateDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(new ErrorDto 
                { 
                    Message = "Invalid voice channel data.", 
                    Errors = ModelState 
                });

            var existing = await _voiceChannelService.GetVoiceChannelByIdAsync(dto.VoiceChannelId);
            if (existing == null)
                return NotFound(new ErrorDto { Message = "VoiceChannel not found." });

            existing.Name = dto.Name;
            existing.IsActive = dto.IsActive;

            var updated = await _voiceChannelService.UpdateVoiceChannelAsync(existing);
            var readDto = _mapper.Map<VoiceChannelReadDto>(updated); 
            return Ok(readDto);
        }

        [HttpDelete("channel/{voiceChannelId}/clanId/{clanId}")]
        [Authorize(Roles = "OWNER,ADMIN")]
        public async Task<IActionResult> DeleteVoiceChannel(Guid voiceChannelId)
        {
            var result = await _voiceChannelService.DeleteVoiceChannelAsync(voiceChannelId);
            if (!result)
                return NotFound(new ErrorDto { Message = "VoiceChannel not found or already deleted." });

            return NoContent();
        }
    }
}
