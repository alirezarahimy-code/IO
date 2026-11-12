using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112050000_DeviceUserDownloads")]
public sealed class DeviceUserDownloads:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE dbo.DeviceUserDownloads(
              [Id] uniqueidentifier NOT NULL,
              [DeviceId] uniqueidentifier NOT NULL,
              [SourceId] uniqueidentifier NOT NULL,
              [CompanyId] uniqueidentifier NOT NULL,
              [RequestedByUserId] uniqueidentifier NOT NULL,
              [RequestedAtUtc] datetimeoffset NOT NULL,
              [ExpectedDeviceVersion] varbinary(8) NOT NULL,
              [TimeoutSeconds] int NOT NULL,
              [State] nvarchar(32) NOT NULL,
              [ServiceUserId] uniqueidentifier NULL,
              [ClaimId] uniqueidentifier NULL,
              [LeaseAtUtc] datetimeoffset NULL,
              [FinishedAtUtc] datetimeoffset NULL,
              [ReceivedCount] bigint NOT NULL,
              [Version] rowversion NOT NULL,
              CONSTRAINT PK_DeviceUserDownloads PRIMARY KEY (Id),
              CONSTRAINT FK_DeviceUserDownloads_Devices_DeviceId FOREIGN KEY([DeviceId]) REFERENCES dbo.[Devices](Id),
              CONSTRAINT FK_DeviceUserDownloads_EventSources_SourceId FOREIGN KEY([SourceId]) REFERENCES dbo.[EventSources](Id),
              CONSTRAINT FK_DeviceUserDownloads_Companies_CompanyId FOREIGN KEY([CompanyId]) REFERENCES dbo.[Companies](Id),
              CONSTRAINT FK_DeviceUserDownloads_Users_RequestedByUserId FOREIGN KEY([RequestedByUserId]) REFERENCES dbo.[Users](Id),
              CONSTRAINT FK_DeviceUserDownloads_Users_ServiceUserId FOREIGN KEY([ServiceUserId]) REFERENCES dbo.[Users](Id),
              CONSTRAINT CK_DeviceUserDownloads_Request CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 600 AND [ReceivedCount]>=0 AND DATEPART(TZOFFSET,[RequestedAtUtc])=0),
              CONSTRAINT CK_DeviceUserDownloads_State CHECK ([State] COLLATE Latin1_General_100_BIN2 IN (N'pending',N'running',N'succeeded',N'incomplete',N'failed',N'interrupted',N'timed-out',N'unsupported',N'identity-mismatch',N'credential-unavailable',N'endpoint-denied',N'bridge-failed',N'cancelled',N'revoked',N'device-changed')),
              CONSTRAINT CK_DeviceUserDownloads_Lifecycle CHECK ((([State]=N'pending' AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [LeaseAtUtc] IS NULL AND [FinishedAtUtc] IS NULL AND [ReceivedCount]=0) OR ([State]=N'running' AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [LeaseAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([State] IN(N'cancelled',N'revoked',N'device-changed') AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [LeaseAtUtc] IS NULL AND [FinishedAtUtc] IS NOT NULL AND [ReceivedCount]=0) OR ([State] NOT IN(N'pending',N'running',N'cancelled',N'revoked',N'device-changed') AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [LeaseAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NOT NULL)) AND DATEPART(TZOFFSET,[LeaseAtUtc])=0 AND DATEPART(TZOFFSET,[FinishedAtUtc])=0 AND ([LeaseAtUtc] IS NULL OR [LeaseAtUtc]>=[RequestedAtUtc]) AND ([FinishedAtUtc] IS NULL OR [FinishedAtUtc]>=COALESCE([LeaseAtUtc],[RequestedAtUtc])))
            );
            CREATE UNIQUE INDEX IX_DeviceUserDownloads_DeviceId ON dbo.DeviceUserDownloads(DeviceId) WHERE State IN(N'pending',N'running');
            CREATE INDEX IX_DeviceUserDownloads_RequestedAtUtc_Id ON dbo.DeviceUserDownloads(RequestedAtUtc,Id);
            CREATE INDEX IX_DeviceUserDownloads_SourceId ON dbo.DeviceUserDownloads(SourceId);
            CREATE INDEX IX_DeviceUserDownloads_CompanyId ON dbo.DeviceUserDownloads(CompanyId);
            CREATE INDEX IX_DeviceUserDownloads_RequestedByUserId ON dbo.DeviceUserDownloads(RequestedByUserId);
            CREATE INDEX IX_DeviceUserDownloads_ServiceUserId ON dbo.DeviceUserDownloads(ServiceUserId);
            CREATE TABLE dbo.DeviceDownloadedUsers(
              [DownloadId] uniqueidentifier NOT NULL,
              [Sequence] bigint NOT NULL,
              [PersonIdentifier] nvarchar(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
              [Name] nvarchar(200) NOT NULL,
              [Card] nvarchar(40) NULL,
              [Privilege] int NOT NULL,
              [Enabled] bit NOT NULL,
              [Fingerprint] nvarchar(64) NOT NULL,
              CONSTRAINT PK_DeviceDownloadedUsers PRIMARY KEY (DownloadId,Sequence),
              CONSTRAINT FK_DeviceDownloadedUsers_DeviceUserDownloads_DownloadId FOREIGN KEY([DownloadId]) REFERENCES dbo.[DeviceUserDownloads](Id),
              CONSTRAINT CK_DeviceDownloadedUsers_Data CHECK ([Sequence]>0 AND LEN([PersonIdentifier]) BETWEEN 1 AND 40 AND [Privilege] BETWEEN 0 AND 3 AND LEN([Fingerprint])=64)
            );
            CREATE INDEX IX_DeviceDownloadedUsers_DownloadId_PersonIdentifier ON dbo.DeviceDownloadedUsers(DownloadId,PersonIdentifier);
            """);
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER dbo.TR_DeviceUserDownloads_History ON dbo.DeviceUserDownloads AFTER INSERT,UPDATE,DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51018,''Download history is retained.'',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.Id=i.Id) AND i.State<>N''pending'') THROW 51018,''Downloads start pending.'',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(SELECT d.DeviceId,d.SourceId,d.CompanyId,d.RequestedByUserId,d.RequestedAtUtc,d.ExpectedDeviceVersion,d.TimeoutSeconds EXCEPT SELECT i.DeviceId,i.SourceId,i.CompanyId,i.RequestedByUserId,i.RequestedAtUtc,i.ExpectedDeviceVersion,i.TimeoutSeconds) OR
                 d.State NOT IN(N''pending'',N''running'') OR
                 (d.State=N''pending'' AND i.State NOT IN(N''running'',N''cancelled'',N''revoked'',N''device-changed'')) OR
                 (d.State=N''running'' AND (i.State=N''pending'' OR i.ReceivedCount<d.ReceivedCount OR EXISTS(SELECT d.ServiceUserId,d.ClaimId EXCEPT SELECT i.ServiceUserId,i.ClaimId))))
                 THROW 51018,''Request identity and completed snapshots are immutable.'',1;
            END');
            EXEC(N'CREATE TRIGGER dbo.TR_DeviceDownloadedUsers_History ON dbo.DeviceDownloadedUsers AFTER INSERT,UPDATE,DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted) OR EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceUserDownloads d ON d.Id=i.DownloadId WHERE d.State<>N''running'') THROW 51018,''Downloaded rows are immutable and belong to a running claim.'',1;
            END');
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.DeviceUserDownloads) THROW 51018,'Download history prevents destructive downgrade.',1;
        DROP TABLE dbo.DeviceDownloadedUsers;
        DROP TABLE dbo.DeviceUserDownloads;
        """);
}
