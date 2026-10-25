/* ==========================================================================
   ARIA (آریا) — Least-privilege SQL Server login
   --------------------------------------------------------------------------
   Purpose: the portal (Aria/TWF) must NOT connect with db_owner, sa, or the
            same account the T5 engine uses. This script creates a dedicated
            login with only the rights the application actually needs.

   Current state (before this script):
       Web.config uses Integrated Security=True, i.e. the IIS application-pool
       identity, which on a default installation maps to an account with far
       more rights than needed.

   Run once, as sysadmin, on the SQL Server instance:
       sqlcmd -S . -E -i db\02-aria-sql-login.sql

   Review the @password value before running in production.
   ========================================================================== */

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* --------------------------------------------------------------------------
   Configuration — EDIT THESE
   -------------------------------------------------------------------------- */
DECLARE @loginName  SYSNAME = N'aria_app';
DECLARE @password   NVARCHAR(128) = N'Change-Me-Strong-Password-1404!';
DECLARE @t5Database SYSNAME = N'ReportT5V3';
DECLARE @hrDatabase SYSNAME = N'Personnel';

PRINT '=== ARIA least-privilege login setup ===';
PRINT '';

/* --------------------------------------------------------------------------
   1. Server login
   -------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @loginName)
BEGIN
    DECLARE @sql NVARCHAR(400) =
        N'CREATE LOGIN ' + QUOTENAME(@loginName) +
        N' WITH PASSWORD = ' + QUOTENAME(@password, '''') +
        N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = ' + QUOTENAME(@t5Database) + N';';
    EXEC sp_executesql @sql;
    PRINT 'Created server login: ' + @loginName;
END
ELSE
BEGIN
    PRINT 'Server login already exists: ' + @loginName + ' (password left unchanged)';
END

/* Explicitly NOT a sysadmin / securityadmin / dbcreator. */
IF EXISTS (SELECT 1
           FROM sys.server_role_members rm
           JOIN sys.server_principals r ON r.principal_id = rm.role_principal_id
           JOIN sys.server_principals m ON m.principal_id = rm.member_principal_id
           WHERE m.name = @loginName
             AND r.name IN (N'sysadmin', N'securityadmin', N'dbcreator', N'bulkadmin'))
BEGIN
    PRINT 'WARNING: ' + @loginName + ' holds a high-privilege server role - review immediately.';
END
ELSE
BEGIN
    PRINT 'Server-role check: clean (no sysadmin/securityadmin/dbcreator/bulkadmin).';
END
GO

/* --------------------------------------------------------------------------
   2. Database user + rights in the T5 database
   -------------------------------------------------------------------------- */
DECLARE @t5Database SYSNAME = N'ReportT5V3';
DECLARE @loginName  SYSNAME = N'aria_app';
DECLARE @sql NVARCHAR(MAX);

IF DB_ID(@t5Database) IS NULL
BEGIN
    PRINT 'ERROR: database ' + @t5Database + ' does not exist. Install T5V3 first.';
END
ELSE
BEGIN
    SET @sql = N'
USE ' + QUOTENAME(@t5Database) + N';

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''' + @loginName + N''')
BEGIN
    CREATE USER ' + QUOTENAME(@loginName) + N' FOR LOGIN ' + QUOTENAME(@loginName) + N';
    PRINT ''Created database user in ' + @t5Database + N' '';
END;

/* Baseline: connect + read/write data. NO ddl_admin, NO db_owner, NO execute-on-everything. */
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members rm
               JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
               JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
               WHERE m.name = N''' + @loginName + N''' AND r.name = N''db_datareader'')
    ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@loginName) + N';

IF NOT EXISTS (SELECT 1 FROM sys.database_role_members rm
               JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
               JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
               WHERE m.name = N''' + @loginName + N''' AND r.name = N''db_datawriter'')
    ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@loginName) + N';

/* Hard denials: the application must never change schema and must never read
   server/security metadata. Explicit DENY wins over any granted role. */
DENY ALTER ON SCHEMA::dbo TO ' + QUOTENAME(@loginName) + N';
DENY CONTROL ON SCHEMA::dbo TO ' + QUOTENAME(@loginName) + N';
DENY VIEW SERVER STATE TO ' + QUOTENAME(@loginName) + N';
DENY VIEW ANY DEFINITION TO ' + QUOTENAME(@loginName) + N';

/* Audit trail: Aria writes its own security events. */
IF OBJECT_ID(N''dbo.SystemLog'', N''U'') IS NOT NULL
    GRANT INSERT ON dbo.SystemLog TO ' + QUOTENAME(@loginName) + N';
';

    EXEC sp_executesql @sql;
    PRINT 'Applied least-privilege rights in ' + @t5Database;
END
GO

/* --------------------------------------------------------------------------
   3. Read-only access to the Personnel (org chart) database
   -------------------------------------------------------------------------- */
DECLARE @hrDatabase SYSNAME = N'Personnel';
DECLARE @loginName  SYSNAME = N'aria_app';
DECLARE @sql NVARCHAR(MAX);

IF DB_ID(@hrDatabase) IS NULL
BEGIN
    PRINT 'NOTE: database ' + @hrDatabase + ' not found - skipping (Aria needs it for the org chart).';
END
ELSE
BEGIN
    SET @sql = N'
USE ' + QUOTENAME(@hrDatabase) + N';

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''' + @loginName + N''')
BEGIN
    CREATE USER ' + QUOTENAME(@loginName) + N' FOR LOGIN ' + QUOTENAME(@loginName) + N';
    PRINT ''Created database user in ' + @hrDatabase + N' '';
END;

IF NOT EXISTS (SELECT 1 FROM sys.database_role_members rm
               JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
               JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
               WHERE m.name = N''' + @loginName + N''' AND r.name = N''db_datareader'')
    ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@loginName) + N';

DENY ALTER ON SCHEMA::dbo TO ' + QUOTENAME(@loginName) + N';
DENY CONTROL ON SCHEMA::dbo TO ' + QUOTENAME(@loginName) + N';
';

    EXEC sp_executesql @sql;
    PRINT 'Applied read-only access in ' + @hrDatabase;
END
GO

/* --------------------------------------------------------------------------
   4. Verify
   -------------------------------------------------------------------------- */
PRINT '';
PRINT '--- Verification ------------------------------------------------';

SELECT
    p.name                                   AS [Login],
    p.type_desc                              AS [Type],
    p.is_disabled                            AS [Disabled],
    p.default_database_name                  AS [DefaultDb]
FROM sys.server_principals p
WHERE p.name = N'aria_app';

SELECT
    DB_NAME() AS [CurrentDb],
    dp.name   AS [User],
    r.name    AS [Role]
FROM sys.database_principals dp
LEFT JOIN sys.database_role_members rm ON rm.member_principal_id = dp.principal_id
LEFT JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
WHERE dp.name = N'aria_app';

PRINT '';
PRINT 'Done. Now update Web.config for Aria:';
PRINT '  <add name="T5CnnStr"        connectionString="Data Source=.;Initial Catalog=ReportT5V3;User ID=aria_app;Password=***;MultipleActiveResultSets=True;" providerName="System.Data.SqlClient" />';
PRINT '  <add name="PersonnelCnnStr" connectionString="Data Source=.;Initial Catalog=Personnel;User ID=aria_app;Password=***;MultipleActiveResultSets=True;" providerName="System.Data.SqlClient" />';
PRINT '';
PRINT 'Run this file once per database, then re-run db/verify-schema.sql.';
GO
