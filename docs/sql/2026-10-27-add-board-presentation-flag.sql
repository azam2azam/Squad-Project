-- ===========================================================================
--  Squad Status Board — "include this board in Present mode"
--  Migration: 20261027144255_AddBoardPresentationFlag
--  Generated: 2026-10-27
-- ===========================================================================
--
--  WHAT THIS DOES — one column, one table
--    Boards  + IncludeInPresentation  bit NOT NULL  DEFAULT 1
--
--  Present mode now walks one programme at a time and skips any board whose
--  flag is 0. The default is 1, so every board already in the table keeps
--  appearing exactly as it does today — nothing to backfill.
--
--  SAFETY
--    * Additive. No column is dropped, no row is changed.
--    * NOT NULL with a default, so the ALTER does not rewrite rows as NULL.
--    * Idempotent: guarded on __EFMigrationsHistory, safe to re-run.
--    * One transaction — it all applies, or none of it does.
--    * Records the migration id at the end, so the application will not
--      re-apply it on start-up.
--
--  BEFORE YOU RUN IT
--    1. Take a backup.
--    2. Check the target database is at 20260921092458_AddTeamMessaging:
--         SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;
--       If it is further behind, run all-migrations-idempotent.sql instead.
--    3. Run it against the application database (default: SquadStatusBoard).
--
--  ROLLBACK, if it is ever needed
--    ALTER TABLE [Boards] DROP CONSTRAINT [DF_Boards_IncludeInPresentation];
--      -- the default constraint's generated name may differ; find it with:
--      -- SELECT name FROM sys.default_constraints
--      --   WHERE parent_object_id = OBJECT_ID('Boards');
--    ALTER TABLE [Boards] DROP COLUMN [IncludeInPresentation];
--    DELETE FROM [__EFMigrationsHistory]
--      WHERE [MigrationId] = N'20261027144255_AddBoardPresentationFlag';
-- ===========================================================================

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027144255_AddBoardPresentationFlag'
)
BEGIN
    ALTER TABLE [Boards] ADD [IncludeInPresentation] bit NOT NULL DEFAULT CAST(1 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027144255_AddBoardPresentationFlag'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027144255_AddBoardPresentationFlag', N'8.0.11');
END;
GO

COMMIT;
GO

