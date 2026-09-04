using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class JiraSettingsConfiguration : IEntityTypeConfiguration<JiraSettings>
{
    public void Configure(EntityTypeBuilder<JiraSettings> builder)
    {
        builder.ToTable("JiraSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.BaseUrl).HasMaxLength(300);
        builder.Property(s => s.Email).HasMaxLength(320);
        // Ciphertext is considerably longer than the token it protects.
        builder.Property(s => s.EncryptedApiToken).HasMaxLength(2000);
        builder.Property(s => s.TokenHint).HasMaxLength(40);
        builder.Property(s => s.UpdatedBy).HasMaxLength(200);
        builder.Property(s => s.LastSyncResult).HasMaxLength(500);

        builder.Ignore(s => s.IsUsable);
    }
}
