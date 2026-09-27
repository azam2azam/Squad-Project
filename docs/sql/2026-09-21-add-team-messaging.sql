-- ===========================================================================
--  Squad Status Board — team messaging
--  Migration: 20260921092458_AddTeamMessaging
--  Generated: 2026-09-27
-- ===========================================================================
--
--  WHAT THIS DOES — four new tables, nothing existing is touched
--    Conversations         a thread, optionally attached to a board
--                          (FK to Boards, ON DELETE CASCADE)
--    ConversationMembers   who is in a conversation
--                          (FK to Conversations CASCADE, to Users RESTRICT)
--    Messages              the messages themselves, self-referencing for
--                          replies (FK to Conversations CASCADE, to Users
--                          RESTRICT, to Messages for ReplyToMessageId)
--    MessageMentions       who was @-mentioned in a message
--                          (FK to Messages CASCADE, to Users RESTRICT)
--
--  The RESTRICT foreign keys to Users are deliberate: deleting an account
--  must not silently take the messages that account wrote.
--
--  SAFETY
--    * Purely additive. No existing table, column or row is changed.
--    * Idempotent: every statement is guarded on __EFMigrationsHistory, so
--      running it twice is harmless.
--    * Wrapped in one transaction — it all applies, or none of it does.
--    * It records the migration id in __EFMigrationsHistory at the end, so the
--      application will not try to apply it again on start-up.
--
--  BEFORE YOU RUN IT
--    1. Take a backup.
--    2. Check the target database is at 20260920085547_AddPersonProfiles:
--         SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;
--       If it is further behind, run all-migrations-idempotent.sql instead —
--       that one applies whatever is missing, from any starting point.
--    3. Run it against the application database (default: SquadStatusBoard).
--
--  NOTE ON STATUS
--    This migration comes from work that is still uncommitted in the repository
--    working tree. The schema below is what that code expects today; if the
--    feature changes before it lands, regenerate this script.
--
--  ROLLBACK, if it is ever needed
--    DROP TABLE [MessageMentions];
--    DROP TABLE [Messages];
--    DROP TABLE [ConversationMembers];
--    DROP TABLE [Conversations];
--    DELETE FROM [__EFMigrationsHistory]
--      WHERE [MigrationId] = N'20260921092458_AddTeamMessaging';
-- ===========================================================================

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE TABLE [Conversations] (
        [Id] uniqueidentifier NOT NULL,
        [Kind] int NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [ScopeKey] nvarchar(120) NOT NULL,
        [BoardId] uniqueidentifier NULL,
        [SquadName] nvarchar(200) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [LastActivityAt] datetimeoffset NOT NULL,
        [LastMessagePreview] nvarchar(200) NULL,
        [LastMessageAuthor] nvarchar(200) NULL,
        CONSTRAINT [PK_Conversations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Conversations_Boards_BoardId] FOREIGN KEY ([BoardId]) REFERENCES [Boards] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE TABLE [ConversationMembers] (
        [Id] uniqueidentifier NOT NULL,
        [ConversationId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [JoinedAt] datetimeoffset NOT NULL,
        [LastReadAt] datetimeoffset NULL,
        [IsMuted] bit NOT NULL,
        CONSTRAINT [PK_ConversationMembers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ConversationMembers_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ConversationMembers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE TABLE [Messages] (
        [Id] uniqueidentifier NOT NULL,
        [ConversationId] uniqueidentifier NOT NULL,
        [AuthorUserId] uniqueidentifier NOT NULL,
        [Body] nvarchar(4000) NOT NULL,
        [ReplyToMessageId] uniqueidentifier NULL,
        [SentAt] datetimeoffset NOT NULL,
        [EditedAt] datetimeoffset NULL,
        [DeletedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Messages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Messages_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Messages_Messages_ReplyToMessageId] FOREIGN KEY ([ReplyToMessageId]) REFERENCES [Messages] ([Id]),
        CONSTRAINT [FK_Messages_Users_AuthorUserId] FOREIGN KEY ([AuthorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE TABLE [MessageMentions] (
        [Id] uniqueidentifier NOT NULL,
        [MessageId] uniqueidentifier NOT NULL,
        [MentionedUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MessageMentions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MessageMentions_Messages_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [Messages] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MessageMentions_Users_MentionedUserId] FOREIGN KEY ([MentionedUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ConversationMembers_ConversationId_UserId] ON [ConversationMembers] ([ConversationId], [UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE INDEX [IX_ConversationMembers_UserId] ON [ConversationMembers] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE INDEX [IX_Conversations_BoardId] ON [Conversations] ([BoardId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE INDEX [IX_Conversations_LastActivityAt] ON [Conversations] ([LastActivityAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Conversations_ScopeKey] ON [Conversations] ([ScopeKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE INDEX [IX_MessageMentions_MentionedUserId] ON [MessageMentions] ([MentionedUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MessageMentions_MessageId_MentionedUserId] ON [MessageMentions] ([MessageId], [MentionedUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE INDEX [IX_Messages_AuthorUserId] ON [Messages] ([AuthorUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE INDEX [IX_Messages_ConversationId_SentAt] ON [Messages] ([ConversationId], [SentAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    CREATE INDEX [IX_Messages_ReplyToMessageId] ON [Messages] ([ReplyToMessageId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260921092458_AddTeamMessaging'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260921092458_AddTeamMessaging', N'8.0.11');
END;
GO

COMMIT;
GO

