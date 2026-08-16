using IdentityService.Data;
using Microsoft.EntityFrameworkCore;

namespace IdentityServiceTests.UnitTests.Migrations;

public class ProfileBackgroundUrlMigrationTests
{
    [Fact]
    public void MigrationAssembly_ContainsAddUserProfileBackgroundUrlMigration()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new IdentityDbContext(options);

        Assert.Contains(
            "20260815000000_AddUserProfileBackgroundUrl",
            context.Database.GetMigrations()
        );
    }
}
