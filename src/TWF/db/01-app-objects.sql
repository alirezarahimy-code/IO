/* ==========================================================================
   ARIA (آریا) — Application-owned database objects
   --------------------------------------------------------------------------
   Target : Microsoft SQL Server (T-SQL)
   Catalog: ReportT5V3   (the T5 core database)
   Scope  : ONLY objects that belong to the web application itself.
            Every other table in ReportT5V3 / Personnel is owned by T5V3
            and must NOT be altered by this script.

   Idempotent: safe to run repeatedly on an existing production database.
   ========================================================================== */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

USE [ReportT5V3];
GO

/* --------------------------------------------------------------------------
   [PassCheckProps] — password change history
   Used by MyTime3.Controllers.ChangePassController and
   TWF.Infrastructure.Data.TwfDataRepository to enforce:
     * "password must be changed on first login"  (row exists)
     * "password must be rotated every N months"  (LastChangeDate check)

   Column layout is kept identical to the legacy TWF 4.28 contract
   ([ID], [Badge], [LastChangeDate]) so existing customer databases
   keep working without a migration.
   -------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.PassCheckProps', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PassCheckProps]
    (
        [ID]             INT      IDENTITY(1,1) NOT NULL,
        [Badge]          BIGINT   NOT NULL,
        [LastChangeDate] DATETIME NOT NULL,
        CONSTRAINT [PK_PassCheckProps] PRIMARY KEY CLUSTERED ([ID] ASC)
    );

    PRINT 'Created dbo.PassCheckProps';
END
ELSE
BEGIN
    PRINT 'dbo.PassCheckProps already exists - left untouched';

    /* Defensive: report (do not alter) if an older/foreign layout is detected. */
    IF COL_LENGTH(N'dbo.PassCheckProps', N'Badge') IS NULL
        PRINT 'WARNING: column [Badge] is missing - password rotation will not work.';
    IF COL_LENGTH(N'dbo.PassCheckProps', N'LastChangeDate') IS NULL
        PRINT 'WARNING: column [LastChangeDate] is missing - password rotation will not work.';
END
GO

/* One row per badge: guarantees the checker never sees duplicate history. */
IF OBJECT_ID(N'dbo.PassCheckProps', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'UX_PassCheckProps_Badge'
                     AND object_id = OBJECT_ID(N'dbo.PassCheckProps'))
BEGIN
    /* Only create the unique index when the existing data allows it. */
    IF NOT EXISTS (SELECT 1 FROM [dbo].[PassCheckProps]
                   GROUP BY [Badge] HAVING COUNT(*) > 1)
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX [UX_PassCheckProps_Badge]
            ON [dbo].[PassCheckProps] ([Badge] ASC);
        PRINT 'Created index UX_PassCheckProps_Badge';
    END
    ELSE
    BEGIN
        PRINT 'Skipped UX_PassCheckProps_Badge: duplicate [Badge] values exist.';
    END
END
GO

PRINT 'ARIA application-owned objects are ready.';
GO
