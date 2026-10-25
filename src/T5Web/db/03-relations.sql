/* =====================================================================
   T5Web Admin Portal — relation integrity report (read-only)
   ---------------------------------------------------------------------
   Run order: 01 -> 02 -> 03 -> verify.sql

   What this script is for
   -----------------------
   The engine owns its schema, so the portal cannot add a foreign key to
   [Record], [Request] or any other engine table (docs/01, R1). It cannot add
   a foreign key between catalogs either: ReportT5V3 and Personnel are two
   databases, and a cross-database FK is not a thing in SQL Server.

   So the relations are enforced in three places instead:

     1. the 33 FOREIGN KEY constraints the engine's own DDL declares
        (engine/relations.json, kind = "declared");
     2. the code — every cross-catalog reference is resolved by
        PersonnelDirectory and merged in memory, never JOINed (docs/09);
     3. this report — it proves the data actually satisfies the relations the
        code assumes, and it is the artefact to run before and after any
        migration, import or manual correction.

   Every row is read-only: nothing here writes, deletes or repairs. A
   "VIOLATION" row is a data problem for the operator, not a script bug.

   Cross-catalog checks need both catalogs on the same SQL Server instance
   (the deployment the legacy portal also required). If Personnel lives on
   another server, run the two halves separately on each server.
   ===================================================================== */

SET NOCOUNT ON;

/* Change this if the Personnel catalog has another name on this server. */
DECLARE @Personnel sysname = N'Personnel';

DECLARE @results TABLE
(
    CheckName  nvarchar(120) NOT NULL,
    RelationFa nvarchar(200) NOT NULL,
    Orphans    int           NULL,
    Status     nvarchar(12)  NULL
);

DECLARE @sql nvarchar(max);

/* ------------------------------------------------------------------ keys
   Two tables pair (badge, feature) and (badge, title) etc. A duplicate row
   means the portal's MERGE would silently keep one of them. ------------- */
INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'T5WebAccess key', N'کلید (شماره پرسنلی، قابلیت) تکراری', COUNT(1)
FROM (SELECT [BadgeNumber], [Feature] FROM [T5WebAccess]
      GROUP BY [BadgeNumber], [Feature] HAVING COUNT(1) > 1) d;

/* ------------------------------------------------- same-catalog relations
   Declared by the engine's DDL; these can be checked with a plain anti-join. */
INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'Workflow.TitleID → TitlesChart', N'عنوان شغلی ناموجود در زنجیره تأیید', COUNT(1)
FROM [Workflow] w
WHERE NOT EXISTS (SELECT 1 FROM [TitlesChart] t WHERE t.[TitleID] = w.[TitleID]);

INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'Workflow.AccepterTitleID → TitlesChart', N'عنوان تأییدکننده ناموجود', COUNT(1)
FROM [Workflow] w
WHERE NOT EXISTS (SELECT 1 FROM [TitlesChart] t WHERE t.[TitleID] = w.[AccepterTitleID]);

INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'Workflow.LeaveTypeID → LeaveType', N'نوع مرخصی ناموجود در زنجیره تأیید', COUNT(1)
FROM [Workflow] w
WHERE NOT EXISTS (SELECT 1 FROM [LeaveType] lt WHERE lt.[LeaveID] = w.[LeaveTypeID]);

INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'ShiftDetail.ShiftID → Shift', N'ردیف برنامه با شیفت ناموجود', COUNT(1)
FROM [ShiftDetail] d
WHERE NOT EXISTS (SELECT 1 FROM [Shift] s WHERE s.[ShiftID] = d.[ShiftID]);

INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'ShiftDetail.ScheduleID → Schedule', N'ردیف برنامه با برنامه ناموجود', COUNT(1)
FROM [ShiftDetail] d
WHERE NOT EXISTS (SELECT 1 FROM [Schedule] s WHERE s.[ScheduleID] = d.[ScheduleID]);

INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'YearRemain.LeaveTypeID → LeaveType', N'مانده مرخصی با نوع ناموجود', COUNT(1)
FROM [YearRemain] y
WHERE NOT EXISTS (SELECT 1 FROM [LeaveType] lt WHERE lt.[LeaveID] = y.[LeaveTypeID]);

INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'LeavesAdded.LeaveTypeID → LeaveType', N'مرخصی اضافه‌شده با نوع ناموجود', COUNT(1)
FROM [LeavesAdded] l
WHERE NOT EXISTS (SELECT 1 FROM [LeaveType] lt WHERE lt.[LeaveID] = l.[LeaveTypeID]);

INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'RequestAccept.RequestID → Request', N'تأیید بدون درخواست', COUNT(1)
FROM [RequestAccept] a
WHERE NOT EXISTS (SELECT 1 FROM [Request] r WHERE r.[RequestID] = a.[RequestID]);

INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'UserShift.ShiftID → Shift', N'اختصاص شیفت به شیفت ناموجود', COUNT(1)
FROM [UserShift] us
WHERE NOT EXISTS (SELECT 1 FROM [Shift] s WHERE s.[ShiftID] = us.[ShiftID]);

/* ----------------------------------------------------- hierarchy cycles
   A cycle here makes any recursive read (the department tree) loop. */
INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'Department hierarchy cycle', N'حلقه در درخت سازمانی', COUNT(1)
FROM [Department] d
WHERE d.[UpperDeptID] IS NOT NULL AND d.[UpperDeptID] <> 0
  AND d.[UpperDeptID] = d.[DepartmentID];

INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'Department hierarchy depth', N'سطرهایی که والدشان وجود ندارد (ریشه می‌شوند)', COUNT(1)
FROM [Department] d
WHERE d.[UpperDeptID] IS NOT NULL AND d.[UpperDeptID] <> 0
  AND NOT EXISTS (SELECT 1 FROM [Department] p WHERE p.[DepartmentID] = d.[UpperDeptID]);

INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'TitlesChart.UpperTitleID → TitlesChart', N'عنوان شغلی با والد ناموجود', COUNT(1)
FROM [TitlesChart] t
WHERE t.[UpperTitleID] IS NOT NULL AND t.[UpperTitleID] <> 0
  AND NOT EXISTS (SELECT 1 FROM [TitlesChart] p WHERE p.[TitleID] = t.[UpperTitleID]);

INSERT INTO @results (CheckName, RelationFa, Orphans)
SELECT N'Gate.UpperGateID → Gate', N'درب با والد ناموجود', COUNT(1)
FROM [Gate] g
WHERE g.[UpperGateID] IS NOT NULL AND g.[UpperGateID] <> 0
  AND NOT EXISTS (SELECT 1 FROM [Gate] p WHERE p.[GateID] = g.[UpperGateID]);

/* ------------------------------------------- cross-catalog relations
   These have no FK (two catalogs, one server) and the portal resolves them
   in memory, so the report is the only proof they hold. ------------------ */
IF OBJECT_ID(N'[' + @Personnel + N'].[dbo].[UserInfo]', N'U') IS NOT NULL
BEGIN
    SET @sql = N'
SELECT N''Record.BadgeNumber → UserInfo'', N''تردد با شماره پرسنلی ناموجود'', COUNT(1)
FROM [Record] r
WHERE NOT EXISTS (SELECT 1 FROM [' + @Personnel + N'].[dbo].[UserInfo] u
                  WHERE u.[BadgeNumber] = r.[BadgeNumber]);';
    INSERT INTO @results (CheckName, RelationFa, Orphans) EXEC (@sql);

    SET @sql = N'
SELECT N''Request.BadgeNumber → UserInfo'', N''درخواست با شماره پرسنلی ناموجود'', COUNT(1)
FROM [Request] r
WHERE NOT EXISTS (SELECT 1 FROM [' + @Personnel + N'].[dbo].[UserInfo] u
                  WHERE u.[BadgeNumber] = r.[BadgeNumber]);';
    INSERT INTO @results (CheckName, RelationFa, Orphans) EXEC (@sql);

    SET @sql = N'
