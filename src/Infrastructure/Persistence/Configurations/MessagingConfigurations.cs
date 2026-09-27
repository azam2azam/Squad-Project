using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Title).IsRequired().HasMaxLength(200);
        builder.Property(c => c.ScopeKey).IsRequired().HasMaxLength(120);
        builder.Property(c => c.SquadName).HasMaxLength(200);
        builder.Property(c => c.LastMessagePreview).HasMaxLength(200);
        builder.Property(c => c.LastMessageAuthor).HasMaxLength(200);

        // The invariant the whole feature rests on: one conversation per thing. Two
        // channels for one squad would split the conversation in half and neither half
        // would be the record of what was decided.
        builder.HasIndex(c => c.ScopeKey).IsUnique();

        // The inbox is ordered by this, every time it loads.
        builder.HasIndex(c => c.LastActivityAt);

        builder.HasOne(c => c.Board)
            .WithMany()
            .HasForeignKey(c => c.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Members)
            .WithOne(m => m.Conversation)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Conversation.Members))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ConversationMemberConfiguration : IEntityTypeConfiguration<ConversationMember>
{
    public void Configure(EntityTypeBuilder<ConversationMember> builder)
    {
        builder.ToTable("ConversationMembers");
        builder.HasKey(m => m.Id);

        builder.Ignore(m => m.UnreadSince);

        // Somebody is in a conversation once. Also the index behind "my inbox".
        builder.HasIndex(m => new { m.ConversationId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            // Restrict, not Cascade: deleting a user should not silently vacuum their
            // half of every thread out of everyone else's history. Accounts here are
            // deactivated rather than deleted anyway.
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Body).IsRequired().HasMaxLength(4000);

        builder.Ignore(m => m.IsDeleted);

        // Every read of a thread is "this conversation, newest first".
        builder.HasIndex(m => new { m.ConversationId, m.SentAt });

        builder.HasOne(m => m.Conversation)
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Author)
            .WithMany()
            .HasForeignKey(m => m.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.ReplyTo)
            .WithMany()
            .HasForeignKey(m => m.ReplyToMessageId)
            // NoAction rather than Cascade: a reply outlives the message it answers,
            // which is the entire point of leaving a tombstone behind on delete.
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(m => m.Mentions)
            .WithOne(x => x.Message)
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Message.Mentions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class MessageMentionConfiguration : IEntityTypeConfiguration<MessageMention>
{
    public void Configure(EntityTypeBuilder<MessageMention> builder)
    {
        builder.ToTable("MessageMentions");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.MessageId, x.MentionedUserId }).IsUnique();

        // "Everything that mentions me" is the query people run most.
        builder.HasIndex(x => x.MentionedUserId);

        builder.HasOne(x => x.MentionedUser)
            .WithMany()
            .HasForeignKey(x => x.MentionedUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
