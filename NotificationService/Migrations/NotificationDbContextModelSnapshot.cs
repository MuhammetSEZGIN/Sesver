using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NotificationService.Data;

#nullable disable

namespace NotificationService.Migrations;

[DbContext(typeof(NotificationDbContext))]
partial class NotificationDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "9.0.1")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("NotificationService.Models.NotificationEntity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            b.Property<string>("ActorUserId").HasMaxLength(450).HasColumnType("character varying(450)");
            b.Property<string>("Body").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<DateTime>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            b.Property<bool>("IsRead").HasColumnType("boolean");
            b.Property<DateTime?>("ReadAt").HasColumnType("timestamp with time zone");
            b.Property<string>("TargetId").HasMaxLength(450).HasColumnType("character varying(450)");
            b.Property<string>("Title").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)");
            b.Property<string>("Type").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<string>("UserId").IsRequired().HasMaxLength(450).HasColumnType("character varying(450)");
            b.HasKey("Id");
            b.HasIndex("UserId", "IsRead", "CreatedAt").IsDescending(false, false, true);
            b.ToTable("Notifications");
        });
#pragma warning restore 612, 618
    }
}
