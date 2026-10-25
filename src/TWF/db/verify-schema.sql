/* ==========================================================================
   ARIA (آریا) — Database readiness verification
   --------------------------------------------------------------------------
   Read-only diagnostics. Runs nothing that modifies data or schema.
   Execute against the T5 core database (ReportT5V3) and repeat against the
   Personnel database.

   Usage (offline Windows server, SSMS or sqlcmd):
       sqlcmd -S . -d ReportT5V3 -E -i db\verify-schema.sql
       sqlcmd -S . -d Personnel   -E -i db\verify-schema.sql
   ========================================================================== */

SET NOCOUNT ON;

PRINT '==================================================================';
PRINT '  ARIA database readiness report';
PRINT '==================================================================';
PRINT 'Server     : ' + CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(64))
    + ' (' + CAST(SERVERPROPERTY('Edition') AS NVARCHAR(128)) + ')';
PRINT 'Database   : ' + DB_NAME();
PRINT 'Checked at : ' + CONVERT(NVARCHAR(19), GETDATE(), 120);
PRINT '';

/* --------------------------------------------------------------------------
   Expected objects.
   "Required" objects are those the rewritten code addresses directly:
     * TD5 layer       -> Calendar, DepartmentRules, LeaveTypes, Request,
                          UserInfo, UserRules
     * DA layer (T5V3) -> Workflow, Deputy, Record, Area, Position, ...
     * TWF app itself  -> PassCheckProps (password history)
   Objects marked optional are read through the vendor T5V3 DA classes and may
   legitimately be absent on a trimmed deployment.
   -------------------------------------------------------------------------- */
DECLARE @expected TABLE
(
    ObjectName NVARCHAR(128) NOT NULL,
    Owner      NVARCHAR(16)  NOT NULL,   -- 'T5' | 'Personnel' | 'App'
    Required   BIT           NOT NULL
);

INSERT INTO @expected (ObjectName, Owner, Required) VALUES
    (N'Calendar',         N'T5',        1),
    (N'DepartmentRules',  N'T5',        1),
    (N'LeaveTypes',       N'T5',        1),
    (N'Request',          N'T5',        1),
    (N'UserRules',        N'T5',        1),
    (N'Workflow',         N'T5',        0),
    (N'RequestAccept',    N'T5',        0),
    (N'Deputy',           N'T5',        0),
    (N'PersonnelDeputy',  N'T5',        0),
    (N'Record',           N'T5',        0),
    (N'DailyLeave',       N'T5',        0),
    (N'HourlyLeave',      N'T5',        0),
    (N'OverTime',         N'T5',        0),
    (N'Area',             N'T5',        0),
    (N'Position',         N'T5',        0),
    (N'Parameter',        N'T5',        0),
    (N'UserInfo',         N'Personnel', 0),
    (N'TitlesChart',      N'Personnel', 0),
    (N'UserInfoTitles',   N'Personnel', 0),
    (N'PassCheckProps',   N'App',       1);

/* --------------------------------------------------------------------------
   1. Missing-object report
   -------------------------------------------------------------------------- */
PRINT '--- 1. Object presence -------------------------------------------------';

SELECT
    e.ObjectName                                   AS [Object],
    e.Owner                                        AS [OwnedBy],
    CASE WHEN e.Required = 1 THEN 'yes' ELSE 'no' END AS [Required],
    CASE WHEN OBJECT_ID(N'dbo.' + e.ObjectName, N'U') IS NULL
         THEN 'MISSING' ELSE 'present' END          AS [Status]
FROM @expected AS e
ORDER BY
    CASE WHEN OBJECT_ID(N'dbo.' + e.ObjectName, N'U') IS NULL THEN 0 ELSE 1 END,
    e.Owner, e.ObjectName;

DECLARE @missingRequired INT =
(
    SELECT COUNT(*)
    FROM @expected AS e
    WHERE e.Required = 1
      AND OBJECT_ID(N'dbo.' + e.ObjectName, N'U') IS NULL
);

PRINT '';
IF @missingRequired = 0
    PRINT 'Result: all REQUIRED objects are present in this database.';
ELSE
    PRINT 'Result: ' + CAST(@missingRequired AS NVARCHAR(10))
        + ' REQUIRED object(s) missing. Install/upgrade T5V3 first, then run db/01-app-objects.sql.';
PRINT '';

/* --------------------------------------------------------------------------
   2. Row-count snapshot (quick sanity check that data is reachable)
   -------------------------------------------------------------------------- */
PRINT '--- 2. Row counts ------------------------------------------------------';

DECLARE @tables TABLE (Seq INT IDENTITY(1,1), TableName NVARCHAR(128));
INSERT INTO @tables (TableName) VALUES
    (N'Request'), (N'Workflow'), (N'Record'), (N'Calendar'),
    (N'UserInfo'), (N'TitlesChart'), (N'PassCheckProps');

DECLARE @i INT = 1, @max INT = (SELECT COUNT(*) FROM @tables);
DECLARE @name NVARCHAR(128), @sql NVARCHAR(400), @cnt INT;

WHILE @i <= @max
BEGIN
    SELECT @name = TableName FROM @tables WHERE Seq = @i;

    IF OBJECT_ID(N'dbo.' + @name, N'U') IS NOT NULL
    BEGIN
        SET @sql = N'SELECT @c = COUNT(*) FROM [dbo].[' + @name + N'];';
        EXEC sp_executesql @sql, N'@c INT OUTPUT', @c = @cnt OUTPUT;
        PRINT '  ' + LEFT(@name + SPACE(20), 20) + CAST(@cnt AS NVARCHAR(20)) + ' row(s)';
    END
    ELSE
    BEGIN
        PRINT '  ' + LEFT(@name + SPACE(20), 20) + '(table not present)';
    END

    SET @i = @i + 1;
END

PRINT '';

/* --------------------------------------------------------------------------
   3. Password-history layout check (ARIA-owned contract)
   -------------------------------------------------------------------------- */
PRINT '--- 3. Password history contract ---------------------------------------';

IF OBJECT_ID(N'dbo.PassCheckProps', N'U') IS NULL
BEGIN
    PRINT 'PassCheckProps : MISSING  -> run db/01-app-objects.sql';
END
ELSE
BEGIN
    SELECT
        COL_LENGTH(N'dbo.PassCheckProps', N'Badge')          AS [Badge_bytes],
        COL_LENGTH(N'dbo.PassCheckProps', N'LastChangeDate') AS [LastChangeDate_bytes];

    IF COL_LENGTH(N'dbo.PassCheckProps', N'Badge') IS NOT NULL
       AND COL_LENGTH(N'dbo.PassCheckProps', N'LastChangeDate') IS NOT NULL
        PRINT 'PassCheckProps : OK (legacy-compatible layout)';
    ELSE
        PRINT 'PassCheckProps : WRONG LAYOUT -> expected columns [Badge], [LastChangeDate]';
END

PRINT '';
PRINT 'Report finished.';
GO
