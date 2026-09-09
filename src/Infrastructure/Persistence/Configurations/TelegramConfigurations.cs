using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class TelegramSettingsConfiguration : IEntityTypeConfiguration<TelegramSettings>
{
    public void Configure(EntityTypeBuilder<TelegramSettings> builder)
    {
        builder.ToTable("TelegramSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.BaseUrl).HasMaxLength(300);
        // Ciphertext is considerably longer than the token it protects.
        builder.Property(s => s.EncryptedBotToken).HasMaxLength(2000);
        builder.Property(s => s.TokenHint).HasMaxLength(40);
        builder.Property(s => s.BotUsername).HasMaxLength(100);
        builder.Property(s => s.AllowedChatIds).HasMaxLength(500);
        builder.Property(s => s.UpdatedBy).HasMaxLength(200);
        builder.Property(s => s.LastPollResult).HasMaxLength(500);
    }
}

public sealed class TelegramLinkConfiguration : IEntityTypeConfiguration<TelegramLink>
{
    public void Configure(EntityTypeBuilder<TelegramLink> builder)
    {
        builder.ToTable("TelegramLinks");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.DisplayName).HasMaxLength(200);
        builder.Property(l => l.TelegramUsername).HasMaxLength(100);

        // One Telegram account is one person. Enforced here rather than only in the
        // handler, because "who sent this" must never have two answers.
        builder.HasIndex(l => l.TelegramUserId).IsUnique();

        builder.HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TelegramEnrolmentConfiguration : IEntityTypeConfiguration<TelegramEnrolment>
{
    public void Configure(EntityTypeBuilder<TelegramEnrolment> builder)
    {
        builder.ToTable("TelegramEnrolments");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Code).HasMaxLength(20);
        builder.Property(e => e.IssuedBy).HasMaxLength(200);

        builder.HasIndex(e => e.Code).IsUnique();

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TelegramMessageConfiguration : IEntityTypeConfiguration<TelegramMessage>
{
    public void Configure(EntityTypeBuilder<TelegramMessage> builder)
    {
        builder.ToTable("TelegramMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.SenderName).HasMaxLength(200);
        builder.Property(m => m.Text).HasMaxLength(2000);
        builder.Property(m => m.Detail).HasMaxLength(1000);

        // Telegram replays anything unacknowledged, and a crash between applying and
        // acknowledging is exactly when that happens — so the same update must not be able
        // to land twice.
        builder.HasIndex(m => m.UpdateId).IsUnique();
        builder.HasIndex(m => m.ReceivedAt);

        // No navigation to Board on purpose: the log outlives boards, and a deleted board
        // must not take the record of what was said about it.
        builder.Property(m => m.BoardId);
    }
}
