using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class BoardCategoryConfiguration : IEntityTypeConfiguration<BoardCategory>
{
    public void Configure(EntityTypeBuilder<BoardCategory> builder)
    {
        builder.ToTable("BoardCategories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(80);
        builder.Property(c => c.Description).HasMaxLength(400);
        builder.Property(c => c.Color).IsRequired().HasMaxLength(7);

        // One programme per name, so two people cannot create rival "AI" categories.
        builder.HasIndex(c => c.Name).IsUnique();
    }
}
