using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112190000_DeviceNetworkIp")]
public sealed class DeviceNetworkIp:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE dbo.DeviceOperations ADD NetworkPlanJson nvarchar(1000) NULL;");
        migrationBuilder.Sql("""
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Request;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Result;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Resolution;
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Request CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 60 AND (([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read',N'device.restart',N'network.ip.set',N'network.inspect') AND [TargetUtc] IS NULL) OR ([Kind]=N'clock.set' AND [TargetUtc] IS NOT NULL AND DATEPART(TZOFFSET,[TargetUtc])=0 AND DATEPART(NANOSECOND,[TargetUtc])=0)));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Result CHECK (([ResultJson] IS NULL OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read',N'network.inspect') AND [State]=N'succeeded' AND ISJSON([ResultJson])=1 AND DATALENGTH([ResultJson])<=8000)) AND ([Kind] COLLATE Latin1_General_100_BIN2 NOT IN(N'information.read',N'settings.read',N'network.inspect') OR [State]<>N'succeeded' OR [ResultJson] IS NOT NULL));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Resolution CHECK (([ResolutionInfoId] IS NULL AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL) OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'clock.set',N'device.restart',N'network.ip.set') AND [State]=N'unknown' AND [ResolutionInfoId] IS NOT NULL AND [ResolutionInfoId]<>[Id] AND [ResolvedByUserId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET,[ResolvedAtUtc])=0 AND [ResolvedAtUtc]>=[FinishedAtUtc]));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Network CHECK (([Kind] COLLATE Latin1_General_100_BIN2 NOT IN(N'network.ip.set',N'network.inspect') AND [NetworkPlanJson] IS NULL) OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'network.ip.set',N'network.inspect') AND [NetworkPlanJson] IS NOT NULL AND ISJSON([NetworkPlanJson])=1 AND DATALENGTH([NetworkPlanJson])<=2000 AND JSON_VALUE([NetworkPlanJson],'$.OldAddress') IS NOT NULL AND JSON_VALUE([NetworkPlanJson],'$.NewAddress') IS NOT NULL AND JSON_VALUE([NetworkPlanJson],'$.Serial') IS NOT NULL AND JSON_VALUE([NetworkPlanJson],'$.OldAddress')<>JSON_VALUE([NetworkPlanJson],'$.NewAddress') AND (([Kind]=N'network.ip.set' AND JSON_VALUE([NetworkPlanJson],'$.ChangeId') IS NULL AND JSON_VALUE([NetworkPlanJson],'$.ObserveNewAddress')=N'false' AND [State]<>N'succeeded') OR ([Kind]=N'network.inspect' AND TRY_CONVERT(uniqueidentifier,JSON_VALUE([NetworkPlanJson],'$.ChangeId')) IS NOT NULL AND JSON_VALUE([NetworkPlanJson],'$.ObserveNewAddress') IN(N'true',N'false')))));
        """);
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
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM dbo.DeviceOperations WHERE NetworkPlanJson IS NOT NULL) THROW 51030,'Network transition history prevents destructive downgrade.',1;");
        migrationBuilder.Sql("""
        CREATE OR ALTER TRIGGER dbo.TR_DeviceOperations_History ON dbo.DeviceOperations AFTER INSERT,UPDATE,DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51017,'Device operations retain history.',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.Id=i.Id) AND i.State<>N'pending') THROW 51017,'New operations must be pending.',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(
                SELECT d.DeviceId,d.Kind COLLATE Latin1_General_100_BIN2,DATALENGTH(d.Kind),d.RequestedByUserId,d.RequestedAtUtc,d.ExpectedDeviceVersion,d.TimeoutSeconds,d.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(d.TimeZoneId),d.TargetUtc
                EXCEPT SELECT i.DeviceId,i.Kind COLLATE Latin1_General_100_BIN2,DATALENGTH(i.Kind),i.RequestedByUserId,i.RequestedAtUtc,i.ExpectedDeviceVersion,i.TimeoutSeconds,i.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(i.TimeZoneId),i.TargetUtc))
                THROW 51017,'Operation request identity is immutable.',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE
                (d.State=N'pending' AND i.State NOT IN(N'running',N'cancelled',N'revoked',N'device-changed',N'expired')) OR
                (d.State=N'running' AND (i.State IN(N'pending',N'running') OR EXISTS(SELECT d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc EXCEPT SELECT i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc))) OR
                (d.State NOT IN(N'pending',N'running') AND EXISTS(SELECT d.State COLLATE Latin1_General_100_BIN2,DATALENGTH(d.State),d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc,d.FinishedAtUtc,d.ResultJson COLLATE Latin1_General_100_BIN2,DATALENGTH(d.ResultJson) EXCEPT SELECT i.State COLLATE Latin1_General_100_BIN2,DATALENGTH(i.State),i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc,i.FinishedAtUtc,i.ResultJson COLLATE Latin1_General_100_BIN2,DATALENGTH(i.ResultJson))) OR d.ResolvedAtUtc IS NOT NULL)
                THROW 51017,'Terminal results and owned claims are immutable.',1;
              IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN dbo.DeviceOperations r ON r.Id=i.ResolutionInfoId WHERE i.ResolutionInfoId IS NOT NULL AND
                (r.Id IS NULL OR r.DeviceId<>i.DeviceId OR r.Kind<>N'information.read' OR r.State<>N'succeeded' OR r.FinishedAtUtc<i.FinishedAtUtc OR r.RequestedAtUtc<=i.FinishedAtUtc OR r.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR JSON_VALUE(r.ResultJson,'$.ClockLocal') IS NULL))
                THROW 51017,'Clock review requires a later valid observation of the same unchanged device.',1;
            END
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Request;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Result;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Resolution;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Network;
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Request CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 60 AND (([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read',N'device.restart') AND [TargetUtc] IS NULL) OR ([Kind]=N'clock.set' AND [TargetUtc] IS NOT NULL AND DATEPART(TZOFFSET,[TargetUtc])=0 AND DATEPART(NANOSECOND,[TargetUtc])=0)));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Result CHECK (([ResultJson] IS NULL OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read') AND [State]=N'succeeded' AND ISJSON([ResultJson])=1 AND DATALENGTH([ResultJson])<=8000)) AND ([Kind] COLLATE Latin1_General_100_BIN2 NOT IN(N'information.read',N'settings.read') OR [State]<>N'succeeded' OR [ResultJson] IS NOT NULL));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Resolution CHECK (([ResolutionInfoId] IS NULL AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL) OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'clock.set',N'device.restart') AND [State]=N'unknown' AND [ResolutionInfoId] IS NOT NULL AND [ResolutionInfoId]<>[Id] AND [ResolvedByUserId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET,[ResolvedAtUtc])=0 AND [ResolvedAtUtc]>=[FinishedAtUtc]));
        ALTER TABLE dbo.DeviceOperations DROP COLUMN NetworkPlanJson;
        """);
    }
}
