using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).IsRequired().HasMaxLength(320);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Role).HasConversion<int>();
        builder.Property(u => u.PasswordHash).HasMaxLength(500);
        builder.Property(u => u.ExternalSubject).HasMaxLength(200);
        builder.Property(u => u.RefreshTokenHash).HasMaxLength(200);

        // Login is by email, so it must be unique and indexed.
        builder.HasIndex(u => u.Email).IsUnique();

        // Refresh happens on every session renewal; without this it is a table scan.
        builder.HasIndex(u => u.RefreshTokenHash);

        // Set when the deployment federates with corporate OIDC.
        builder.HasIndex(u => u.ExternalSubject);
    }
}
