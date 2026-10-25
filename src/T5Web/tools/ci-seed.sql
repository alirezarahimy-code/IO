/* =====================================================================
   TEST FIXTURE — a Windows account, a badge, and a permission matrix
   ---------------------------------------------------------------------
   The portal cannot show a single screen without data: AdminIdentity looks the
   Windows account up in [UserInfo] (WebUser = 1, not resigned), and every page
   is gated by a row in [T5WebAccess]. An installation check that stops at
   "IIS answers 401" therefore proves the padlock and nothing behind it.

   This file seeds exactly that much, on top of the synthetic schema
   (tools/synthetic-schema.py), so a CI runner can open every real screen:

       sqlcmd -S (localdb)\\T5Web -E -b -i tools/ci-seed.sql \
              -v Who=runneradmin

   It is NOT a deployment step and NOT part of the deploy kit: real data comes
   from the engine (docs/09, docs/12). Names are obviously fake, and BadgeNumber
   1001 is the fixture badge, not a real employee.
   ===================================================================== */

SET NOCOUNT ON;
GO

IF N'$(Who)' = N''
BEGIN
    RAISERROR (N'Pass -v Who=<the Windows account name the test will sign in as>.', 16, 1);
    SET NOEXEC ON;
END
GO

/* ---- the operator ---------------------------------------------------- */
USE [Personnel];
GO

/* The two lookups are IDENTITY columns in the engine DDL (engine/column-types.json), so
   the fixture cannot choose their values: it inserts the rows and then discovers the
   keys the engine handed out. A hand-picked ID would work on the synthetic schema and
   fail on the real install with Msg 544 - the fixture has to survive both. */
IF NOT EXISTS (SELECT 1 FROM dbo.[Department] WHERE [DepartmentName] = N'واحد آزمون')
BEGIN
    INSERT INTO dbo.[Department] ([DepartmentName], [Name], [UpperDeptID])
        VALUES (N'واحد آزمون', N'واحد آزمون', 0);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.[EmpType] WHERE [EmpTypeName] = N'رسمی')
BEGIN
    INSERT INTO dbo.[EmpType] ([EmpTypeName]) VALUES (N'رسمی');
END
GO

DECLARE @DeptID    int = (SELECT TOP 1 [DepartmentID] FROM dbo.[Department]
                           WHERE [DepartmentName] = N'واحد آزمون' ORDER BY [DepartmentID]);
DECLARE @EmpTypeID int = (SELECT TOP 1 [EmpTypeID] FROM dbo.[EmpType]
                           WHERE [EmpTypeName] = N'رسمی' ORDER BY [EmpTypeID]);

IF NOT EXISTS (SELECT 1 FROM dbo.[UserInfo] WHERE [BadgeNumber] = 1001)
BEGIN
    INSERT INTO dbo.[UserInfo]
        ([BadgeNumber], [UserName], [EmployeeNumber], [FirstName], [LastName], [FatherName],
         [NationalNo], [Phone], [Email], [Address], [Descriptions],
         [DeptID], [EmpTypeID], [CertificateID], [Birthday], [HiredDay],
         [Gender], [IsFemale], [ProgramUser], [WebUser], [DeviceEnable],
         [DeviceCardNo], [DeviceName], [DevicePass], [DeviceGroupNumber],
         [DeviceMachineNumber], [DevicePriv], [DevicePrivilage], [DviceGroup],
         [Janbaz], [JanbazCode], [ValidFromHasValue], [ValidToHasValue])
    VALUES
        (1001, N'$(Who)', N'0001', N'آزمون', N'نصب', N'پورتال',
         N'0012345678', N'00000000', N'fixture@example.invalid', N'-', N'CI fixture row',
         @DeptID, @EmpTypeID, 0, '1980-01-01', '2015-01-01',
         0, 0, 0, 1, 0,
         N'', N'', N'', 0,
         0, 0, 0, 0,
         0, 0, 0, 0);
END
ELSE
BEGIN
    -- Re-running the fixture must make the row usable again, not silently keep a stale
    -- user name: the Windows account differs from runner to runner.
    UPDATE dbo.[UserInfo]
       SET [UserName] = N'$(Who)', [WebUser] = 1, [FiredDate] = NULL,
           [DeptID] = @DeptID, [EmpTypeID] = @EmpTypeID
     WHERE [BadgeNumber] = 1001;
END
GO

/* ---- the permission matrix ------------------------------------------- */
/* Every feature in T5Web.Domain/AdminFeature.cs, at Allow. tools/ci-fixture-gate.py
   fails the build when a new feature is added and this list is not updated: a
   fixture that silently misses a feature turns a real screen into a 403 and the
   sweep then "passes" without ever having rendered it. */
USE [ReportT5V3];
GO

MERGE dbo.[T5WebAccess] AS target
USING (VALUES
    (1001, 10, 2),
    (1001, 11, 2),
    (1001, 12, 2),
    (1001, 13, 2),
    (1001, 15, 2),
    (1001, 16, 2),
    (1001, 17, 2),
    (1001, 18, 2),
    (1001, 20, 2),
    (1001, 21, 2),
    (1001, 22, 2),
    (1001, 23, 2),
    (1001, 24, 2),
    (1001, 25, 2),
    (1001, 26, 2),
    (1001, 27, 2),
    (1001, 30, 2),
    (1001, 31, 2),
    (1001, 32, 2),
    (1001, 33, 2),
    (1001, 34, 2),
    (1001, 35, 2),
    (1001, 40, 2),
    (1001, 41, 2),
    (1001, 50, 2),
    (1001, 51, 2),
    (1001, 52, 2),
    (1001, 53, 2),
    (1001, 54, 2),
    (1001, 55, 2),
    (1001, 56, 2),
    (1001, 57, 2),
    (1001, 60, 2),
    (1001, 61, 2),
    (1001, 62, 2),
    (1001, 70, 2),
    (1001, 90, 2)
) AS source ([BadgeNumber], [Feature], [Level])
ON target.[BadgeNumber] = source.[BadgeNumber] AND target.[Feature] = source.[Feature]
WHEN MATCHED THEN UPDATE SET [Level] = source.[Level], [ChangedBy] = N'ci-fixture'
WHEN NOT MATCHED THEN INSERT ([BadgeNumber], [Feature], [Level], [ChangedBy])
     VALUES (source.[BadgeNumber], source.[Feature], source.[Level], N'ci-fixture');
GO

PRINT N'fixture ready: badge 1001 can sign in as $(Who) and open every feature.';
GO
