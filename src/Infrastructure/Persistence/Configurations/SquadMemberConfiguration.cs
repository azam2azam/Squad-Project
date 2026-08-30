using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class SquadMemberConfiguration : IEntityTypeConfiguration<SquadMember>
{
    public void Configure(EntityTypeBuilder<SquadMember> builder)
    {
        builder.ToTable("SquadMembers");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Detail).HasMaxLength(200);
        builder.Property(m => m.Role).HasConversion<int>();

        builder.HasOne(m => m.Person)
            .WithMany(p => p.Assignments)
            .HasForeignKey(m => m.PersonId)
            // Restrict, not Cascade: deactivating a person must never destroy the
            // historical record of squads they were on (spec section 5).
            .OnDelete(DeleteBehavior.Restrict);

        // A person appears at most once per board.
        builder.HasIndex(m => new { m.BoardId, m.PersonId }).IsUnique();
        builder.HasIndex(m => new { m.BoardId, m.OrderIndex });

        // Mirrors the Board soft-delete filter. Without this, querying SquadMembers
        // directly (e.g. PUT /members/{id}) would reach members of deleted boards.
        builder.HasQueryFilter(m => !m.Board.IsDeleted);
    }
}
