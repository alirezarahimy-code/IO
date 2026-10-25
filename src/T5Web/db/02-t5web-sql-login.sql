/* =====================================================================
   T5Web Admin Portal — least-privilege SQL login
   ---------------------------------------------------------------------
   The portal runs under its own login. It is NOT db_owner: the engine owns
   the schema, and an administrator clicking the wrong button must not be
   able to drop a table.

   The login may:
     * read every table (the admin screens show engine data);
     * write only the four engine tables an administrator is responsible for
       (UserInfo, Device, Workflow, Gate) plus its own two tables.

   Anything else — DDL, truncating [Record], editing [Request] — is refused by
   SQL Server, not by an if-statement in C#.

   Change the password before running in production:
     ALTER LOGIN [t5web_admin] WITH PASSWORD = N'<strong password>';
   ===================================================================== */

SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N't5web_admin')
BEGIN
    CREATE LOGIN [t5web_admin]
        WITH PASSWORD = N'ChangeMe!T5Web#2026',
             CHECK_POLICY = ON,
             CHECK_EXPIRATION = OFF,
             DEFAULT_DATABASE = [ReportT5V3];
END
GO

USE [ReportT5V3];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N't5web_admin')
BEGIN
    CREATE USER [t5web_admin] FOR LOGIN [t5web_admin] WITH DEFAULT_SCHEMA = dbo;
END
GO

GRANT SELECT ON SCHEMA::dbo TO [t5web_admin];
GO

GRANT INSERT, UPDATE ON OBJECT::dbo.UserInfo   TO [t5web_admin];
GRANT INSERT, UPDATE ON OBJECT::dbo.Device     TO [t5web_admin];
GRANT INSERT, UPDATE ON OBJECT::dbo.Workflow   TO [t5web_admin];
GRANT INSERT, UPDATE ON OBJECT::dbo.Gate       TO [t5web_admin];
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.T5WebAccess TO [t5web_admin];
GRANT SELECT, INSERT           ON OBJECT::dbo.T5WebAudit  TO [t5web_admin];
GO

/* The audit trail is append-only: even the portal login cannot rewrite history. */
DENY UPDATE, DELETE ON OBJECT::dbo.T5WebAudit TO [t5web_admin];
DENY DELETE            ON OBJECT::dbo.T5WebAccess TO [t5web_admin];
GO

PRINT N't5web_admin created with least-privilege grants. Change its password now.';
GO
