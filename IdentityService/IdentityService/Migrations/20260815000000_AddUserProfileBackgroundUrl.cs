using IdentityService.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("20260815000000_AddUserProfileBackgroundUrl")]
public partial class AddUserProfileBackgroundUrl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProfileBackgroundUrl",
            schema: "identity",
            table: "AspNetUsers",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true
        );

        // Widened from 190 to the 200 characters the client contract advertises.
        migrationBuilder.AlterColumn<string>(
            name: "Bio",
            schema: "identity",
            table: "AspNetUsers",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(190)",
            oldMaxLength: 190,
            oldNullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ProfileBackgroundUrl",
            schema: "identity",
            table: "AspNetUsers"
        );

        migrationBuilder.AlterColumn<string>(
            name: "Bio",
            schema: "identity",
            table: "AspNetUsers",
            type: "character varying(190)",
            maxLength: 190,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200,
            oldNullable: true
        );
    }
}
