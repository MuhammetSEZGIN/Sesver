using ClanService.Models;
using ClanService.Interfaces;
using ClanService.Interfaces.Repositories;
using ClanService.Interfaces.Services;
using Shared.Contracts;

namespace ClanService.Services
{
    public class ClanMembershipService : IClanMembershipService
    {
        private readonly IClanMembershipRepository _membershipRepository;
        private readonly IUserRepository _userRepository;
        private readonly IClanMessageProducer _clanMessageProducer;
        private readonly ILogger<ClanMembershipService> _logger;

        public ClanMembershipService(
            IClanMembershipRepository membershipRepository,
            IUserRepository userRepository,
            IClanMessageProducer clanMessageProducer,
            ILogger<ClanMembershipService> logger)
        {
            _logger = logger;
            _membershipRepository = membershipRepository;
            _userRepository = userRepository;
            _clanMessageProducer = clanMessageProducer;
        }

        public async Task<(ClanMembership, string)> AddMemberAsync(ClanMembership membership)
        {
            try
            {
                var existingMembership = await _membershipRepository.GetMemberByUserAndClanIdAsync(membership.UserId, membership.ClanId);
                if (existingMembership != null)
                    return (null, "User is already a member of this clan.");

                var existingUser = await _userRepository.GetByIdAsync(membership.UserId);
                if (existingUser == null)
                    return (null, "User not found.");

                membership.Id = Guid.NewGuid();
                await _membershipRepository.AddAsync(membership);
                await _clanMessageProducer.PublishClanRoleEventAsync(new ClanRoleEventDto
                {
                    UserId = membership.UserId,
                    ClanId = membership.ClanId.ToString(),
                    Role = ClanRole.MEMBER.ToString(),
                    EventType = ClanRoleEventType.ASSIGN_ROLE.ToString()
                });
                await _clanMessageProducer.PublishClanMembershipChangedMessageAsync(new ClanMembershipChangedMessage
                {
                    MembershipId = membership.Id,
                    ClanId = membership.ClanId.ToString(),
                    UserId = membership.UserId,
                    UserName = existingUser.Username,
                    AvatarUrl = existingUser.AvatarUrl,
                    Role = membership.Role,
                    ChangeType = ClanMembershipChangeType.Joined
                });
                _logger.LogInformation("User {UserId} added to clan {ClanId} successfully.", membership.UserId, membership.ClanId);
                return (membership, "User added to the clan successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while adding user {UserId} to clan {ClanId}.", membership.UserId, membership.ClanId);
                return (null, "An error occurred while adding user to the clan.");
            }
        }

        public async Task<ClanMembership> GetMembershipAsync(Guid membershipId)
        {
            return await _membershipRepository.GetByIdAsync(membershipId);
        }

        public async Task<List<ClanMembership>> GetMembershipsByClanIdAsync(Guid clanId)
        {
            var result = await _membershipRepository.GetMembersByClanIdAsync(clanId);
            return result.ToList();
        }

        public async Task<List<ClanMembership>> GetMembershipsByUserIdAsync(string userId)
        {
            return await _membershipRepository.GetByUserIdAsync(userId);
        }

        public async Task<(ClanMembership, string)> LeaveClanAsync(string userId, Guid clanId)
        {
            try
            {
                var membership = await _membershipRepository.GetMemberByUserAndClanIdAsync(userId, clanId);

                if (membership == null)
                    return (null, "User is not a member of this clan.");

                await _membershipRepository.DeleteAsync(membership);
                await _clanMessageProducer.PublishClanRoleEventAsync(new ClanRoleEventDto
                {
                    UserId = membership.UserId,
                    ClanId = membership.ClanId.ToString(),
                    Role = null,
                    EventType = ClanRoleEventType.REMOVE_ROLE.ToString()
                });
                await _clanMessageProducer.PublishClanMembershipChangedMessageAsync(new ClanMembershipChangedMessage
                {
                    MembershipId = membership.Id,
                    ClanId = membership.ClanId.ToString(),
                    UserId = membership.UserId,
                    Role = membership.Role,
                    ChangeType = ClanMembershipChangeType.Removed
                });
                _logger.LogInformation("User {UserId} has left clan {ClanId} successfully.", userId, clanId);
                return (membership, "User has left the clan successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while user {UserId} was leaving clan {ClanId}.", userId, clanId);
                return (null, "An error occurred while leaving the clan.");
            }
        }

        public async Task<bool> RemoveMemberAsync(string userId, Guid clanId)
        {
            var existing = await _membershipRepository.GetMemberByUserAndClanIdAsync(userId, clanId);
            _logger.LogWarning("There is no membership for user {UserId} and clan {ClanId}.", userId, clanId);
            if (existing == null) return false;
            try
            {
                await _membershipRepository.DeleteAsync(existing);
                _logger.LogInformation("Membership for user {UserId} and clan {ClanId} removed successfully.", userId, clanId);
                await _clanMessageProducer.PublishClanRoleEventAsync(new ClanRoleEventDto
                {
                    UserId = existing.UserId,
                    ClanId = existing.ClanId.ToString(),
                    Role = null,
                    EventType = ClanRoleEventType.REMOVE_ROLE.ToString()
                });
                await _clanMessageProducer.PublishClanMembershipChangedMessageAsync(new ClanMembershipChangedMessage
                {
                    MembershipId = existing.Id,
                    ClanId = existing.ClanId.ToString(),
                    UserId = existing.UserId,
                    Role = existing.Role,
                    ChangeType = ClanMembershipChangeType.Removed
                });
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while removing membership for user {UserId} and clan {ClanId}.", userId, clanId);
                return false;
            }

        }
    }
}
