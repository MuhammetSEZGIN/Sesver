namespace IdentityService.DTOs;

public sealed class AdminUserListItemDto
{
    public string Id { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public string AvatarUrl { get; set; }
    public bool EmailConfirmed { get; set; }
}

public sealed class AdminUserPageDto
{
    public IReadOnlyList<AdminUserListItemDto> Items { get; set; } = [];
    public int Page { get; set; }
    public int Limit { get; set; }
    public int TotalCount { get; set; }
}
