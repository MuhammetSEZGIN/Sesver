using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NotificationService.Data;

#nullable disable

namespace NotificationService.Migrations;

[DbContext(typeof(NotificationDbContext))]
[Migration("20260725000000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                TargetId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                IsRead = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Notifications", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_Notifications_UserId_IsRead_CreatedAt",
            table: "Notifications",
            columns: new[] { "UserId", "IsRead", "CreatedAt" },
            descending: new[] { false, false, true });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "Notifications");
}
