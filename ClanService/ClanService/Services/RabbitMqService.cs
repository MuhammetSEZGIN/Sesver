using System;
using ClanService.Interfaces;
using Shared.Contracts;
using ClanService.Models;
using ClanService.Interfaces.Repositories;

namespace ClanService.Services;

public class RabbitMqService : IRabbitMqService
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<RabbitMqService> _logger;
    public RabbitMqService(IUserRepository userRepository, ILogger<RabbitMqService> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }


    public async Task ConsumeUserInformation(UserUpdatedMessage userUpdatedMessage)
    {
        var existing = await _userRepository.GetByIdAsync(userUpdatedMessage.userId);
        if (existing != null)
        {
            existing.Username = userUpdatedMessage.userName;
            existing.AvatarUrl = userUpdatedMessage.AvatarUrl;
            await _userRepository.UpdateAsync(existing);
            _logger.LogInformation("User {UserId} updated successfully.", userUpdatedMessage.userId);
        }
        else
        {
            var user = new User
            {
                Id = userUpdatedMessage.userId,
                Username = userUpdatedMessage.userName,
                AvatarUrl = userUpdatedMessage.AvatarUrl
            };
            await _userRepository.AddAsync(user);
            _logger.LogInformation("User {UserId} created successfully.", userUpdatedMessage.userId);
        }
    }

    public async Task ConsumeUserDeleted(UserDeletedMessage userDeletedMessage)
    {
        var existing = await _userRepository.GetByIdAsync(userDeletedMessage.UserId);
        if (existing == null)
        {
            _logger.LogInformation(
                "User {UserId} was already absent; deletion event ignored.",
                userDeletedMessage.UserId);
            return;
        }

        await _userRepository.DeleteAsync(existing);
        _logger.LogInformation("User {UserId} deleted successfully.", userDeletedMessage.UserId);
    }
}
