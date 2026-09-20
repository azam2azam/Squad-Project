using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.FullName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.DefaultDetail).HasMaxLength(200);
        builder.Property(p => p.Email).HasMaxLength(320);
        builder.Property(p => p.AvatarColorOverride).HasMaxLength(9);
        builder.Property(p => p.DefaultRole).HasConversion<int>();

        builder.Property(p => p.Headline).HasMaxLength(120);
        builder.Property(p => p.About).HasMaxLength(2000);

        builder.HasMany(p => p.Skills)
            .WithOne(s => s.Person)
            .HasForeignKey(s => s.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Achievements)
            .WithOne(a => a.Person)
            .HasForeignKey(a => a.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // The backing fields are the source of truth; EF must not use the read-only
        // properties — the same rule Board.Members follows.
        builder.Metadata
            .FindNavigation(nameof(Person.Skills))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Metadata
            .FindNavigation(nameof(Person.Achievements))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(p => p.Initials);

        builder.HasIndex(p => p.FullName);
        builder.HasIndex(p => p.IsActive);

        // Deliberately no query filter: the roster manager must be able to show
        // deactivated people so they can be reactivated.
    }
}
