-- ===========================================================================
--  Squad Status Board — every migration to date, idempotent
--  Generated: 2026-09-20  (through 20260920085547_AddPersonProfiles)
-- ===========================================================================
--  Use this when a database is more than one migration behind, or when you are
--  not sure what it has. Each statement is guarded on __EFMigrationsHistory, so
--  it applies only what is missing and is safe to re-run.
--
--  On an empty database it builds the whole schema from nothing.
--  Take a backup first.
-- ===========================================================================

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    CREATE TABLE [BoardAuditEntries] (
        [Id] uniqueidentifier NOT NULL,
        [BoardId] uniqueidentifier NOT NULL,
        [Field] nvarchar(100) NOT NULL,
        [OldValue] nvarchar(1000) NULL,
        [NewValue] nvarchar(1000) NULL,
        [ChangedBy] nvarchar(200) NOT NULL,
        [ChangedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_BoardAuditEntries] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    CREATE TABLE [Boards] (
        [Id] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Product] nvarchar(100) NOT NULL,
        [SquadName] nvarchar(100) NOT NULL,
        [Sprint] nvarchar(100) NULL,
        [Status] int NOT NULL,
        [ProgressPercent] int NOT NULL,
        [BlockerNote] nvarchar(1000) NULL,
        [Velocity] float NULL,
        [TargetDate] date NULL,
        [JiraProjectKey] nvarchar(50) NULL,
        [JiraBoardId] nvarchar(50) NULL,
        [CreatedBy] nvarchar(200) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [OrderIndex] int NOT NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_Boards] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    CREATE TABLE [People] (
        [Id] uniqueidentifier NOT NULL,
        [FullName] nvarchar(200) NOT NULL,
        [DefaultRole] int NOT NULL,
        [DefaultDetail] nvarchar(200) NULL,
        [Email] nvarchar(320) NULL,
        [AvatarColorOverride] nvarchar(9) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_People] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    CREATE TABLE [SquadMembers] (
        [Id] uniqueidentifier NOT NULL,
        [BoardId] uniqueidentifier NOT NULL,
        [PersonId] uniqueidentifier NOT NULL,
        [Role] int NOT NULL,
        [Detail] nvarchar(200) NULL,
        [AllocationPercent] int NULL,
        [OrderIndex] int NOT NULL,
        CONSTRAINT [PK_SquadMembers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SquadMembers_Boards_BoardId] FOREIGN KEY ([BoardId]) REFERENCES [Boards] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SquadMembers_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BoardAuditEntries_BoardId_ChangedAt] ON [BoardAuditEntries] ([BoardId], [ChangedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Boards_IsDeleted] ON [Boards] ([IsDeleted]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Boards_OrderIndex] ON [Boards] ([OrderIndex]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_People_FullName] ON [People] ([FullName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_People_IsActive] ON [People] ([IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SquadMembers_BoardId_OrderIndex] ON [SquadMembers] ([BoardId], [OrderIndex]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SquadMembers_BoardId_PersonId] ON [SquadMembers] ([BoardId], [PersonId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SquadMembers_PersonId] ON [SquadMembers] ([PersonId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830034932_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260830034932_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830161537_AddUsersAndBoardOwner'
)
BEGIN
    ALTER TABLE [Boards] ADD [OwnerId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830161537_AddUsersAndBoardOwner'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [Email] nvarchar(320) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [Role] int NOT NULL,
        [PasswordHash] nvarchar(500) NULL,
        [ExternalSubject] nvarchar(200) NULL,
        [PersonId] uniqueidentifier NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [LastLoginAt] datetimeoffset NULL,
        [RefreshTokenHash] nvarchar(200) NULL,
        [RefreshTokenExpiresAt] datetimeoffset NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830161537_AddUsersAndBoardOwner'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830161537_AddUsersAndBoardOwner'
)
BEGIN
    CREATE INDEX [IX_Users_ExternalSubject] ON [Users] ([ExternalSubject]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830161537_AddUsersAndBoardOwner'
)
BEGIN
    CREATE INDEX [IX_Users_RefreshTokenHash] ON [Users] ([RefreshTokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830161537_AddUsersAndBoardOwner'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260830161537_AddUsersAndBoardOwner', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830192414_AddBoardRisk'
)
BEGIN
    ALTER TABLE [Boards] ADD [RiskLevel] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830192414_AddBoardRisk'
)
BEGIN
    ALTER TABLE [Boards] ADD [RiskNote] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830192414_AddBoardRisk'
)
BEGIN
    CREATE INDEX [IX_Boards_RiskLevel] ON [Boards] ([RiskLevel]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830192414_AddBoardRisk'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260830192414_AddBoardRisk', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904191206_AddJiraSettings'
)
BEGIN
    CREATE TABLE [JiraSettings] (
        [Id] uniqueidentifier NOT NULL,
        [BaseUrl] nvarchar(300) NOT NULL,
        [Email] nvarchar(320) NOT NULL,
        [EncryptedApiToken] nvarchar(2000) NOT NULL,
        [TokenHint] nvarchar(40) NOT NULL,
        [Enabled] bit NOT NULL,
        [AutoApply] bit NOT NULL,
        [SyncIntervalMinutes] int NOT NULL,
        [UpdatedBy] nvarchar(200) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [LastSyncAt] datetimeoffset NULL,
        [LastSyncResult] nvarchar(500) NULL,
        CONSTRAINT [PK_JiraSettings] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904191206_AddJiraSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260904191206_AddJiraSettings', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905155859_AddSquadRoles'
)
BEGIN
    CREATE TABLE [SquadRoles] (
        [Value] int NOT NULL,
        [Name] nvarchar(60) NOT NULL,
        [Label] nvarchar(80) NOT NULL,
        [PluralLabel] nvarchar(80) NOT NULL,
        [Color] nvarchar(7) NOT NULL,
        [OrderIndex] int NOT NULL,
        [IsBuiltIn] bit NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_SquadRoles] PRIMARY KEY ([Value])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905155859_AddSquadRoles'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Value', N'Color', N'IsActive', N'IsBuiltIn', N'Label', N'Name', N'OrderIndex', N'PluralLabel') AND [object_id] = OBJECT_ID(N'[SquadRoles]'))
        SET IDENTITY_INSERT [SquadRoles] ON;
    EXEC(N'INSERT INTO [SquadRoles] ([Value], [Color], [IsActive], [IsBuiltIn], [Label], [Name], [OrderIndex], [PluralLabel])
    VALUES (0, N''#2DD4BF'', CAST(1 AS bit), CAST(1 AS bit), N''Product Owner'', N''ProductOwner'', 0, N''Product Owners''),
    (1, N''#A78BFA'', CAST(1 AS bit), CAST(1 AS bit), N''Tech Lead'', N''TechLead'', 1, N''Tech Leads''),
    (2, N''#6366F1'', CAST(1 AS bit), CAST(1 AS bit), N''Developer'', N''Developer'', 2, N''Developers''),
    (3, N''#F59E0B'', CAST(1 AS bit), CAST(1 AS bit), N''QA Engineer'', N''QaEngineer'', 3, N''QA Engineers''),
    (4, N''#EC4899'', CAST(1 AS bit), CAST(1 AS bit), N''UI/UX Designer'', N''UxDesigner'', 4, N''UI/UX Designers''),
    (5, N''#38BDF8'', CAST(1 AS bit), CAST(1 AS bit), N''Business Analyst'', N''BusinessAnalyst'', 5, N''Business Analysts''),
    (6, N''#10B981'', CAST(1 AS bit), CAST(1 AS bit), N''DevOps'', N''DevOps'', 6, N''DevOps'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Value', N'Color', N'IsActive', N'IsBuiltIn', N'Label', N'Name', N'OrderIndex', N'PluralLabel') AND [object_id] = OBJECT_ID(N'[SquadRoles]'))
        SET IDENTITY_INSERT [SquadRoles] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905155859_AddSquadRoles'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SquadRoles_Name] ON [SquadRoles] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905155859_AddSquadRoles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260905155859_AddSquadRoles', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907232119_AddBoardCategories'
)
BEGIN
    ALTER TABLE [Boards] ADD [CategoryId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907232119_AddBoardCategories'
)
BEGIN
    CREATE TABLE [BoardCategories] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [Description] nvarchar(400) NULL,
        [Color] nvarchar(7) NOT NULL,
        [OrderIndex] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_BoardCategories] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907232119_AddBoardCategories'
)
BEGIN
    CREATE INDEX [IX_Boards_CategoryId] ON [Boards] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907232119_AddBoardCategories'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BoardCategories_Name] ON [BoardCategories] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907232119_AddBoardCategories'
)
BEGIN
    ALTER TABLE [Boards] ADD CONSTRAINT [FK_Boards_BoardCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [BoardCategories] ([Id]) ON DELETE SET NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907232119_AddBoardCategories'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260907232119_AddBoardCategories', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908124128_AddSmartsheetIntegration'
)
BEGIN
    ALTER TABLE [Boards] ADD [SmartsheetSheetId] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908124128_AddSmartsheetIntegration'
)
BEGIN
    CREATE TABLE [SmartsheetSettings] (
        [Id] uniqueidentifier NOT NULL,
        [BaseUrl] nvarchar(300) NOT NULL,
        [EncryptedAccessToken] nvarchar(2000) NOT NULL,
        [TokenHint] nvarchar(40) NOT NULL,
        [Enabled] bit NOT NULL,
        [AutoApply] bit NOT NULL,
        [SyncIntervalMinutes] int NOT NULL,
        [ProgressColumn] nvarchar(200) NOT NULL,
        [StatusColumn] nvarchar(200) NOT NULL,
        [UpdatedBy] nvarchar(200) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [LastSyncAt] datetimeoffset NULL,
        [LastSyncResult] nvarchar(500) NULL,
        CONSTRAINT [PK_SmartsheetSettings] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908124128_AddSmartsheetIntegration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260908124128_AddSmartsheetIntegration', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909112617_AddStaffScheduling'
)
BEGIN
    ALTER TABLE [SquadMembers] ADD [EndsOn] date NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909112617_AddStaffScheduling'
)
BEGIN
    ALTER TABLE [SquadMembers] ADD [StartsOn] date NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909112617_AddStaffScheduling'
)
BEGIN
    CREATE TABLE [PersonAvailability] (
        [Id] uniqueidentifier NOT NULL,
        [PersonId] uniqueidentifier NOT NULL,
        [FromDate] date NOT NULL,
        [ToDate] date NOT NULL,
        [Kind] int NOT NULL,
        [CapacityPercent] int NOT NULL,
        [Note] nvarchar(400) NULL,
        [RecordedBy] nvarchar(200) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_PersonAvailability] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PersonAvailability_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909112617_AddStaffScheduling'
)
BEGIN
    CREATE TABLE [WorkItems] (
        [Id] uniqueidentifier NOT NULL,
        [BoardId] uniqueidentifier NOT NULL,
        [PersonId] uniqueidentifier NULL,
        [Title] nvarchar(300) NOT NULL,
        [Detail] nvarchar(2000) NULL,
        [Status] int NOT NULL,
        [PlannedStart] date NULL,
        [PlannedEnd] date NULL,
        [CompletedOn] date NULL,
        [EffortHours] float NULL,
        [CreatedBy] nvarchar(200) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_WorkItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkItems_Boards_BoardId] FOREIGN KEY ([BoardId]) REFERENCES [Boards] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_WorkItems_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909112617_AddStaffScheduling'
)
BEGIN
    CREATE INDEX [IX_PersonAvailability_PersonId_FromDate_ToDate] ON [PersonAvailability] ([PersonId], [FromDate], [ToDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909112617_AddStaffScheduling'
)
BEGIN
    CREATE INDEX [IX_WorkItems_BoardId] ON [WorkItems] ([BoardId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909112617_AddStaffScheduling'
)
BEGIN
    CREATE INDEX [IX_WorkItems_PersonId_Status] ON [WorkItems] ([PersonId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909112617_AddStaffScheduling'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260909112617_AddStaffScheduling', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    ALTER TABLE [Boards] ADD [Code] nvarchar(12) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    ALTER TABLE [BoardAuditEntries] ADD [Source] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    CREATE TABLE [TelegramEnrolments] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [IssuedBy] nvarchar(200) NOT NULL,
        [IssuedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [RedeemedAt] datetimeoffset NULL,
        [RedeemedByTelegramUserId] bigint NULL,
        CONSTRAINT [PK_TelegramEnrolments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TelegramEnrolments_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    CREATE TABLE [TelegramLinks] (
        [Id] uniqueidentifier NOT NULL,
        [TelegramUserId] bigint NOT NULL,
        [TelegramUsername] nvarchar(100) NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [LinkedAt] datetimeoffset NOT NULL,
        [LastSeenAt] datetimeoffset NULL,
        [IsActive] bit NOT NULL,
        [RevokedAt] datetimeoffset NULL,
        CONSTRAINT [PK_TelegramLinks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TelegramLinks_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    CREATE TABLE [TelegramMessages] (
        [Id] uniqueidentifier NOT NULL,
        [UpdateId] bigint NOT NULL,
        [ChatId] bigint NOT NULL,
        [SenderTelegramUserId] bigint NOT NULL,
        [SenderName] nvarchar(200) NOT NULL,
        [Text] nvarchar(2000) NOT NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        [Outcome] int NOT NULL,
        [Detail] nvarchar(1000) NULL,
        [BoardId] uniqueidentifier NULL,
        [UserId] uniqueidentifier NULL,
        CONSTRAINT [PK_TelegramMessages] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    CREATE TABLE [TelegramSettings] (
        [Id] uniqueidentifier NOT NULL,
        [BaseUrl] nvarchar(300) NOT NULL,
        [EncryptedBotToken] nvarchar(2000) NOT NULL,
        [TokenHint] nvarchar(40) NOT NULL,
        [BotUsername] nvarchar(100) NULL,
        [Enabled] bit NOT NULL,
        [LastUpdateId] bigint NOT NULL,
        [AllowedChatIds] nvarchar(500) NULL,
        [ReplyToUnknownSenders] bit NOT NULL,
        [UpdatedBy] nvarchar(200) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [LastPollAt] datetimeoffset NULL,
        [LastPollResult] nvarchar(500) NULL,
        CONSTRAINT [PK_TelegramSettings] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Boards_Code] ON [Boards] ([Code]) WHERE [Code] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TelegramEnrolments_Code] ON [TelegramEnrolments] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    CREATE INDEX [IX_TelegramEnrolments_UserId] ON [TelegramEnrolments] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TelegramLinks_TelegramUserId] ON [TelegramLinks] ([TelegramUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    CREATE INDEX [IX_TelegramLinks_UserId] ON [TelegramLinks] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    CREATE INDEX [IX_TelegramMessages_ReceivedAt] ON [TelegramMessages] ([ReceivedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TelegramMessages_UpdateId] ON [TelegramMessages] ([UpdateId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909225646_AddTelegramIntegration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260909225646_AddTelegramIntegration', N'8.0.11');
END;
GO

COMMIT;
GO

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

