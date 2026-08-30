using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.ToTable("Boards");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Title).IsRequired().HasMaxLength(200);
        builder.Property(b => b.Product).IsRequired().HasMaxLength(100);
        builder.Property(b => b.SquadName).IsRequired().HasMaxLength(100);
        builder.Property(b => b.Sprint).HasMaxLength(100);
        builder.Property(b => b.BlockerNote).HasMaxLength(1000);
        builder.Property(b => b.JiraProjectKey).HasMaxLength(50);
        builder.Property(b => b.JiraBoardId).HasMaxLength(50);
        builder.Property(b => b.CreatedBy).IsRequired().HasMaxLength(200);

        // Stored as int so the enum can gain members without a data migration.
        builder.Property(b => b.Status).HasConversion<int>();

        builder.HasMany(b => b.Members)
            .WithOne(m => m.Board)
            .HasForeignKey(m => m.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        // The backing field is the source of truth; EF must not use the read-only property.
        builder.Metadata
            .FindNavigation(nameof(Board.Members))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Derived values are never persisted.
        builder.Ignore(b => b.Composition);
        builder.Ignore(b => b.Warnings);

        builder.HasIndex(b => b.OrderIndex);
        builder.HasIndex(b => b.IsDeleted);

        // Soft-deleted boards drop out of every query unless explicitly asked for.
        builder.HasQueryFilter(b => !b.IsDeleted);
    }
}
