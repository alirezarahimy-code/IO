/* =====================================================================
   T5Web Admin Portal — schema verification
   Prints one row per required object: OK / MISSING.
   Run after 01-t5web-objects.sql on the target server.
   ===================================================================== */

SET NOCOUNT ON;

DECLARE @results TABLE
(
    ObjectName nvarchar(200) NOT NULL,
    Kind       nvarchar(20)  NOT NULL,
    Status     nvarchar(10)  NOT NULL
);

/* ---------- engine objects the portal reads or writes ---------- */
INSERT INTO @results (ObjectName, Kind, Status)
SELECT v.ObjectName, N'table',
       CASE WHEN OBJECT_ID(N'dbo.' + v.ObjectName, N'U') IS NULL THEN N'MISSING' ELSE N'OK' END
FROM (VALUES
    (N'UserInfo'), (N'UserInfoTitles'), (N'TitlesChart'), (N'Department'),
    (N'Device'), (N'Gate'), (N'EmpType'), (N'Certificate'),
    (N'Workflow'), (N'LeaveTypes'), (N'Request'), (N'RequestAccept'),
    (N'Record'), (N'HourlyLeave')
) AS v(ObjectName);

/* ---------- portal-owned objects ---------- */
INSERT INTO @results (ObjectName, Kind, Status)
SELECT v.ObjectName, N'table',
       CASE WHEN OBJECT_ID(N'dbo.' + v.ObjectName, N'U') IS NULL THEN N'MISSING' ELSE N'OK' END
FROM (VALUES (N'T5WebAccess'), (N'T5WebAudit'), (N'T5WebDeviceCommand')) AS v(ObjectName);

/* ---------- columns the data layer depends on ---------- */
INSERT INTO @results (ObjectName, Kind, Status)
SELECT v.ObjectName + N'.' + v.ColumnName, N'column',
       CASE WHEN COL_LENGTH(N'dbo.' + v.ObjectName, v.ColumnName) IS NULL THEN N'MISSING' ELSE N'OK' END
FROM (VALUES
    (N'UserInfo', N'BadgeNumber'), (N'UserInfo', N'DeptID'), (N'UserInfo', N'NationalNo'),
    (N'UserInfo', N'UserName'), (N'UserInfo', N'Password'), (N'UserInfo', N'WebUser'),
    (N'UserInfoTitles', N'TitleChartID'),
    (N'TitlesChart', N'TitleID'), (N'TitlesChart', N'TitleName'),
    (N'Department', N'DepartmentID'), (N'Department', N'DepartmentName'), (N'Department', N'UpperDeptID'),
    (N'Device', N'MachineNumber'), (N'Device', N'IP'), (N'Device', N'Port'), (N'Device', N'LastStamp'),
    (N'Workflow', N'OrderIndex'), (N'Workflow', N'AccepterTitleID'),
    (N'T5WebAccess', N'Feature'), (N'T5WebAccess', N'Level'),
    (N'T5WebAudit', N'Hash'), (N'T5WebAudit', N'PreviousHash'),
    (N'T5WebDeviceCommand', N'MachineNumber'), (N'T5WebDeviceCommand', N'Command'),
    (N'T5WebDeviceCommand', N'Status')
) AS v(ObjectName, ColumnName);

/* ---------- the portal login ---------- */
INSERT INTO @results (ObjectName, Kind, Status)
SELECT N'login t5web_admin', N'principal',
       CASE WHEN SUSER_ID(N't5web_admin') IS NULL THEN N'MISSING' ELSE N'OK' END;

INSERT INTO @results (ObjectName, Kind, Status)
SELECT N'user t5web_admin (this db)', N'principal',
       CASE WHEN USER_ID(N't5web_admin') IS NULL THEN N'MISSING' ELSE N'OK' END;

INSERT INTO @results (ObjectName, Kind, Status)
SELECT N'audit is append-only', N'policy',
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
SELECT ObjectName, Kind, Status FROM @results ORDER BY
    CASE Status WHEN N'MISSING' THEN 0 ELSE 1 END, Kind, ObjectName;

SELECT COUNT(1) AS MissingObjects FROM @results WHERE Status = N'MISSING';
