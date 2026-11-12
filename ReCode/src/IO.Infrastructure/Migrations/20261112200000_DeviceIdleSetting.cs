using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112200000_DeviceIdleSetting")]
public sealed class DeviceIdleSetting:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE dbo.DeviceOperations ADD IdlePlanJson nvarchar(4000) NULL;");
        migrationBuilder.Sql("""
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Request;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Result;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Resolution;
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Request CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 60 AND (([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read',N'device.restart',N'network.ip.set',N'network.inspect',N'settings.idle.set') AND [TargetUtc] IS NULL) OR ([Kind]=N'clock.set' AND [TargetUtc] IS NOT NULL AND DATEPART(TZOFFSET,[TargetUtc])=0 AND DATEPART(NANOSECOND,[TargetUtc])=0)));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Result CHECK (([ResultJson] IS NULL OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read',N'network.inspect',N'settings.idle.set') AND [State]=N'succeeded' AND ISJSON([ResultJson])=1 AND DATALENGTH([ResultJson])<=8000)) AND ([Kind] COLLATE Latin1_General_100_BIN2 NOT IN(N'information.read',N'settings.read',N'network.inspect',N'settings.idle.set') OR [State]<>N'succeeded' OR [ResultJson] IS NOT NULL));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Resolution CHECK (([ResolutionInfoId] IS NULL AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL) OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'clock.set',N'device.restart',N'network.ip.set',N'settings.idle.set') AND [State]=N'unknown' AND [ResolutionInfoId] IS NOT NULL AND [ResolutionInfoId]<>[Id] AND [ResolvedByUserId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET,[ResolvedAtUtc])=0 AND [ResolvedAtUtc]>=[FinishedAtUtc]));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Idle CHECK (([Kind] COLLATE Latin1_General_100_BIN2<>N'settings.idle.set' AND [IdlePlanJson] IS NULL) OR ([Kind] COLLATE Latin1_General_100_BIN2=N'settings.idle.set' AND [IdlePlanJson] IS NOT NULL AND ISJSON([IdlePlanJson])=1 AND DATALENGTH([IdlePlanJson])<=8000 AND JSON_VALUE([IdlePlanJson],'$.Contract') IS NOT NULL AND JSON_VALUE([IdlePlanJson],'$.Contract') COLLATE Latin1_General_100_BIN2=N'zk-idle-minutes-v1' AND TRY_CONVERT(uniqueidentifier,JSON_VALUE([IdlePlanJson],'$.SnapshotId')) IS NOT NULL AND JSON_VALUE([IdlePlanJson],'$.Serial') IS NOT NULL AND JSON_VALUE([IdlePlanJson],'$.Product') IS NOT NULL AND JSON_VALUE([IdlePlanJson],'$.Firmware') IS NOT NULL AND JSON_VALUE([IdlePlanJson],'$.ApprovalReference') IS NOT NULL AND TRY_CONVERT(int,JSON_VALUE([IdlePlanJson],'$.ExpectedMinutes')) IS NOT NULL AND TRY_CONVERT(int,JSON_VALUE([IdlePlanJson],'$.NewMinutes')) IS NOT NULL AND TRY_CONVERT(int,JSON_VALUE([IdlePlanJson],'$.ExpectedMinutes')) BETWEEN 1 AND 999 AND TRY_CONVERT(int,JSON_VALUE([IdlePlanJson],'$.NewMinutes')) BETWEEN 1 AND 999 AND JSON_VALUE([IdlePlanJson],'$.ExpectedMinutes')<>JSON_VALUE([IdlePlanJson],'$.NewMinutes')));
        """);
        migrationBuilder.Sql("""
        CREATE OR ALTER TRIGGER dbo.TR_DeviceOperations_History ON dbo.DeviceOperations AFTER INSERT,UPDATE,DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51017,'Device operations retain history.',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.Id=i.Id) AND i.State<>N'pending') THROW 51017,'New operations must be pending.',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(
                SELECT d.DeviceId,d.Kind COLLATE Latin1_General_100_BIN2,DATALENGTH(d.Kind),d.RequestedByUserId,d.RequestedAtUtc,d.ExpectedDeviceVersion,d.TimeoutSeconds,d.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(d.TimeZoneId),d.IdlePlanJson COLLATE Latin1_General_100_BIN2,DATALENGTH(d.IdlePlanJson),d.TargetUtc,d.NetworkPlanJson COLLATE Latin1_General_100_BIN2,DATALENGTH(d.NetworkPlanJson)
                EXCEPT SELECT i.DeviceId,i.Kind COLLATE Latin1_General_100_BIN2,DATALENGTH(i.Kind),i.RequestedByUserId,i.RequestedAtUtc,i.ExpectedDeviceVersion,i.TimeoutSeconds,i.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(i.TimeZoneId),i.IdlePlanJson COLLATE Latin1_General_100_BIN2,DATALENGTH(i.IdlePlanJson),i.TargetUtc,i.NetworkPlanJson COLLATE Latin1_General_100_BIN2,DATALENGTH(i.NetworkPlanJson)))
                THROW 51017,'Operation request identity is immutable.',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE
                (d.State=N'pending' AND i.State NOT IN(N'running',N'cancelled',N'revoked',N'device-changed',N'expired')) OR
                (d.State=N'running' AND (i.State IN(N'pending',N'running') OR EXISTS(SELECT d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc EXCEPT SELECT i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc))) OR
                (d.State NOT IN(N'pending',N'running') AND EXISTS(SELECT d.State COLLATE Latin1_General_100_BIN2,DATALENGTH(d.State),d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc,d.FinishedAtUtc,d.ResultJson COLLATE Latin1_General_100_BIN2,DATALENGTH(d.ResultJson) EXCEPT SELECT i.State COLLATE Latin1_General_100_BIN2,DATALENGTH(i.State),i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc,i.FinishedAtUtc,i.ResultJson COLLATE Latin1_General_100_BIN2,DATALENGTH(i.ResultJson))) OR d.ResolvedAtUtc IS NOT NULL)
                THROW 51017,'Terminal results and owned claims are immutable.',1;
              IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN dbo.DeviceOperations s ON s.Id=TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.IdlePlanJson,'$.SnapshotId')) WHERE i.Kind=N'settings.idle.set' AND
                (s.Id IS NULL OR s.Kind<>N'settings.read' OR s.State<>N'succeeded' OR s.DeviceId<>i.DeviceId OR s.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR s.TimeZoneId<>i.TimeZoneId OR s.RequestedAtUtc>i.RequestedAtUtc OR s.RequestedAtUtc<DATEADD(MINUTE,-5,i.RequestedAtUtc)
                 OR JSON_VALUE(s.ResultJson,'$.Serial') IS NULL OR JSON_VALUE(s.ResultJson,'$.Product') IS NULL OR JSON_VALUE(s.ResultJson,'$.Firmware') IS NULL OR JSON_VALUE(s.ResultJson,'$.SleepTime') IS NULL
                 OR JSON_VALUE(s.ResultJson,'$.Serial') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.IdlePlanJson,'$.Serial') COLLATE Latin1_General_100_BIN2
                 OR JSON_VALUE(s.ResultJson,'$.Product') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.IdlePlanJson,'$.Product') COLLATE Latin1_General_100_BIN2
                 OR JSON_VALUE(s.ResultJson,'$.Firmware') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.IdlePlanJson,'$.Firmware') COLLATE Latin1_General_100_BIN2
                 OR JSON_VALUE(s.ResultJson,'$.SleepTime')<>JSON_VALUE(i.IdlePlanJson,'$.ExpectedMinutes')))
                THROW 51031,'Idle write must retain the exact fresh same-model preview snapshot.',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE i.Kind=N'settings.idle.set' AND i.State=N'succeeded' AND
                (JSON_VALUE(i.ResultJson,'$.Serial') IS NULL OR JSON_VALUE(i.ResultJson,'$.Product') IS NULL OR JSON_VALUE(i.ResultJson,'$.Firmware') IS NULL OR JSON_VALUE(i.ResultJson,'$.Minutes') IS NULL
                 OR JSON_VALUE(i.ResultJson,'$.Serial') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.IdlePlanJson,'$.Serial') COLLATE Latin1_General_100_BIN2
                 OR JSON_VALUE(i.ResultJson,'$.Product') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.IdlePlanJson,'$.Product') COLLATE Latin1_General_100_BIN2
                 OR JSON_VALUE(i.ResultJson,'$.Firmware') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.IdlePlanJson,'$.Firmware') COLLATE Latin1_General_100_BIN2
                 OR JSON_VALUE(i.ResultJson,'$.Minutes')<>JSON_VALUE(i.IdlePlanJson,'$.NewMinutes')))
                THROW 51031,'Idle success requires exact typed read-back.',1;
              IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN dbo.DeviceOperations p ON p.Id=TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.NetworkPlanJson,'$.ChangeId'))
                WHERE i.Kind=N'network.inspect' AND (p.Id IS NULL OR p.Kind<>N'network.ip.set' OR p.State<>N'unknown' OR p.DeviceId<>i.DeviceId OR p.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR i.RequestedAtUtc<=p.FinishedAtUtc
                 OR JSON_VALUE(p.NetworkPlanJson,'$.OldAddress')<>JSON_VALUE(i.NetworkPlanJson,'$.OldAddress') OR JSON_VALUE(p.NetworkPlanJson,'$.NewAddress')<>JSON_VALUE(i.NetworkPlanJson,'$.NewAddress') OR JSON_VALUE(p.NetworkPlanJson,'$.Serial') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.NetworkPlanJson,'$.Serial') COLLATE Latin1_General_100_BIN2))
                THROW 51030,'Network inspection must retain the approved same-device transition.',1;
              IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN dbo.DeviceOperations r ON r.Id=i.ResolutionInfoId WHERE i.ResolutionInfoId IS NOT NULL AND
                (r.Id IS NULL OR r.DeviceId<>i.DeviceId OR r.State<>N'succeeded' OR r.FinishedAtUtc<i.FinishedAtUtc OR r.RequestedAtUtc<=i.FinishedAtUtc OR r.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR r.TimeZoneId<>i.TimeZoneId
                 OR (i.Kind NOT IN(N'network.ip.set',N'settings.idle.set') AND (r.Kind<>N'information.read' OR JSON_VALUE(r.ResultJson,'$.ClockLocal') IS NULL))
                 OR (i.Kind=N'settings.idle.set' AND (r.Kind<>N'settings.read' OR JSON_VALUE(r.ResultJson,'$.Serial') IS NULL OR JSON_VALUE(r.ResultJson,'$.Product') IS NULL OR JSON_VALUE(r.ResultJson,'$.Firmware') IS NULL OR JSON_VALUE(r.ResultJson,'$.SleepTime') IS NULL
                  OR JSON_VALUE(r.ResultJson,'$.Serial') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.IdlePlanJson,'$.Serial') COLLATE Latin1_General_100_BIN2
                  OR JSON_VALUE(r.ResultJson,'$.Product') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.IdlePlanJson,'$.Product') COLLATE Latin1_General_100_BIN2
                  OR JSON_VALUE(r.ResultJson,'$.Firmware') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.IdlePlanJson,'$.Firmware') COLLATE Latin1_General_100_BIN2
                  OR JSON_VALUE(r.ResultJson,'$.SleepTime') NOT IN(JSON_VALUE(i.IdlePlanJson,'$.ExpectedMinutes'),JSON_VALUE(i.IdlePlanJson,'$.NewMinutes'))))
                 OR (i.Kind=N'network.ip.set' AND (r.Kind<>N'network.inspect' OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.NetworkPlanJson,'$.ChangeId'))<>i.Id
                  OR JSON_VALUE(r.NetworkPlanJson,'$.OldAddress')<>JSON_VALUE(i.NetworkPlanJson,'$.OldAddress') OR JSON_VALUE(r.NetworkPlanJson,'$.NewAddress')<>JSON_VALUE(i.NetworkPlanJson,'$.NewAddress')
                  OR JSON_VALUE(r.ResultJson,'$.Serial') IS NULL OR JSON_VALUE(r.ResultJson,'$.Serial') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.NetworkPlanJson,'$.Serial') COLLATE Latin1_General_100_BIN2
                  OR JSON_VALUE(r.ResultJson,'$.ConnectedAddress') IS NULL OR JSON_VALUE(r.ResultJson,'$.DeviceIp') IS NULL OR JSON_VALUE(r.ResultJson,'$.ConnectedAddress')<>JSON_VALUE(r.ResultJson,'$.DeviceIp')
                  OR JSON_VALUE(r.ResultJson,'$.ConnectedAddress')<>CASE WHEN JSON_VALUE(r.NetworkPlanJson,'$.ObserveNewAddress')=N'true' THEN JSON_VALUE(i.NetworkPlanJson,'$.NewAddress') ELSE JSON_VALUE(i.NetworkPlanJson,'$.OldAddress') END))))
                THROW 51017,'Review requires later valid evidence of the same unchanged device and exact operation.',1;
            END
        """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM dbo.DeviceOperations WHERE IdlePlanJson IS NOT NULL) THROW 51031,'Idle setting history prevents destructive downgrade.',1;");
        migrationBuilder.Sql("""
        CREATE OR ALTER TRIGGER dbo.TR_DeviceOperations_History ON dbo.DeviceOperations AFTER INSERT,UPDATE,DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51017,'Device operations retain history.',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.Id=i.Id) AND i.State<>N'pending') THROW 51017,'New operations must be pending.',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(
                SELECT d.DeviceId,d.Kind COLLATE Latin1_General_100_BIN2,DATALENGTH(d.Kind),d.RequestedByUserId,d.RequestedAtUtc,d.ExpectedDeviceVersion,d.TimeoutSeconds,d.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(d.TimeZoneId),d.TargetUtc,d.NetworkPlanJson COLLATE Latin1_General_100_BIN2,DATALENGTH(d.NetworkPlanJson)
                EXCEPT SELECT i.DeviceId,i.Kind COLLATE Latin1_General_100_BIN2,DATALENGTH(i.Kind),i.RequestedByUserId,i.RequestedAtUtc,i.ExpectedDeviceVersion,i.TimeoutSeconds,i.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(i.TimeZoneId),i.TargetUtc,i.NetworkPlanJson COLLATE Latin1_General_100_BIN2,DATALENGTH(i.NetworkPlanJson)))
                THROW 51017,'Operation request identity is immutable.',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE
                (d.State=N'pending' AND i.State NOT IN(N'running',N'cancelled',N'revoked',N'device-changed',N'expired')) OR
                (d.State=N'running' AND (i.State IN(N'pending',N'running') OR EXISTS(SELECT d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc EXCEPT SELECT i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc))) OR
                (d.State NOT IN(N'pending',N'running') AND EXISTS(SELECT d.State COLLATE Latin1_General_100_BIN2,DATALENGTH(d.State),d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc,d.FinishedAtUtc,d.ResultJson COLLATE Latin1_General_100_BIN2,DATALENGTH(d.ResultJson) EXCEPT SELECT i.State COLLATE Latin1_General_100_BIN2,DATALENGTH(i.State),i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc,i.FinishedAtUtc,i.ResultJson COLLATE Latin1_General_100_BIN2,DATALENGTH(i.ResultJson))) OR d.ResolvedAtUtc IS NOT NULL)
                THROW 51017,'Terminal results and owned claims are immutable.',1;
              IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN dbo.DeviceOperations p ON p.Id=TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.NetworkPlanJson,'$.ChangeId'))
                WHERE i.Kind=N'network.inspect' AND (p.Id IS NULL OR p.Kind<>N'network.ip.set' OR p.State<>N'unknown' OR p.DeviceId<>i.DeviceId OR p.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR i.RequestedAtUtc<=p.FinishedAtUtc
                 OR JSON_VALUE(p.NetworkPlanJson,'$.OldAddress')<>JSON_VALUE(i.NetworkPlanJson,'$.OldAddress') OR JSON_VALUE(p.NetworkPlanJson,'$.NewAddress')<>JSON_VALUE(i.NetworkPlanJson,'$.NewAddress') OR JSON_VALUE(p.NetworkPlanJson,'$.Serial') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.NetworkPlanJson,'$.Serial') COLLATE Latin1_General_100_BIN2))
                THROW 51030,'Network inspection must retain the approved same-device transition.',1;
              IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN dbo.DeviceOperations r ON r.Id=i.ResolutionInfoId WHERE i.ResolutionInfoId IS NOT NULL AND
                (r.Id IS NULL OR r.DeviceId<>i.DeviceId OR r.State<>N'succeeded' OR r.FinishedAtUtc<i.FinishedAtUtc OR r.RequestedAtUtc<=i.FinishedAtUtc OR r.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR r.TimeZoneId<>i.TimeZoneId
                 OR (i.Kind<>N'network.ip.set' AND (r.Kind<>N'information.read' OR JSON_VALUE(r.ResultJson,'$.ClockLocal') IS NULL))
                 OR (i.Kind=N'network.ip.set' AND (r.Kind<>N'network.inspect' OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.NetworkPlanJson,'$.ChangeId'))<>i.Id
                  OR JSON_VALUE(r.NetworkPlanJson,'$.OldAddress')<>JSON_VALUE(i.NetworkPlanJson,'$.OldAddress') OR JSON_VALUE(r.NetworkPlanJson,'$.NewAddress')<>JSON_VALUE(i.NetworkPlanJson,'$.NewAddress')
                  OR JSON_VALUE(r.ResultJson,'$.Serial') IS NULL OR JSON_VALUE(r.ResultJson,'$.Serial') COLLATE Latin1_General_100_BIN2<>JSON_VALUE(i.NetworkPlanJson,'$.Serial') COLLATE Latin1_General_100_BIN2
                  OR JSON_VALUE(r.ResultJson,'$.ConnectedAddress') IS NULL OR JSON_VALUE(r.ResultJson,'$.DeviceIp') IS NULL OR JSON_VALUE(r.ResultJson,'$.ConnectedAddress')<>JSON_VALUE(r.ResultJson,'$.DeviceIp')
                  OR JSON_VALUE(r.ResultJson,'$.ConnectedAddress')<>CASE WHEN JSON_VALUE(r.NetworkPlanJson,'$.ObserveNewAddress')=N'true' THEN JSON_VALUE(i.NetworkPlanJson,'$.NewAddress') ELSE JSON_VALUE(i.NetworkPlanJson,'$.OldAddress') END))))
                THROW 51017,'Review requires later valid evidence of the same unchanged device and exact operation.',1;
            END
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Request;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Result;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Resolution;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Idle;
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Request CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 60 AND (([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read',N'device.restart',N'network.ip.set',N'network.inspect') AND [TargetUtc] IS NULL) OR ([Kind]=N'clock.set' AND [TargetUtc] IS NOT NULL AND DATEPART(TZOFFSET,[TargetUtc])=0 AND DATEPART(NANOSECOND,[TargetUtc])=0)));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Result CHECK (([ResultJson] IS NULL OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read',N'network.inspect') AND [State]=N'succeeded' AND ISJSON([ResultJson])=1 AND DATALENGTH([ResultJson])<=8000)) AND ([Kind] COLLATE Latin1_General_100_BIN2 NOT IN(N'information.read',N'settings.read',N'network.inspect') OR [State]<>N'succeeded' OR [ResultJson] IS NOT NULL));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Resolution CHECK (([ResolutionInfoId] IS NULL AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL) OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'clock.set',N'device.restart',N'network.ip.set') AND [State]=N'unknown' AND [ResolutionInfoId] IS NOT NULL AND [ResolutionInfoId]<>[Id] AND [ResolvedByUserId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET,[ResolvedAtUtc])=0 AND [ResolvedAtUtc]>=[FinishedAtUtc]));
        ALTER TABLE dbo.DeviceOperations DROP COLUMN IdlePlanJson;
        """);
    }
}
