using System;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Data;

public class IdentityDbContext : IdentityUserContext<ApplicationUser>
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options) { }

    public DbSet<UserRefreshToken> UserRefreshTokens { get; set; }
    public DbSet<Friendship> Friendships { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.Entity<UserRefreshToken>(entity =>
        {
            entity.HasIndex(x => x.RefreshToken).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity
                .HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Friendship>(entity =>
        {
            entity.HasIndex(x => new { x.RequesterId, x.AddresseeId }).IsUnique();
            entity.ToTable(t =>
                t.HasCheckConstraint("CK_Friendship_NotSelf", "\"RequesterId\" <> \"AddresseeId\"")
            );
            entity
                .HasOne(e => e.Requester)
                .WithMany()
                .HasForeignKey(e => e.RequesterId)
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasOne(e => e.Addressee)
                .WithMany()
                .HasForeignKey(e => e.AddresseeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
