/* =====================================================================
   T5Web Admin Portal — least-privilege SQL login
   ---------------------------------------------------------------------
   The portal runs under its own login. It is NOT db_owner: the engine owns
   the schema, and an administrator clicking the wrong button must not be
   able to drop a table.

   The privileges belong to a role, [t5web_app]; the login is only a member of it.
   The app pool's Windows identity joins the same role in the same two catalogs
   (db\04-grant-app-pool-login.sql), so both see exactly the same thing.

   The role may:
     * read every table (the admin screens show engine data);
     * write the tables the screens actually write, and only with the verbs they
       use: personnel, departments and devices in [Personnel]; attendance records,
       requests, acceptances, workflows and leave entitlements in [ReportT5V3];
     * append to its own audit table and its own device queue.

   Anything else — DDL, deleting a person, deleting an attendance record, rewriting
   the audit trail — is refused by SQL Server, not by an if-statement in C#.
   tools/db-grant-gate.py reads the write statements out of T5Web.Data and fails the
   build when one of them has no grant here: a screen that renders is not a screen
   that saves (BUG-43).

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

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N't5web_app' AND type = N'R')
BEGIN
    CREATE ROLE [t5web_app] AUTHORIZATION [dbo];
END
GO

/* Every privilege belongs to the role, never to a login. That is what lets the app
   pool's Windows identity join with one line (db\04-grant-app-pool-login.sql) instead
   of a second copy of this table list - and a second copy is how one screen becomes
   readable for one identity and forbidden for the other. */

IF NOT EXISTS (SELECT 1
               FROM sys.database_role_members rm
               JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
               JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
               WHERE r.name = N't5web_app' AND m.name = N't5web_admin')
BEGIN
    EXEC (N'ALTER ROLE [t5web_app] ADD MEMBER [t5web_admin]');
END
GO

/* Reads: the admin screens show engine data, so the whole schema is readable. */
GRANT SELECT ON SCHEMA::dbo TO [t5web_app];
GO

/* Writes: only the tables a generic module can edit (generated from ModuleRegistry,
   catalog t5). One statement per table on purpose: `GRANT ... ON OBJECT::a, OBJECT::b`
   is a syntax error (Msg 102) that takes the whole batch down with it. It was written
   that way until a CI job finally ran these scripts with -b and reported "Incorrect
   syntax near ','" - the grants had never executed anywhere (BUG-36). */
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[AllowValue] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Area] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[AreaPoint] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Calendar] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[CalendarHoliday] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[CustomWorkflow] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[DailyLeaf] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[DepartmentRule] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Deputy] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[HourlyLeave] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[LeaveDescription] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[LeaveType] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[LeavesAdded] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Parameter] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[PersonnelDeputy] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Position] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[PositionStep] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Ramezan] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[ReportAccess] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Rule] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Schedule] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Shift] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[TempSchedule] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[UserInfoManager] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[UserPermission] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[UserPositionData] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[UserRule] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[UserShift] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[ValidLeave] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[YearRemain] TO [t5web_app];
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::[dbo].[T5WebAccess] TO [t5web_app];
GRANT SELECT, INSERT           ON OBJECT::[dbo].[T5WebAudit]  TO [t5web_app];
/* The device queue: the portal appends a ticket and never rewrites one. */
GRANT SELECT, INSERT           ON OBJECT::[dbo].[T5WebDeviceCommand] TO [t5web_app];
GO

/* =====================================================================
   The engine tables the attendance, request and leave screens write.
   These were missing until a gate asked the question - every one of those
   screens rendered, and every save would have failed with
     The UPDATE permission was denied on the object 'Record', database 'ReportT5V3'
   Verbs are the ones the data layer uses, not "everything": the portal edits a
   record and can never delete one, and can only insert a request acceptance.
   ===================================================================== */
GRANT INSERT, UPDATE ON OBJECT::[dbo].[Record]        TO [t5web_app];
GRANT UPDATE         ON OBJECT::[dbo].[Request]       TO [t5web_app];
GRANT INSERT         ON OBJECT::[dbo].[RequestAccept] TO [t5web_app];
GRANT INSERT, DELETE ON OBJECT::[dbo].[Workflow]      TO [t5web_app];
GRANT INSERT, DELETE ON OBJECT::[dbo].[EmpTypeLeave]  TO [t5web_app];
GO

/* Attendance and requests are the engine's history. Editing is the administrator's
   job; erasing is not, and the refusal is the database's, not the screen's. */
DENY DELETE ON OBJECT::[dbo].[Record]        TO [t5web_app];
DENY DELETE ON OBJECT::[dbo].[Request]       TO [t5web_app];
DENY DELETE ON OBJECT::[dbo].[RequestAccept] TO [t5web_app];
GO

/* The audit trail is append-only: even the portal login cannot rewrite history. */
DENY UPDATE, DELETE ON OBJECT::[dbo].[T5WebAudit] TO [t5web_app];
DENY DELETE            ON OBJECT::[dbo].[T5WebAccess] TO [t5web_app];
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

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N't5web_app' AND type = N'R')
BEGIN
    CREATE ROLE [t5web_app] AUTHORIZATION [dbo];
END
GO

/* Every privilege belongs to the role, never to a login. That is what lets the app
   pool's Windows identity join with one line (db\04-grant-app-pool-login.sql) instead
   of a second copy of this table list - and a second copy is how one screen becomes
   readable for one identity and forbidden for the other. */

IF NOT EXISTS (SELECT 1
               FROM sys.database_role_members rm
               JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
               JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
               WHERE r.name = N't5web_app' AND m.name = N't5web_admin')
BEGIN
    EXEC (N'ALTER ROLE [t5web_app] ADD MEMBER [t5web_admin]');
END
GO

GRANT SELECT ON SCHEMA::dbo TO [t5web_app];
GO

/* Writes: the base-data modules that live in this catalog (catalog personnel). */
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Certificate] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Dashboard] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[DeviceMessage] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[EmpType] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Gate] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[ManagerDevice] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Property] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[PropertyDropdownValue] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[TitlesChart] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[UserInfoProperty] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[UserInfoTitles] TO [t5web_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[UserInfoVaccinationDetails] TO [t5web_app];

GRANT SELECT, INSERT, UPDATE, DELETE ON
    OBJECT::[dbo].[VaccineLog] TO [t5web_app];
GO

/* The three tables the personnel, department-chart and device screens update.
   The screens rendered fine without these grants; clicking save did not. */
GRANT UPDATE ON OBJECT::[dbo].[Department] TO [t5web_app];
GRANT UPDATE ON OBJECT::[dbo].[Device]     TO [t5web_app];
GRANT UPDATE ON OBJECT::[dbo].[UserInfo]   TO [t5web_app];
GO

/* A person is never deleted from the portal: the engine's own client owns that,
   and a deleted row would take the attendance history with it. */
DENY DELETE ON OBJECT::[dbo].[UserInfo] TO [t5web_app];
GO

PRINT N't5web_admin created with least-privilege grants. Change its password now.';
GO
