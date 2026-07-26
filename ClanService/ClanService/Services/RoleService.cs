using System;
using ClanService.Interfaces;
using ClanService.Interfaces.Repositories;
using ClanService.Interfaces.Services;
using Shared.Contracts;
namespace ClanService.Services;

public class RoleService : IRoleService
{
    private readonly IClanMembershipRepository _membershipRepository;
    private readonly ILogger<RoleService> _logger;
    private readonly IClanMessageProducer _clanMessageProducer;
    
    public RoleService(IClanMembershipRepository membershipRepository, ILogger<RoleService> logger, IClanMessageProducer clanMessageProducer)
    {
        _logger = logger;
        _membershipRepository = membershipRepository;
        _clanMessageProducer = clanMessageProducer;
    }
    
    private static readonly string[] AllowedRoles = [ClanRole.MEMBER.ToString(), ClanRole.ADMIN.ToString(), ClanRole.OWNER.ToString()];

    public async Task<bool> UpdateRoleAsync(Guid membershipId, string roleName)
    {
        if (!AllowedRoles.Contains(roleName))
        {
            _logger.LogWarning("Invalid role name '{RoleName}' provided.", roleName);
            return false;
        }
        if(roleName == ClanRole.OWNER.ToString())
        {
            _logger.LogWarning("Attempt to assign OWNER role, which is not allowed through this endpoint.");
            return false;
        }

        try
        {
            var existingMembership = await _membershipRepository.GetByIdAsync(membershipId);
            if (existingMembership == null)
            {
                _logger.LogWarning("Membership not found.");
                return false;
            }
            
            existingMembership.Role = roleName;
            await _membershipRepository.UpdateAsync(existingMembership);
            await _clanMessageProducer.PublishClanRoleEventAsync(new ClanRoleEventDto
            {
                UserId = existingMembership.UserId,
                ClanId = existingMembership.ClanId.ToString(),
                Role = roleName,
                EventType = ClanRoleEventType.ASSIGN_ROLE.ToString()
            });
            await _clanMessageProducer.PublishClanMembershipChangedMessageAsync(new ClanMembershipChangedMessage
            {
                MembershipId = existingMembership.Id,
                ClanId = existingMembership.ClanId.ToString(),
                UserId = existingMembership.UserId,
                Role = roleName,
                ChangeType = ClanMembershipChangeType.RoleChanged
            });
            _logger.LogInformation("Role of membership {MembershipId} updated to {RoleName}.", membershipId, roleName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while updating role of membership {MembershipId}.", membershipId);
            return false;
        }
    }
}
