using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class PersonSkillConfiguration : IEntityTypeConfiguration<PersonSkill>
{
    public void Configure(EntityTypeBuilder<PersonSkill> builder)
    {
        builder.ToTable("PersonSkills");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).IsRequired().HasMaxLength(60);

        // "Who here knows FHIR" is the question skills exist to answer, so the column the
        // search runs over is indexed.
        builder.HasIndex(s => s.Name);
        builder.HasIndex(s => new { s.PersonId, s.OrderIndex });
    }
}

public sealed class PersonAchievementConfiguration : IEntityTypeConfiguration<PersonAchievement>
{
    public void Configure(EntityTypeBuilder<PersonAchievement> builder)
    {
        builder.ToTable("PersonAchievements");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Detail).HasMaxLength(1000);

        builder.HasIndex(a => new { a.PersonId, a.OrderIndex });
    }
}

public sealed class PersonPhotoConfiguration : IEntityTypeConfiguration<PersonPhoto>
{
    public void Configure(EntityTypeBuilder<PersonPhoto> builder)
    {
        builder.ToTable("PersonPhotos");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Bytes).IsRequired();
        builder.Property(p => p.ContentType).IsRequired().HasMaxLength(100);

        builder.Ignore(p => p.Version);

        // One photo per person, enforced by the database rather than by hoping.
        builder.HasIndex(p => p.PersonId).IsUnique();

        builder.HasOne(p => p.Person)
            .WithOne(p => p.Photo)
            .HasForeignKey<PersonPhoto>(p => p.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
