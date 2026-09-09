using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class PersonAvailabilityConfiguration : IEntityTypeConfiguration<PersonAvailability>
{
    public void Configure(EntityTypeBuilder<PersonAvailability> builder)
    {
        builder.ToTable("PersonAvailability");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Note).HasMaxLength(400);
        builder.Property(a => a.RecordedBy).HasMaxLength(200);

        // Removing somebody from the roster takes their calendar with them: an
        // availability record for nobody is not worth keeping.
        builder.HasOne(a => a.Person)
            .WithMany()
            .HasForeignKey(a => a.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Every capacity query is "who is away in this window".
        builder.HasIndex(a => new { a.PersonId, a.FromDate, a.ToDate });
    }
}

public sealed class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.ToTable("WorkItems");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Title).IsRequired().HasMaxLength(300);
        builder.Property(w => w.Detail).HasMaxLength(2000);
        builder.Property(w => w.CreatedBy).HasMaxLength(200);

        builder.HasOne(w => w.Board)
            .WithMany()
            .HasForeignKey(w => w.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        // Taking somebody off the roster must not delete the work: it becomes unassigned,
        // which is exactly the state a lead needs to see and reassign.
        builder.HasOne(w => w.Person)
            .WithMany()
            .HasForeignKey(w => w.PersonId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(w => w.BoardId);
        builder.HasIndex(w => new { w.PersonId, w.Status });

        builder.Ignore(w => w.IsDone);

        // Board is soft-deleted behind a global filter. Without the matching filter here,
        // work items belonging to a deleted board keep appearing in every person's task
        // list — the same trap SquadMember hit.
        builder.HasQueryFilter(w => !w.Board.IsDeleted);
    }
}
