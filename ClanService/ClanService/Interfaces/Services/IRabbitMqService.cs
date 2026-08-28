using System;
using Shared.Contracts;
namespace ClanService.Interfaces;

public interface IRabbitMqService
{
    Task ConsumeUserInformation(UserUpdatedMessage userUpdatedMessage);
    Task ConsumeUserDeleted(UserDeletedMessage userDeletedMessage);
}
