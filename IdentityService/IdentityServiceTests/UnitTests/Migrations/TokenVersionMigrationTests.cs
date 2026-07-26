using IdentityService.Data;
using Microsoft.EntityFrameworkCore;

namespace IdentityServiceTests.UnitTests.Migrations;

public class TokenVersionMigrationTests
{
    [Fact]
    public void MigrationAssembly_ContainsAddTokenVersionMigration()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new IdentityDbContext(options);

        Assert.Contains("20260726000000_AddTokenVersion", context.Database.GetMigrations());
    }
}
