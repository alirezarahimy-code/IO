/* =====================================================================
   T5Web Admin Portal — schema verification
   Prints one row per required object: OK / MISSING.

   Run after 01-t5web-objects.sql. Because the portal lives on TWO catalogs
   (docs/09), a bare OBJECT_ID(N'dbo.UserInfo') can only ever answer for the
   database the script was pointed at. That is how CI found this file reporting
   Department/DepartmentID MISSING while they existed one catalog away.

   So every check names its catalog explicitly, and the two catalog names are
   parameters at the top: change them if the deployment uses other names.

   Output contract (the deploy verifier and CI both parse it):
     * one row per object with Status = OK | MISSING
     * a final row MissingObjects = <count>, and no other row may contain the
       word MISSING in its name or kind.
   ===================================================================== */

SET NOCOUNT ON;

DECLARE @T5        sysname = N'ReportT5V3';
DECLARE @Personnel sysname = N'Personnel';

IF DB_ID(@T5) IS NULL
    RAISERROR (N'The t5 catalog (%s) does not exist on this instance.', 16, 1, @T5);
IF DB_ID(@Personnel) IS NULL
    RAISERROR (N'The personnel catalog (%s) does not exist on this instance.', 16, 1, @Personnel);

DECLARE @results TABLE
(
    Catalog    sysname       NOT NULL,
    ObjectName nvarchar(200) NOT NULL,
    Kind       nvarchar(20)  NOT NULL,
    Status     nvarchar(10)  NOT NULL
);

/* ---------- engine tables, by catalog ---------- */
INSERT INTO @results (Catalog, ObjectName, Kind, Status)
SELECT @Personnel, v.ObjectName, N'table',
       CASE WHEN OBJECT_ID(QUOTENAME(@Personnel) + N'.dbo.' + v.ObjectName, N'U') IS NULL
            THEN N'MISSING' ELSE N'OK' END
FROM (VALUES
    (N'UserInfo'), (N'UserInfoTitles'), (N'TitlesChart'), (N'Department'),
    (N'Device'), (N'Gate'), (N'EmpType'), (N'Certificate')
) AS v(ObjectName);

INSERT INTO @results (Catalog, ObjectName, Kind, Status)
SELECT @T5, v.ObjectName, N'table',
       CASE WHEN OBJECT_ID(QUOTENAME(@T5) + N'.dbo.' + v.ObjectName, N'U') IS NULL
            THEN N'MISSING' ELSE N'OK' END
FROM (VALUES
    (N'Workflow'), (N'LeaveType'), (N'Request'), (N'RequestAccept'),
    (N'Record'), (N'HourlyLeave'), (N'UserShift'), (N'SystemLog')
) AS v(ObjectName);

/* ---------- portal-owned objects (always in the t5 catalog) ---------- */
INSERT INTO @results (Catalog, ObjectName, Kind, Status)
SELECT @T5, v.ObjectName, N'table',
       CASE WHEN OBJECT_ID(QUOTENAME(@T5) + N'.dbo.' + v.ObjectName, N'U') IS NULL
            THEN N'MISSING' ELSE N'OK' END
FROM (VALUES
    (N'T5WebAccess'), (N'T5WebAudit'), (N'T5WebDeviceCommand')
) AS v(ObjectName);

/* ---------- columns the data layer depends on ---------- */
INSERT INTO @results (Catalog, ObjectName, Kind, Status)
SELECT @Personnel, v.ObjectName + N'.' + v.ColumnName, N'column',
       CASE WHEN COL_LENGTH(QUOTENAME(@Personnel) + N'.dbo.' + v.ObjectName, v.ColumnName) IS NULL
            THEN N'MISSING' ELSE N'OK' END
FROM (VALUES
    (N'UserInfo', N'BadgeNumber'),
    (N'UserInfo', N'DeptID'),
    (N'UserInfo', N'NationalNo'),
    (N'UserInfo', N'UserName'),
    (N'UserInfo', N'Password'),
    (N'UserInfo', N'WebUser'),
    (N'UserInfo', N'FirstName'),
    (N'UserInfo', N'LastName'),
    (N'UserInfo', N'HiredDate'),
    (N'UserInfo', N'FiredDate'),
    (N'UserInfoTitles', N'TitleChartID'),
    (N'UserInfoTitles', N'BadgeNumber'),
    (N'TitlesChart', N'TitleID'),
    (N'TitlesChart', N'TitleName'),
    (N'Department', N'DepartmentID'),
    (N'Department', N'DepartmentName'),
    (N'Department', N'UpperDeptID'),
    (N'Device', N'MachineNumber'),
    (N'Device', N'IP'),
    (N'Device', N'MachineName')
) AS v(ObjectName, ColumnName);

