using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112040000_DeviceOperations")]
public sealed class DeviceOperations:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE dbo.DeviceOperations(
              Id uniqueidentifier NOT NULL CONSTRAINT PK_DeviceOperations PRIMARY KEY,
              DeviceId uniqueidentifier NOT NULL, Kind nvarchar(40) NOT NULL, RequestedByUserId uniqueidentifier NOT NULL,
              RequestedAtUtc datetimeoffset NOT NULL, ExpectedDeviceVersion varbinary(8) NOT NULL, TimeoutSeconds int NOT NULL,
              TimeZoneId nvarchar(100) NOT NULL, TargetUtc datetimeoffset NULL, State nvarchar(32) NOT NULL,
              ServiceUserId uniqueidentifier NULL, ClaimId uniqueidentifier NULL, ClaimedAtUtc datetimeoffset NULL, FinishedAtUtc datetimeoffset NULL,
              ResultJson nvarchar(4096) NULL, ResolutionInfoId uniqueidentifier NULL, ResolvedByUserId uniqueidentifier NULL, ResolvedAtUtc datetimeoffset NULL, Version rowversion NOT NULL,
              CONSTRAINT FK_DeviceOperations_Devices_DeviceId FOREIGN KEY(DeviceId) REFERENCES dbo.Devices(Id),
              CONSTRAINT FK_DeviceOperations_Users_RequestedByUserId FOREIGN KEY(RequestedByUserId) REFERENCES dbo.Users(Id),
              CONSTRAINT FK_DeviceOperations_Users_ServiceUserId FOREIGN KEY(ServiceUserId) REFERENCES dbo.Users(Id),
              CONSTRAINT FK_DeviceOperations_Users_ResolvedByUserId FOREIGN KEY(ResolvedByUserId) REFERENCES dbo.Users(Id),
              CONSTRAINT FK_DeviceOperations_DeviceOperations_ResolutionInfoId FOREIGN KEY(ResolutionInfoId) REFERENCES dbo.DeviceOperations(Id),
              CONSTRAINT CK_DeviceOperations_Request CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 60 AND (([Kind]=N'information.read' AND [TargetUtc] IS NULL) OR ([Kind]=N'clock.set' AND [TargetUtc] IS NOT NULL AND DATEPART(TZOFFSET,[TargetUtc])=0 AND DATEPART(NANOSECOND,[TargetUtc])=0))),
              CONSTRAINT CK_DeviceOperations_State CHECK ([State] COLLATE Latin1_General_100_BIN2 IN (N'pending',N'running',N'succeeded',N'failed',N'unsupported',N'unknown',N'interrupted',N'timed-out',N'identity-mismatch',N'credential-unavailable',N'bridge-failed',N'endpoint-denied',N'cancelled',N'revoked',N'device-changed',N'expired')),
              CONSTRAINT CK_DeviceOperations_Lifecycle CHECK ((([State]=N'pending' AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [ClaimedAtUtc] IS NULL AND [FinishedAtUtc] IS NULL) OR ([State]=N'running' AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [ClaimedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([State] IN (N'cancelled',N'revoked',N'device-changed',N'expired') AND [FinishedAtUtc] IS NOT NULL AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [ClaimedAtUtc] IS NULL) OR ([State] NOT IN (N'pending',N'running',N'cancelled',N'revoked',N'device-changed',N'expired') AND [FinishedAtUtc] IS NOT NULL AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [ClaimedAtUtc] IS NOT NULL AND [FinishedAtUtc]>=[ClaimedAtUtc])) AND DATEPART(TZOFFSET,[RequestedAtUtc])=0 AND DATEPART(TZOFFSET,[ClaimedAtUtc])=0 AND DATEPART(TZOFFSET,[FinishedAtUtc])=0 AND ([ClaimedAtUtc] IS NULL OR [ClaimedAtUtc]>=[RequestedAtUtc]) AND ([FinishedAtUtc] IS NULL OR [FinishedAtUtc]>=[RequestedAtUtc])),
              CONSTRAINT CK_DeviceOperations_Result CHECK (([ResultJson] IS NULL OR ([Kind]=N'information.read' AND [State]=N'succeeded' AND ISJSON([ResultJson])=1 AND DATALENGTH([ResultJson])<=8192)) AND ([Kind]<>N'information.read' OR [State]<>N'succeeded' OR [ResultJson] IS NOT NULL)),
              CONSTRAINT CK_DeviceOperations_Resolution CHECK (([ResolutionInfoId] IS NULL AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL) OR ([Kind]=N'clock.set' AND [State]=N'unknown' AND [ResolutionInfoId] IS NOT NULL AND [ResolutionInfoId]<>[Id] AND [ResolvedByUserId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET,[ResolvedAtUtc])=0 AND [ResolvedAtUtc]>=[FinishedAtUtc]))
            );
            CREATE UNIQUE INDEX IX_DeviceOperations_DeviceId ON dbo.DeviceOperations(DeviceId) WHERE State IN (N'pending',N'running');
            CREATE INDEX IX_DeviceOperations_DeviceId_RequestedAtUtc ON dbo.DeviceOperations(DeviceId,RequestedAtUtc);
            CREATE INDEX IX_DeviceOperations_RequestedByUserId ON dbo.DeviceOperations(RequestedByUserId);
            CREATE INDEX IX_DeviceOperations_ServiceUserId ON dbo.DeviceOperations(ServiceUserId);
            CREATE INDEX IX_DeviceOperations_ResolvedByUserId ON dbo.DeviceOperations(ResolvedByUserId);
            CREATE INDEX IX_DeviceOperations_ResolutionInfoId ON dbo.DeviceOperations(ResolutionInfoId);
            """);
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER dbo.TR_DeviceOperations_History ON dbo.DeviceOperations AFTER INSERT,UPDATE,DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51017,''Device operations retain history.'',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.Id=i.Id) AND i.State<>N''pending'') THROW 51017,''New operations must be pending.'',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(
                SELECT d.DeviceId,d.Kind COLLATE Latin1_General_100_BIN2,DATALENGTH(d.Kind),d.RequestedByUserId,d.RequestedAtUtc,d.ExpectedDeviceVersion,d.TimeoutSeconds,d.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(d.TimeZoneId),d.TargetUtc
                EXCEPT SELECT i.DeviceId,i.Kind COLLATE Latin1_General_100_BIN2,DATALENGTH(i.Kind),i.RequestedByUserId,i.RequestedAtUtc,i.ExpectedDeviceVersion,i.TimeoutSeconds,i.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(i.TimeZoneId),i.TargetUtc))
                THROW 51017,''Operation request identity is immutable.'',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE
                (d.State=N''pending'' AND i.State NOT IN(N''running'',N''cancelled'',N''revoked'',N''device-changed'',N''expired'')) OR
                (d.State=N''running'' AND (i.State IN(N''pending'',N''running'') OR EXISTS(SELECT d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc EXCEPT SELECT i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc))) OR
                (d.State NOT IN(N''pending'',N''running'') AND EXISTS(SELECT d.State COLLATE Latin1_General_100_BIN2,DATALENGTH(d.State),d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc,d.FinishedAtUtc,d.ResultJson COLLATE Latin1_General_100_BIN2,DATALENGTH(d.ResultJson) EXCEPT SELECT i.State COLLATE Latin1_General_100_BIN2,DATALENGTH(i.State),i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc,i.FinishedAtUtc,i.ResultJson COLLATE Latin1_General_100_BIN2,DATALENGTH(i.ResultJson))) OR d.ResolvedAtUtc IS NOT NULL)
                THROW 51017,''Terminal results and owned claims are immutable.'',1;
              IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN dbo.DeviceOperations r ON r.Id=i.ResolutionInfoId WHERE i.ResolutionInfoId IS NOT NULL AND
                (r.Id IS NULL OR r.DeviceId<>i.DeviceId OR r.Kind<>N''information.read'' OR r.State<>N''succeeded'' OR r.FinishedAtUtc<i.FinishedAtUtc OR r.RequestedAtUtc<=i.FinishedAtUtc OR r.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR JSON_VALUE(r.ResultJson,''$.ClockLocal'') IS NULL))
                THROW 51017,''Clock review requires a later valid observation of the same unchanged device.'',1;
            END');
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.DeviceOperations) THROW 51017,'Operation history prevents destructive downgrade.',1;
        DROP TABLE dbo.DeviceOperations;
        """);
}
