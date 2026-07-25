using Microsoft.EntityFrameworkCore;
using NotificationService.Models;

namespace NotificationService.Data;

public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var notification = modelBuilder.Entity<NotificationEntity>();
        notification.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt })
            .IsDescending(false, false, true);
        notification.Property(x => x.Type).HasConversion<string>().HasMaxLength(64);
        notification.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
