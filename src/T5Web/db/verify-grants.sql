/* =====================================================================
   T5Web Admin Portal — prove the grants the screens need, on a real server
   ---------------------------------------------------------------------
   db\02 grants; this script asks SQL Server what the identity actually got,
   inside EXECUTE AS LOGIN, so the answer comes from the engine's permission
   system and not from the text of the grant script.

   Run it in each catalog that holds tables:

     sqlcmd -S <server> -E -b -d ReportT5V3 -i db\verify-grants.sql ^
            -v Login="IIS APPPOOL\T5WebAdmin"
     sqlcmd -S <server> -E -b -d Personnel  -i db\verify-grants.sql ^
            -v Login="IIS APPPOOL\T5WebAdmin"

   Checks for tables that live in the other catalog are skipped, so the same
   script is correct in both. Exit code is non-zero when any expectation fails.
   ===================================================================== */

:setvar Login "IIS APPPOOL\T5WebAdmin"

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(Login)')
BEGIN
    RAISERROR (N'The login $(Login) does not exist: run db\04-grant-app-pool-login.sql first.', 16, 1);
    RETURN;
END

/* verb, table, expected (1 = allowed, 0 = refused), why the screen needs it */
DECLARE @check TABLE (verb varchar(6), tbl sysname, want bit, why nvarchar(200));
INSERT INTO @check (verb, tbl, want, why) VALUES
    ('SELECT', 'T5WebAccess',        1, N'permission matrix is displayed'),
    ('INSERT', 'T5WebAccess',        1, N'permission matrix is edited'),
    ('INSERT', 'T5WebAudit',         1, N'screen actions are audited'),
    ('UPDATE', 'T5WebAudit',         0, N'the audit trail is append-only'),
    ('DELETE', 'T5WebAudit',         0, N'the audit trail is append-only'),
    ('INSERT', 'T5WebDeviceCommand', 1, N'device commands are queued'),
    ('INSERT', 'Record',             1, N'attendance is entered'),
    ('UPDATE', 'Record',             1, N'attendance is corrected'),
    ('DELETE', 'Record',             0, N'attendance history is kept'),
    ('UPDATE', 'Request',            1, N'a request is suspended'),
    ('INSERT', 'RequestAccept',      1, N'a request is approved'),
    ('INSERT', 'Workflow',           1, N'a workflow step is added'),
    ('INSERT', 'EmpTypeLeave',       1, N'a leave entitlement is granted'),
    ('DELETE', 'EmpTypeLeave',       1, N'a leave entitlement is withdrawn'),
    ('UPDATE', 'Department',         1, N'the chart is reorganised'),
    ('UPDATE', 'UserInfo',           1, N'personnel data is edited'),
    ('DELETE', 'UserInfo',           0, N'a person is never deleted from the portal'),
    ('UPDATE', 'Device',             1, N'device settings are edited'),
    ('ALTER', '*schema*',            0, N'the portal is not the schema owner');

/* Only what exists here: the two catalogs share the identity, not the tables. */
DELETE FROM @check WHERE tbl <> '*schema*' AND OBJECT_ID(QUOTENAME(tbl), N'U') IS NULL;

DECLARE @result TABLE (verb varchar(12), tbl sysname, want bit, got bit, why nvarchar(200));

EXECUTE AS LOGIN = N'$(Login)';
INSERT INTO @result (verb, tbl, want, got, why)
SELECT verb, tbl, want,
       ISNULL(CONVERT(bit, HAS_PERMS_BY_NAME(CASE WHEN tbl = '*schema*' THEN QUOTENAME(N'dbo') ELSE QUOTENAME(tbl) END,
                                              CASE WHEN tbl = '*schema*' THEN 'SCHEMA' ELSE 'OBJECT' END, verb)), 0),
       why
FROM @check;
REVERT;

SELECT CONCAT(CASE WHEN want = got THEN 'PASS ' ELSE 'FAIL ' END,
              verb, N' ', REPLACE(tbl, '*schema*', N'SCHEMA dbo'), N' — ', why,
              N' (wanted ', CASE WHEN want = 1 THEN N'allowed' ELSE N'refused' END, N')') AS [check]
FROM @result
ORDER BY CASE WHEN want = got THEN 1 ELSE 0 END, tbl, verb;

DECLARE @failed int = (SELECT COUNT(1) FROM @result WHERE want <> got);
IF @failed > 0
BEGIN
    DECLARE @msg nvarchar(200) = CONCAT(N'$(Login) fails ', @failed,
        N' permission checks in ', DB_NAME(), N': the screens will render and then refuse to save.');
    RAISERROR (@msg, 16, 1);
END
ELSE
BEGIN
    PRINT CONCAT(N'PASS  ', DB_NAME(), N': ', (SELECT COUNT(1) FROM @result), N' permission checks on $(Login)');
END
