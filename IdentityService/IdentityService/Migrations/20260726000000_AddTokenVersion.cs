using IdentityService.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("20260726000000_AddTokenVersion")]
public partial class AddTokenVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "TokenVersion",
            schema: "identity",
            table: "AspNetUsers",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TokenVersion",
            schema: "identity",
            table: "AspNetUsers"
        );
    }
}
