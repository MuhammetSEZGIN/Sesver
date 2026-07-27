using ClanService.Models;
using ClanService.Interfaces;
using ClanService.DTOs.ClanDtos;
using ClanService.Interfaces.Repositories;
using ClanService.Interfaces.Services;
using Shared.Contracts;

namespace ClanService.Services
{
    public class ClanService : IClanService
    {
        private readonly IClanRepository _clanRepository;
        private readonly IUserRepository _userRepository;
        private readonly IClanMessageProducer _clanMessageProducer;

        private readonly IClanMembershipRepository _clanMembershipRepository;
        private readonly IClanInvitation _clanInvitationRepository;
        private readonly ILogger<ClanService> _logger;

        public ClanService(
            IClanRepository clanRepository,
            IUserRepository userRepository,
            IClanMembershipRepository clanMembershipRepository,
            IClanInvitation clanInvitationRepository,
            IClanMessageProducer clanMessageProducer,
            ILogger<ClanService> logger)
        {
            _logger = logger;
            _clanRepository = clanRepository;
            _userRepository = userRepository;
            _clanMembershipRepository = clanMembershipRepository;
            _clanInvitationRepository = clanInvitationRepository;
            _clanMessageProducer = clanMessageProducer;
        }

        public async Task<(Clan, string)> CreateClanAsync(Clan clan, string userId)
        {
            try
            {
                var user = await _userRepository.GetByIdAsync(userId);
                if (user == null)
                    return (null, "User not found");

                clan.ClanId = Guid.NewGuid();
                await _clanRepository.AddAsync(clan);

                var clanMembership = new ClanMembership
                {
                    ClanId = clan.ClanId,
                    UserId = userId,
                    Role = ClanRole.OWNER.ToString()
                };
                await _clanMembershipRepository.AddAsync(clanMembership);
                await _clanMessageProducer.PublishClanRoleEventAsync(new ClanRoleEventDto
                {
                    UserId = userId,
                    ClanId = clan.ClanId.ToString(),
                    Role = ClanRole.OWNER.ToString(),
                    EventType = ClanRoleEventType.ASSIGN_ROLE.ToString()
                });
                await _clanMessageProducer.PublishClanMembershipChangedMessageAsync(new ClanMembershipChangedMessage
                {
                    MembershipId = clanMembership.Id,
                    ClanId = clan.ClanId.ToString(),
                    UserId = userId,
                    UserName = user.Username,
                    AvatarUrl = user.AvatarUrl,
                    Role = clanMembership.Role,
                    ChangeType = ClanMembershipChangeType.Joined
                });
                return (clan, "Clan created successfully");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while creating clan");
                return (null, "Error while creating clan");
            }
        }

        public async Task<Clan> GetClanByIdAsync(Guid clanId)
        {
            try
            {
                return await _clanRepository.GetClanWithDetailsAsync(clanId);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while getting clan {ClanId}", clanId);
                return null;
            }
        }

        public async Task<IEnumerable<Clan>> GetAllClansAsync()
        {
            try
            {
                return await _clanRepository.GetAllAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while getting all clans");
                return Enumerable.Empty<Clan>();
            }
        }

        public async Task<Clan> UpdateClanAsync(Clan clan)
        {
            try
            {
                await _clanRepository.UpdateAsync(clan);
                return clan;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while updating clan {ClanId}", clan.ClanId);
                return null;
            }
        }

        public async Task<bool> DeleteClanAsync(Guid clanId)
        {
            try
            {
                var existing = await _clanRepository.GetByIdAsync(clanId);
                if (existing == null) return false;

                var result = await _clanRepository.DeleteAsync(existing);
                if (result != null)
                {
                    await _clanMessageProducer.PublishClanDeletedMessageAsync(clanId.ToString());
                    await _clanMessageProducer.PublishClanRoleEventAsync(new ClanRoleEventDto
                    {
                        UserId = null,
                        ClanId = clanId.ToString(),
                        Role = null,
                        EventType = ClanRoleEventType.REMOVE_ALL_ROLES.ToString()
                    });
                    _logger.LogInformation("Clan {ClanId} deleted successfully", clanId);
                }
                else
                {
                    _logger.LogError("Failed to delete clan {ClanId}", clanId);
                }
                return result != null;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while deleting clan {ClanId}", clanId);
                return false;
            }
        }

        public async Task<List<Clan>> GetClansByUserIdAsync(string userId)
        {
            try
            {
                var result = await _clanRepository.GetClansByUserIdAsync(userId);
                return result.ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while getting clans for user {UserId}", userId);
                return new List<Clan>();
            }
        }

        public async Task<ClanInvitation> CreateInviteTokenAsync(Guid clanId, TimeSpan? expipreInHours, int? maxUses)
        {
            try
            {
                var clanInvitation = new ClanInvitation
                {
                    ClanId = clanId,
                    ExpiresAt = DateTime.UtcNow.Add(expipreInHours ?? TimeSpan.FromHours(24)),
                    InviteCode = GenerateInviteCode(),
                    IsActive = true,
                    MaxUses = maxUses ?? 10,
                    UsedCount = 0
                };

                await _clanInvitationRepository.AddAsync(clanInvitation);
                return clanInvitation;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while creating invite token for clan {ClanId}", clanId);
                return null;
            }
        }

        private string GenerateInviteCode()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray())[..8];
        }

        public async Task<ClanInvitation> GetInvitationByCodeAsync(string code)
        {
            try
            {
                return await _clanInvitationRepository.GetByCodeAsync(code);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while getting invitation by code {Code}", code);
                return null;
            }
        }

        public async Task<(bool, string, ClanInvitation)> ValidateAndUseInvitationAsync(string code)
        {
            try
            {
                var invitation = await GetInvitationByCodeAsync(code);
                if (invitation == null || !invitation.IsActive)
                    return (false, "The code is invalid or inactive", null);

                if (DateTime.UtcNow > invitation.ExpiresAt)
                {
                    invitation.IsActive = false;
                    await _clanInvitationRepository.UpdateAsync(invitation);
                    return (false, "Expired invitation code", null);
                }

                if (invitation.UsedCount >= invitation.MaxUses)
                {
                    invitation.IsActive = false;
                    await _clanInvitationRepository.UpdateAsync(invitation);
                    return (false, "Max usage limit reached", null);
                }

                invitation.UsedCount++;
                await _clanInvitationRepository.UpdateAsync(invitation);

                return (true, "Invitation code is valid", invitation);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while validating invitation code {Code}", code);
                return (false, "An error occurred while validating the invitation", null);
            }
        }
    }
}
