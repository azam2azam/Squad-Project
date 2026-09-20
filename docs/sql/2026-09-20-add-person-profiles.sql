-- ===========================================================================
--  Squad Status Board — profile fields for the team directory
--  Migration: 20260920085547_AddPersonProfiles
--  Generated: 2026-09-20
-- ===========================================================================
--
--  WHAT THIS DOES
--    People              + About     nvarchar(2000) NULL   the short description
--                        + Headline  nvarchar(120)  NULL   the line under the name
--    PersonSkills        new table   one row per skill, indexed on Name so the
--                                    directory can answer "who here knows X"
--    PersonAchievements  new table   title, optional detail, optional date
--    PersonPhotos        new table   the picture as varbinary(max), one row per
--                                    person, kept out of People so listing the
--                                    roster never drags the image bytes with it
--    BoardAuditEntries     Source    nvarchar(max) -> nvarchar(50)  (tidy-up from
--                                    the Telegram change; the column only ever
--                                    holds "Telegram", so nothing can truncate)
--
--  SAFETY
--    * Additive. No existing column is dropped and no row is deleted.
--    * Idempotent: every statement is guarded on __EFMigrationsHistory, so
--      running it twice is harmless.
--    * Wrapped in one transaction — it all applies, or none of it does.
--    * It records the migration id in __EFMigrationsHistory at the end, so the
--      application will not try to apply it again on start-up.
--
--  BEFORE YOU RUN IT
--    1. Take a backup. This is additive, but it is still a schema change.
--    2. Check the target database is at 20260909225646_AddTelegramIntegration:
--         SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;
--       If it is further behind, run all-migrations-idempotent.sql instead —
--       that one applies whatever is missing, from any starting point.
--    3. Run it against the application database (default: SquadStatusBoard).
--
--  ROLLBACK, if it is ever needed
--    DROP TABLE [PersonAchievements];
--    DROP TABLE [PersonPhotos];
--    DROP TABLE [PersonSkills];
--    ALTER TABLE [People] DROP COLUMN [About];
--    ALTER TABLE [People] DROP COLUMN [Headline];
--    DELETE FROM [__EFMigrationsHistory]
--      WHERE [MigrationId] = N'20260920085547_AddPersonProfiles';
-- ===========================================================================

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920085547_AddPersonProfiles'
)
BEGIN
    ALTER TABLE [People] ADD [About] nvarchar(2000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920085547_AddPersonProfiles'
)
BEGIN
    ALTER TABLE [People] ADD [Headline] nvarchar(120) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920085547_AddPersonProfiles'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BoardAuditEntries]') AND [c].[name] = N'Source');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [BoardAuditEntries] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [BoardAuditEntries] ALTER COLUMN [Source] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920085547_AddPersonProfiles'
)
BEGIN
    CREATE TABLE [PersonAchievements] (
        [Id] uniqueidentifier NOT NULL,
        [PersonId] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Detail] nvarchar(1000) NULL,
        [AchievedOn] date NULL,
        [OrderIndex] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_PersonAchievements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PersonAchievements_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920085547_AddPersonProfiles'
)
BEGIN
    CREATE TABLE [PersonPhotos] (
        [Id] uniqueidentifier NOT NULL,
        [PersonId] uniqueidentifier NOT NULL,
        [Bytes] varbinary(max) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [ByteCount] int NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_PersonPhotos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PersonPhotos_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920085547_AddPersonProfiles'
)
BEGIN
    CREATE TABLE [PersonSkills] (
        [Id] uniqueidentifier NOT NULL,
        [PersonId] uniqueidentifier NOT NULL,
        [Name] nvarchar(60) NOT NULL,
        [OrderIndex] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_PersonSkills] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PersonSkills_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920085547_AddPersonProfiles'
)
BEGIN
    CREATE INDEX [IX_PersonAchievements_PersonId_OrderIndex] ON [PersonAchievements] ([PersonId], [OrderIndex]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920085547_AddPersonProfiles'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PersonPhotos_PersonId] ON [PersonPhotos] ([PersonId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920085547_AddPersonProfiles'
)
BEGIN
    CREATE INDEX [IX_PersonSkills_Name] ON [PersonSkills] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920085547_AddPersonProfiles'
)
BEGIN
    CREATE INDEX [IX_PersonSkills_PersonId_OrderIndex] ON [PersonSkills] ([PersonId], [OrderIndex]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260920085547_AddPersonProfiles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260920085547_AddPersonProfiles', N'8.0.11');
END;
GO

COMMIT;
GO

