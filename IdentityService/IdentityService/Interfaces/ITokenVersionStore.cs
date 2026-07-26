namespace IdentityService.Interfaces;

public interface ITokenVersionStore
{
    Task<bool> SetAtLeastAsync(string userId, int tokenVersion);
    Task<int?> IncrementAsync(string userId, int databaseVersion);
}
