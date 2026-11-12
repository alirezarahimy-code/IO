using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112070000_DeviceUserUploads")]
public sealed class DeviceUserUploads:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE dbo.DeviceUserUploads(
             [Id] uniqueidentifier NOT NULL,
             [BatchId] uniqueidentifier NOT NULL,
             [RequestHash] nvarchar(64) NOT NULL,
             [DeviceId] uniqueidentifier NOT NULL,
             [EmployeeId] uniqueidentifier NOT NULL,
             [SnapshotId] uniqueidentifier NOT NULL,
             [RequestedByUserId] uniqueidentifier NOT NULL,
             [RequestedAtUtc] datetimeoffset NOT NULL,
             [ExpectedDeviceVersion] varbinary(8) NOT NULL,
             [PersonIdentifier] nvarchar(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
             [ExpectedJson] nvarchar(2000) NULL,
             [DesiredJson] nvarchar(2000) NOT NULL,
             [TimeoutSeconds] int NOT NULL,
             [State] nvarchar(32) NOT NULL,
             [ServiceUserId] uniqueidentifier NULL,
             [ClaimId] uniqueidentifier NULL,
             [ClaimedAtUtc] datetimeoffset NULL,
             [FinishedAtUtc] datetimeoffset NULL,
             [ResolutionSnapshotId] uniqueidentifier NULL,
             [ResolvedByUserId] uniqueidentifier NULL,
             [ResolvedAtUtc] datetimeoffset NULL,
             [Version] rowversion NOT NULL,
             CONSTRAINT PK_DeviceUserUploads PRIMARY KEY(Id),
             CONSTRAINT FK_DeviceUserUploads_Devices_DeviceId FOREIGN KEY([DeviceId]) REFERENCES dbo.Devices(Id),
             CONSTRAINT FK_DeviceUserUploads_Employees_EmployeeId FOREIGN KEY([EmployeeId]) REFERENCES dbo.Employees(Id),
             CONSTRAINT FK_DeviceUserUploads_DeviceUserDownloads_SnapshotId FOREIGN KEY([SnapshotId]) REFERENCES dbo.DeviceUserDownloads(Id),
             CONSTRAINT FK_DeviceUserUploads_Users_RequestedByUserId FOREIGN KEY([RequestedByUserId]) REFERENCES dbo.Users(Id),
             CONSTRAINT FK_DeviceUserUploads_Users_ServiceUserId FOREIGN KEY([ServiceUserId]) REFERENCES dbo.Users(Id),
             CONSTRAINT FK_DeviceUserUploads_DeviceUserDownloads_ResolutionSnapshotId FOREIGN KEY([ResolutionSnapshotId]) REFERENCES dbo.DeviceUserDownloads(Id),
             CONSTRAINT FK_DeviceUserUploads_Users_ResolvedByUserId FOREIGN KEY([ResolvedByUserId]) REFERENCES dbo.Users(Id),
             CONSTRAINT CK_DeviceUserUploads_Request CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND LEN([RequestHash])=64 AND DATALENGTH([PersonIdentifier]) BETWEEN 2 AND 80 AND [TimeoutSeconds] BETWEEN 1 AND 60 AND ISJSON([DesiredJson])=1 AND ([ExpectedJson] IS NULL OR ISJSON([ExpectedJson])=1)),
             CONSTRAINT CK_DeviceUserUploads_State CHECK ([State] COLLATE Latin1_General_100_BIN2 IN (N'pending',N'running',N'succeeded',N'unchanged',N'conflict',N'unsupported',N'identity-mismatch',N'failed-before-write',N'unknown',N'cancelled',N'revoked',N'device-changed')),
             CONSTRAINT CK_DeviceUserUploads_Lifecycle CHECK ((([State]=N'pending' AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [ClaimedAtUtc] IS NULL AND [FinishedAtUtc] IS NULL) OR ([State]=N'running' AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [ClaimedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([State] IN(N'cancelled',N'revoked',N'device-changed') AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [ClaimedAtUtc] IS NULL AND [FinishedAtUtc] IS NOT NULL) OR ([State] NOT IN(N'pending',N'running',N'cancelled',N'revoked',N'device-changed') AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [ClaimedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NOT NULL)) AND DATEPART(TZOFFSET,[RequestedAtUtc])=0 AND DATEPART(TZOFFSET,[ClaimedAtUtc])=0 AND DATEPART(TZOFFSET,[FinishedAtUtc])=0 AND ([ClaimedAtUtc] IS NULL OR [ClaimedAtUtc]>=[RequestedAtUtc]) AND ([FinishedAtUtc] IS NULL OR [FinishedAtUtc]>=COALESCE([ClaimedAtUtc],[RequestedAtUtc]))),
             CONSTRAINT CK_DeviceUserUploads_Resolution CHECK (([ResolutionSnapshotId] IS NULL AND [ResolvedByUserId] IS NULL AND [ResolvedAtUtc] IS NULL) OR ([State]=N'unknown' AND [ResolutionSnapshotId] IS NOT NULL AND [ResolvedByUserId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL AND [ResolvedAtUtc]>=[FinishedAtUtc] AND DATEPART(TZOFFSET,[ResolvedAtUtc])=0))
            );
            CREATE INDEX IX_DeviceUserUploads_BatchId_Id ON dbo.DeviceUserUploads(BatchId,Id);
            CREATE INDEX IX_DeviceUserUploads_RequestedAtUtc_Id ON dbo.DeviceUserUploads(RequestedAtUtc,Id);
            CREATE UNIQUE INDEX IX_DeviceUserUploads_DeviceId ON dbo.DeviceUserUploads(DeviceId) WHERE State=N'running';
            CREATE INDEX IX_DeviceUserUploads_EmployeeId ON dbo.DeviceUserUploads([EmployeeId]);
            CREATE INDEX IX_DeviceUserUploads_SnapshotId ON dbo.DeviceUserUploads([SnapshotId]);
            CREATE INDEX IX_DeviceUserUploads_RequestedByUserId ON dbo.DeviceUserUploads([RequestedByUserId]);
            CREATE INDEX IX_DeviceUserUploads_ServiceUserId ON dbo.DeviceUserUploads([ServiceUserId]);
            CREATE INDEX IX_DeviceUserUploads_ResolutionSnapshotId ON dbo.DeviceUserUploads([ResolutionSnapshotId]);
            CREATE INDEX IX_DeviceUserUploads_ResolvedByUserId ON dbo.DeviceUserUploads([ResolvedByUserId]);
            
            EXEC(N'CREATE TRIGGER dbo.TR_DeviceUserUploads_History ON dbo.DeviceUserUploads AFTER INSERT,UPDATE,DELETE AS
            BEGIN SET NOCOUNT ON;
             IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51020,''Upload history retained.'',1;
             IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.Id=i.Id) AND i.State<>N''pending'') THROW 51020,''Uploads start pending.'',1;
             IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(SELECT d.Id,d.BatchId,CONVERT(varbinary(max),d.RequestHash),d.DeviceId,d.EmployeeId,d.SnapshotId,d.RequestedByUserId,d.RequestedAtUtc,d.ExpectedDeviceVersion,CONVERT(varbinary(max),d.PersonIdentifier),CONVERT(varbinary(max),d.ExpectedJson),CONVERT(varbinary(max),d.DesiredJson),d.TimeoutSeconds EXCEPT SELECT i.Id,i.BatchId,CONVERT(varbinary(max),i.RequestHash),i.DeviceId,i.EmployeeId,i.SnapshotId,i.RequestedByUserId,i.RequestedAtUtc,i.ExpectedDeviceVersion,CONVERT(varbinary(max),i.PersonIdentifier),CONVERT(varbinary(max),i.ExpectedJson),CONVERT(varbinary(max),i.DesiredJson),i.TimeoutSeconds) OR
             (d.State=N''pending'' AND i.State NOT IN(N''running'',N''cancelled'',N''revoked'',N''device-changed'')) OR
             (d.State=N''running'' AND (i.State IN(N''pending'',N''running'',N''cancelled'',N''revoked'',N''device-changed'') OR EXISTS(SELECT d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc EXCEPT SELECT i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc))) OR
             (d.State NOT IN(N''pending'',N''running'') AND NOT(d.State=N''unknown'' AND i.State=N''unknown'' AND d.ResolutionSnapshotId IS NULL AND i.ResolutionSnapshotId IS NOT NULL AND i.ServiceUserId=d.ServiceUserId AND i.ClaimId=d.ClaimId AND i.ClaimedAtUtc=d.ClaimedAtUtc AND i.FinishedAtUtc=d.FinishedAtUtc)))
             THROW 51020,''Immutable upload intent and outcome.'',1;
            END');
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.DeviceUserUploads) THROW 51020,'Upload history prevents destructive downgrade.',1;
        DROP TABLE dbo.DeviceUserUploads;
        """);
}
