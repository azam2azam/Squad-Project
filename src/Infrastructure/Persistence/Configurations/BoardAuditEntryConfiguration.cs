using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class BoardAuditEntryConfiguration : IEntityTypeConfiguration<BoardAuditEntry>
{
    public void Configure(EntityTypeBuilder<BoardAuditEntry> builder)
    {
        builder.ToTable("BoardAuditEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Field).IsRequired().HasMaxLength(100);
        builder.Property(e => e.OldValue).HasMaxLength(1000);
        builder.Property(e => e.NewValue).HasMaxLength(1000);
        builder.Property(e => e.ChangedBy).IsRequired().HasMaxLength(200);

        builder.Ignore(e => e.Summary);

        // No FK to Boards: audit rows must survive a board being purged.
        builder.HasIndex(e => new { e.BoardId, e.ChangedAt });
    }
}