INSERT INTO @results (Catalog, ObjectName, Kind, Status)
SELECT @T5, v.ObjectName + N'.' + v.ColumnName, N'column',
       CASE WHEN COL_LENGTH(QUOTENAME(@T5) + N'.dbo.' + v.ObjectName, v.ColumnName) IS NULL
            THEN N'MISSING' ELSE N'OK' END
FROM (VALUES
    (N'Workflow', N'OrderIndex'),
    (N'Workflow', N'AccepterTitleID'),
    (N'Workflow', N'TitleID'),
    (N'Record', N'BadgeNumber'),
    (N'Record', N'CheckTime'),
    (N'Record', N'MachineNumber'),
    (N'Record', N'IsValid'),
    (N'Request', N'BadgeNumber'),
    (N'Request', N'ForDate'),
    (N'Request', N'LeaveID'),
    (N'LeaveType', N'LeaveTypeID'),
    (N'LeaveType', N'LeaveName'),
    (N'UserShift', N'BadgeNumber'),
    (N'UserShift', N'FromDate'),
    (N'UserShift', N'ToDate'),
    (N'UserShift', N'ShiftID'),
    (N'SystemLog', N'ID'),
    (N'SystemLog', N'EventTime'),
    (N'SystemLog', N'EventID'),
    (N'SystemLog', N'Descriptions')
) AS v(ObjectName, ColumnName);

/* ---------- portal columns (defined by db/01, so proven by that DDL) ---------- */
INSERT INTO @results (Catalog, ObjectName, Kind, Status)
SELECT @T5, v.ObjectName + N'.' + v.ColumnName, N'column',
       CASE WHEN COL_LENGTH(QUOTENAME(@T5) + N'.dbo.' + v.ObjectName, v.ColumnName) IS NULL
            THEN N'MISSING' ELSE N'OK' END
FROM (VALUES
    (N'T5WebAccess', N'Feature'),
    (N'T5WebAccess', N'Level'),
    (N'T5WebAccess', N'BadgeNumber'),
    (N'T5WebAudit', N'Hash'),
    (N'T5WebAudit', N'PreviousHash'),
    (N'T5WebAudit', N'UtcTime'),
    (N'T5WebDeviceCommand', N'MachineNumber'),
    (N'T5WebDeviceCommand', N'Command'),
    (N'T5WebDeviceCommand', N'Status')
) AS v(ObjectName, ColumnName);

/* ---------- the portal login ---------- */
INSERT INTO @results (Catalog, ObjectName, Kind, Status)
SELECT N'server', N'login t5web_admin', N'principal',
       CASE WHEN SUSER_ID(N't5web_admin') IS NULL THEN N'MISSING' ELSE N'OK' END;

INSERT INTO @results (Catalog, ObjectName, Kind, Status)
SELECT @T5, N'user t5web_admin (t5)', N'principal',
       CASE WHEN USER_ID(N't5web_admin') IS NULL THEN N'MISSING' ELSE N'OK' END;

INSERT INTO @results (Catalog, ObjectName, Kind, Status)
SELECT @Personnel, N'user t5web_admin (personnel)', N'principal',
       CASE WHEN DATABASEPROPERTYEX(@Personnel, 'Status') <> N'ONLINE'
            THEN N'MISSING'
            WHEN EXISTS (SELECT 1 FROM [Personnel].sys.database_principals WHERE name = N't5web_admin')
            THEN N'OK' ELSE N'MISSING' END;

INSERT INTO @results (Catalog, ObjectName, Kind, Status)
SELECT @T5, N'audit is append-only', N'policy',
       CASE WHEN EXISTS
            (
                SELECT 1
                FROM sys.database_permissions p
                INNER JOIN sys.database_principals u ON u.principal_id = p.grantee_principal_id
                WHERE u.name = N't5web_admin'
                  AND p.state = N'D'
                  AND p.major_id = OBJECT_ID(N'dbo.T5WebAudit')
                  AND p.permission_name IN (N'UPDATE', N'DELETE')
            ) THEN N'OK' ELSE N'MISSING' END;

/* ---------- output ---------- */
SELECT Catalog, ObjectName, Kind, Status FROM @results ORDER BY
    CASE Status WHEN N'MISSING' THEN 0 ELSE 1 END, Catalog, Kind, ObjectName;

SELECT COUNT(1) AS MissingObjects FROM @results WHERE Status = N'MISSING';
