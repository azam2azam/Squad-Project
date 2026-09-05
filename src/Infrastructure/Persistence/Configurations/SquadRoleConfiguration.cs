using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class SquadRoleConfiguration : IEntityTypeConfiguration<SquadRole>
{
    public void Configure(EntityTypeBuilder<SquadRole> builder)
    {
        builder.ToTable("SquadRoles");

        // The key is the number stored on every member and person, so it is assigned by
        // the application rather than generated: it has to be known before insert and
        // must never be renumbered.
        builder.HasKey(r => r.Value);
        builder.Property(r => r.Value).ValueGeneratedNever();

        builder.Property(r => r.Name).IsRequired().HasMaxLength(60);
        builder.Property(r => r.Label).IsRequired().HasMaxLength(80);
        builder.Property(r => r.PluralLabel).IsRequired().HasMaxLength(80);
        builder.Property(r => r.Color).IsRequired().HasMaxLength(7);

        // The identifier is what spreadsheets and the API match on, so it must be unique.
        builder.HasIndex(r => r.Name).IsUnique();

        builder.Ignore(r => r.AsRole);

        // Seeded here rather than in the seeder so a fresh database has the seven roles
        // before anything can reference them, including in environments that never run
        // the demo seeder.
        builder.HasData(RoleMetadata.Defaults.Select(d => new
        {
            Value = (int)d.Role,
            Name = d.Name,
            Label = d.Label,
            PluralLabel = d.PluralLabel,
            Color = d.Color,
            OrderIndex = d.Order,
            IsBuiltIn = true,
            IsActive = true
        }));
    }
}
