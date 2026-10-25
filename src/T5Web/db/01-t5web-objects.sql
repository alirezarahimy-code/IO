/* =====================================================================
   T5Web Admin Portal — objects owned by the portal itself
   ---------------------------------------------------------------------
   Run order: 01 (this file) -> 02-t5web-sql-login.sql -> verify.sql

   Scope
   -----
   The portal is an *administrator* of the shared T5 database, so it may read
   every engine table. It must NOT add columns to engine tables (docs/02, R1):
   the engine upgrades them and a custom column is silently dropped.

   Only two tables are created here, both portal-owned:

     [T5WebAccess]  the portal's own permission matrix
                    (feature enum -> Deny / ReadOnly / Allow)
     [T5WebAudit]   append-only, hash-chained audit trail

   Both are idempotent: running this script twice is a no-op.
   ===================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.T5WebAccess', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T5WebAccess
    (
        BadgeNumber bigint      NOT NULL,
        Feature     int         NOT NULL,
        Level       tinyint     NOT NULL CONSTRAINT DF_T5WebAccess_Level DEFAULT (0),
        ChangedBy   nvarchar(100) NOT NULL CONSTRAINT DF_T5WebAccess_ChangedBy DEFAULT (N'system'),
        ChangedUtc  datetime2(0)  NOT NULL CONSTRAINT DF_T5WebAccess_ChangedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_T5WebAccess PRIMARY KEY CLUSTERED (BadgeNumber, Feature),
        CONSTRAINT CK_T5WebAccess_Level CHECK (Level IN (0, 1, 2))
    );
END
GO

IF OBJECT_ID(N'dbo.T5WebAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T5WebAudit
    (
        ID           bigint        IDENTITY(1,1) NOT NULL,
        UtcTime      datetime2(3)  NOT NULL,
        Actor        nvarchar(100) NOT NULL,
        Feature      int           NOT NULL,
        [Action]     nvarchar(100) NOT NULL,
        [Target]     nvarchar(200) NOT NULL,
        Detail       nvarchar(1000) NULL,
        PreviousHash char(64)      NOT NULL,
        [Hash]       char(64)      NOT NULL,
        CONSTRAINT PK_T5WebAudit PRIMARY KEY CLUSTERED (ID)
    );

    -- The audit page always reads the newest rows; the hash chain reads the tail.
    CREATE NONCLUSTERED INDEX IX_T5WebAudit_UtcTime ON dbo.T5WebAudit (UtcTime DESC);
END
GO

/* The engine's own [UserInfo].[WebUser] decides who may sign in at all, so the
   portal never needs a separate identity table. Guard against an operator
   granting portal access to somebody who cannot authenticate. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_T5WebAccess_Feature' AND object_id = OBJECT_ID(N'dbo.T5WebAccess'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_T5WebAccess_Feature ON dbo.T5WebAccess (Feature, Level) INCLUDE (BadgeNumber);
END
GO

PRINT N'T5Web portal objects are in place.';
GO
