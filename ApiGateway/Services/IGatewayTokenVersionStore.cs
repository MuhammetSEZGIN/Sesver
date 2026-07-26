namespace ApiGateway.Services;

public interface IGatewayTokenVersionStore
{
    Task<int?> GetAsync(string userId);
}