SELECT N''YearRemain.BadgeNumber → UserInfo'', N''مانده مرخصی برای پرسنل ناموجود'', COUNT(1)
FROM [YearRemain] y
WHERE NOT EXISTS (SELECT 1 FROM [' + @Personnel + N'].[dbo].[UserInfo] u
                  WHERE u.[BadgeNumber] = y.[BadgeNumber]);';
    INSERT INTO @results (CheckName, RelationFa, Orphans) EXEC (@sql);

    SET @sql = N'
SELECT N''UserShift.BadgeNumber → UserInfo'', N''اختصاص شیفت برای پرسنل ناموجود'', COUNT(1)
FROM [UserShift] us
WHERE NOT EXISTS (SELECT 1 FROM [' + @Personnel + N'].[dbo].[UserInfo] u
                  WHERE u.[BadgeNumber] = us.[BadgeNumber]);';
    INSERT INTO @results (CheckName, RelationFa, Orphans) EXEC (@sql);

    SET @sql = N'
SELECT N''T5WebAccess.BadgeNumber → UserInfo'', N''سطح دسترسی برای پرسنل ناموجود'', COUNT(1)
FROM [T5WebAccess] p
WHERE NOT EXISTS (SELECT 1 FROM [' + @Personnel + N'].[dbo].[UserInfo] u
                  WHERE u.[BadgeNumber] = p.[BadgeNumber]);';
    INSERT INTO @results (CheckName, RelationFa, Orphans) EXEC (@sql);

    SET @sql = N'
SELECT N''UserInfoTitles.TitleChartID → TitlesChart'', N''عنوان شغلی پرسنل ناموجود'', COUNT(1)
FROM [' + @Personnel + N'].[dbo].[UserInfoTitles] uit
WHERE NOT EXISTS (SELECT 1 FROM [' + @Personnel + N'].[dbo].[TitlesChart] t
                  WHERE t.[TitleID] = uit.[TitleChartID]);';
    INSERT INTO @results (CheckName, RelationFa, Orphans) EXEC (@sql);
END
ELSE
BEGIN
    INSERT INTO @results (CheckName, RelationFa, Orphans)
    VALUES (N'cross-catalog relations', N'پایگاه Personnel در این سرور دیده نشد؛ نیمه‌ی Personnel را جداگانه اجرا کنید', NULL);
END

IF OBJECT_ID(N'[' + @Personnel + N'].[dbo].[Device]', N'U') IS NOT NULL
BEGIN
    SET @sql = N'
SELECT N''Record.MachineNumber → Device'', N''تردد از دستگاه ناموجود'', COUNT(1)
FROM [Record] r
WHERE NOT EXISTS (SELECT 1 FROM [' + @Personnel + N'].[dbo].[Device] d
                  WHERE d.[MachineNumber] = r.[MachineNumber]);';
    INSERT INTO @results (CheckName, RelationFa, Orphans) EXEC (@sql);

    SET @sql = N'
SELECT N''T5WebDeviceCommand.MachineNumber → Device'', N''دستور صف برای دستگاه ناموجود'', COUNT(1)
FROM [T5WebDeviceCommand] c
WHERE NOT EXISTS (SELECT 1 FROM [' + @Personnel + N'].[dbo].[Device] d
                  WHERE d.[MachineNumber] = c.[MachineNumber]);';
    INSERT INTO @results (CheckName, RelationFa, Orphans) EXEC (@sql);
END

/* --------------------------------------------------------------- report */
UPDATE @results
SET Status = CASE WHEN Orphans IS NULL THEN N'n/a'
                  WHEN Orphans = 0 THEN N'OK'
                  ELSE N'VIOLATION' END;

SELECT CheckName, RelationFa, Orphans, Status
FROM @results
ORDER BY CASE Status WHEN N'VIOLATION' THEN 0 WHEN N'n/a' THEN 2 ELSE 1 END, CheckName;

/* A one-line verdict, so this script can be used in a deployment gate. */
SELECT CASE WHEN EXISTS (SELECT 1 FROM @results WHERE Status = N'VIOLATION')
            THEN N'FAIL: data violates a relation the portal relies on'
            ELSE N'OK: every relation holds' END AS Verdict;
