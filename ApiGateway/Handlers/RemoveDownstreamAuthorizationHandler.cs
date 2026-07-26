namespace ApiGateway.Handlers;

/// <summary>
/// JWT is consumed by the gateway. Downstream services receive only the trusted
/// identity headers produced after gateway validation.
/// </summary>
public sealed class RemoveDownstreamAuthorizationHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        request.Headers.Authorization = null;
        request.Headers.Remove("Proxy-Authorization");
        return base.SendAsync(request, cancellationToken);
    }
}
