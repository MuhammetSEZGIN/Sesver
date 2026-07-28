namespace VersionControlService.Services;

/// <summary>
/// Admin endpoint'lerine erisebilecek global roller.
/// Admin__AllowedRoles__0 gibi ortam degiskenleriyle ezilebilir.
/// </summary>
public class AdminAuthorizationOptions
{
    private static readonly string[] DefaultRoles = ["SUPER_ADMIN"];

    public AdminAuthorizationOptions(IEnumerable<string>? allowedRoles = null)
    {
        var roles = allowedRoles?
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim().ToUpperInvariant())
            .ToArray();

        AllowedRoles = new HashSet<string>(
            roles is { Length: > 0 } ? roles : DefaultRoles,
            StringComparer.OrdinalIgnoreCase);
    }

    public ISet<string> AllowedRoles { get; }
}
