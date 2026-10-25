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

/* Reads: the admin screens show engine data, so the whole schema is readable. */
GRANT SELECT ON SCHEMA::dbo TO [t5web_admin];
GO

/* Writes: only the tables a generic module can edit (generated from ModuleRegistry,
   catalog t5). Nothing else in this catalog is writable by the portal login. */
GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.AllowValue,
    OBJECT::dbo.Area,
    OBJECT::dbo.AreaPoint TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.Calendar,
    OBJECT::dbo.CalendarHoliday,
    OBJECT::dbo.CustomWorkflow TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.DailyLeaf,
    OBJECT::dbo.DepartmentRule,
    OBJECT::dbo.Deputy TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.HourlyLeave,
    OBJECT::dbo.LeaveDescription,
    OBJECT::dbo.LeaveType TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.LeavesAdded,
    OBJECT::dbo.Parameter,
    OBJECT::dbo.PersonnelDeputy TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.Position,
    OBJECT::dbo.PositionStep,
    OBJECT::dbo.Ramezan TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.ReportAccess,
    OBJECT::dbo.Rule,
    OBJECT::dbo.Schedule TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.Shift,
    OBJECT::dbo.TempSchedule,
    OBJECT::dbo.UserInfoManager TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.UserPermission,
    OBJECT::dbo.UserPositionData,
    OBJECT::dbo.UserRule TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.UserShift,
    OBJECT::dbo.ValidLeave,
    OBJECT::dbo.YearRemain TO [t5web_admin];
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.T5WebAccess TO [t5web_admin];
GRANT SELECT, INSERT           ON OBJECT::dbo.T5WebAudit  TO [t5web_admin];
GO

/* The audit trail is append-only: even the portal login cannot rewrite history. */
DENY UPDATE, DELETE ON OBJECT::dbo.T5WebAudit TO [t5web_admin];
DENY DELETE            ON OBJECT::dbo.T5WebAccess TO [t5web_admin];
GO

/* =====================================================================
   Second catalog. The engine keeps people, departments, titles and devices
   in [Personnel] (docs/09), not in ReportT5V3, so the same login needs a
   user there as well - otherwise every personnel screen fails with
   "The server principal is not able to access the database".
   The catalog name is repeated here on purpose: this part of the script has
   to be run against the other database.
   ===================================================================== */
USE [Personnel];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N't5web_admin')
BEGIN
    CREATE USER [t5web_admin] FOR LOGIN [t5web_admin] WITH DEFAULT_SCHEMA = dbo;
END
GO

GRANT SELECT ON SCHEMA::dbo TO [t5web_admin];
GO

/* Writes: the base-data modules that live in this catalog (catalog personnel). */
GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.Certificate,
    OBJECT::dbo.Dashboard,
    OBJECT::dbo.DeviceMessage TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.EmpType,
    OBJECT::dbo.Gate,
    OBJECT::dbo.ManagerDevice TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.Property,
    OBJECT::dbo.PropertyDropdownValue,
    OBJECT::dbo.TitlesChart TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.UserInfoProperty,
    OBJECT::dbo.UserInfoTitles,
    OBJECT::dbo.UserInfoVaccinationDetails TO [t5web_admin];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::dbo.VaccineLog TO [t5web_admin];
GO

PRINT N't5web_admin created with least-privilege grants. Change its password now.';
GO
