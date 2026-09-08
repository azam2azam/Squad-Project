using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class SmartsheetSettingsConfiguration : IEntityTypeConfiguration<SmartsheetSettings>
{
    public void Configure(EntityTypeBuilder<SmartsheetSettings> builder)
    {
        builder.ToTable("SmartsheetSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.BaseUrl).HasMaxLength(300);
        // Ciphertext is considerably longer than the token it protects.
        builder.Property(s => s.EncryptedAccessToken).HasMaxLength(2000);
        builder.Property(s => s.TokenHint).HasMaxLength(40);
        builder.Property(s => s.ProgressColumn).HasMaxLength(200);
        builder.Property(s => s.StatusColumn).HasMaxLength(200);
        builder.Property(s => s.UpdatedBy).HasMaxLength(200);
        builder.Property(s => s.LastSyncResult).HasMaxLength(500);
    }
}
