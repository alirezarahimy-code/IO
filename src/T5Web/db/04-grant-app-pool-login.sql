/* =====================================================================
   T5Web Admin Portal — let the IIS app pool reach the two catalogs
   ---------------------------------------------------------------------
   The portal connects with `integrated security=True`, so SQL Server sees
   the *app pool's* Windows identity (IIS APPPOOL\T5WebAdmin by default),
   not the operator and not t5web_admin. Without a login for that identity
   every screen answers 500 with

       Login failed for user 'IIS APPPOOL\T5WebAdmin'.

   This script does not repeat the privilege list. It adds the identity to the
   role that db\02-t5web-sql-login.sql already filled ([t5web_app] in both
   catalogs), so the pool and the least-privilege login read exactly the same
   objects by construction. A second copy of the list is how one screen becomes
   readable for one identity and forbidden for the other.

   Run it on the SQL host, as an administrator, after db\01 and db\02:

       sqlcmd -S SRV-SQL01\T5 -E -b -i db\04-grant-app-pool-login.sql \
              -v AppPoolLogin="IIS APPPOOL\T5WebAdmin"

   The identity is deliberately NOT defaulted: the pool name is a deployment
   decision (3-deploy-portal.ps1 -PoolName), and a script that guesses it would
   grant the wrong Windows account - or, worse, look like it worked.
   ===================================================================== */

SET NOCOUNT ON;
GO

IF N'$(AppPoolLogin)' = N''
BEGIN
    RAISERROR (N'Pass -v AppPoolLogin="IIS APPPOOL\\T5WebAdmin" (the pool name the site runs as).', 16, 1);
    SET NOEXEC ON;
END
GO

/* A Windows login, not a SQL login: no password to store, rotate or leak. */
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(AppPoolLogin)')
BEGIN
    CREATE LOGIN [$(AppPoolLogin)] FROM WINDOWS WITH DEFAULT_DATABASE = [ReportT5V3];
END
GO

GRANT CONNECT SQL TO [$(AppPoolLogin)];
GO

USE [ReportT5V3];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppPoolLogin)')
BEGIN
    CREATE USER [$(AppPoolLogin)] FOR LOGIN [$(AppPoolLogin)] WITH DEFAULT_SCHEMA = dbo;
END
GO

IF NOT EXISTS (SELECT 1
               FROM sys.database_role_members rm
               JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
               JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
               WHERE r.name = N't5web_app' AND m.name = N'$(AppPoolLogin)')
BEGIN
    EXEC (N'ALTER ROLE [t5web_app] ADD MEMBER [$(AppPoolLogin)]');
END
GO

USE [Personnel];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppPoolLogin)')
BEGIN
    CREATE USER [$(AppPoolLogin)] FOR LOGIN [$(AppPoolLogin)] WITH DEFAULT_SCHEMA = dbo;
END
GO

IF NOT EXISTS (SELECT 1
               FROM sys.database_role_members rm
               JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
               JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
               WHERE r.name = N't5web_app' AND m.name = N'$(AppPoolLogin)')
BEGIN
    EXEC (N'ALTER ROLE [t5web_app] ADD MEMBER [$(AppPoolLogin)]');
END
GO

PRINT N'The app pool identity can now read and write exactly what t5web_admin can.';
GO
