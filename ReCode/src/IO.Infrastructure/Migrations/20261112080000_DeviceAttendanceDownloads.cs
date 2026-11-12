using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112080000_DeviceAttendanceDownloads")]
public sealed class DeviceAttendanceDownloads:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        CREATE TABLE dbo.DeviceAttendanceDownloads(
         [Id] uniqueidentifier NOT NULL,
         [DeviceId] uniqueidentifier NOT NULL,
         [SourceId] uniqueidentifier NOT NULL,
         [SiteId] uniqueidentifier NOT NULL,
         [CompanyId] uniqueidentifier NOT NULL,
         [RequestedByUserId] uniqueidentifier NOT NULL,
         [RequestedAtUtc] datetimeoffset NOT NULL,
         [ExpectedDeviceVersion] varbinary(8) NOT NULL,
         [FromUtc] datetimeoffset NOT NULL,
         [UntilUtcExclusive] datetimeoffset NOT NULL,
         [TimeZoneId] nvarchar(100) NOT NULL,
         [InCode] int NOT NULL,
         [OutCode] int NOT NULL,
         [TimeoutSeconds] int NOT NULL,
         [State] nvarchar(32) NOT NULL,
         [ServiceUserId] uniqueidentifier NULL,
         [ClaimId] uniqueidentifier NULL,
         [LeaseAtUtc] datetimeoffset NULL,
         [FinishedAtUtc] datetimeoffset NULL,
         [ReceivedCount] bigint NOT NULL,
         [LastReviewedAtUtc] datetimeoffset NULL,
         [Version] rowversion NOT NULL,
         CONSTRAINT PK_DeviceAttendanceDownloads PRIMARY KEY(Id),
         CONSTRAINT FK_DeviceAttendanceDownloads_Devices_DeviceId FOREIGN KEY([DeviceId]) REFERENCES dbo.Devices(Id),
         CONSTRAINT FK_DeviceAttendanceDownloads_EventSources_SourceId FOREIGN KEY([SourceId]) REFERENCES dbo.EventSources(Id),
         CONSTRAINT FK_DeviceAttendanceDownloads_Sites_SiteId FOREIGN KEY([SiteId]) REFERENCES dbo.Sites(Id),
         CONSTRAINT FK_DeviceAttendanceDownloads_Companies_CompanyId FOREIGN KEY([CompanyId]) REFERENCES dbo.Companies(Id),
         CONSTRAINT FK_DeviceAttendanceDownloads_Users_RequestedByUserId FOREIGN KEY([RequestedByUserId]) REFERENCES dbo.Users(Id),
         CONSTRAINT FK_DeviceAttendanceDownloads_Users_ServiceUserId FOREIGN KEY([ServiceUserId]) REFERENCES dbo.Users(Id),
         CONSTRAINT CK_DeviceAttendanceDownloads_Request CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 600 AND [ReceivedCount]>=0 AND [FromUtc]<[UntilUtcExclusive] AND DATEPART(TZOFFSET,[FromUtc])=0 AND DATEPART(TZOFFSET,[UntilUtcExclusive])=0 AND [InCode] BETWEEN 0 AND 255 AND [OutCode] BETWEEN 0 AND 255 AND [InCode]<>[OutCode]),
         CONSTRAINT CK_DeviceAttendanceDownloads_State CHECK ([State] COLLATE Latin1_General_100_BIN2 IN(N'pending',N'running',N'succeeded',N'incomplete',N'failed',N'interrupted',N'timed-out',N'unsupported',N'identity-mismatch',N'credential-unavailable',N'endpoint-denied',N'bridge-failed',N'cancelled',N'revoked',N'device-changed')),
         CONSTRAINT CK_DeviceAttendanceDownloads_Lifecycle CHECK ((([State]=N'pending' AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [LeaseAtUtc] IS NULL AND [FinishedAtUtc] IS NULL AND [ReceivedCount]=0) OR ([State]=N'running' AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [LeaseAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([State] IN(N'cancelled',N'revoked',N'device-changed') AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [LeaseAtUtc] IS NULL AND [FinishedAtUtc] IS NOT NULL AND [ReceivedCount]=0) OR ([State] NOT IN(N'pending',N'running',N'cancelled',N'revoked',N'device-changed') AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [LeaseAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NOT NULL)) AND DATEPART(TZOFFSET,[RequestedAtUtc])=0 AND DATEPART(TZOFFSET,[LeaseAtUtc])=0 AND DATEPART(TZOFFSET,[FinishedAtUtc])=0 AND DATEPART(TZOFFSET,[LastReviewedAtUtc])=0 AND ([LeaseAtUtc] IS NULL OR [LeaseAtUtc]>=[RequestedAtUtc]) AND ([FinishedAtUtc] IS NULL OR [FinishedAtUtc]>=COALESCE([LeaseAtUtc],[RequestedAtUtc])) AND ([LastReviewedAtUtc] IS NULL OR ([State]=N'succeeded' AND [LastReviewedAtUtc]>=[FinishedAtUtc])))
        );
        CREATE UNIQUE INDEX IX_DeviceAttendanceDownloads_DeviceId ON dbo.DeviceAttendanceDownloads(DeviceId) WHERE State IN(N'pending',N'running');
        CREATE INDEX IX_DeviceAttendanceDownloads_RequestedAtUtc_Id ON dbo.DeviceAttendanceDownloads(RequestedAtUtc,Id);
        CREATE INDEX IX_DeviceAttendanceDownloads_SourceId ON dbo.DeviceAttendanceDownloads([SourceId]);
        CREATE INDEX IX_DeviceAttendanceDownloads_SiteId ON dbo.DeviceAttendanceDownloads([SiteId]);
        CREATE INDEX IX_DeviceAttendanceDownloads_CompanyId ON dbo.DeviceAttendanceDownloads([CompanyId]);
        CREATE INDEX IX_DeviceAttendanceDownloads_RequestedByUserId ON dbo.DeviceAttendanceDownloads([RequestedByUserId]);
        CREATE INDEX IX_DeviceAttendanceDownloads_ServiceUserId ON dbo.DeviceAttendanceDownloads([ServiceUserId]);
        CREATE TABLE dbo.DeviceAttendanceRows(
         [DownloadId] uniqueidentifier NOT NULL,
         [Sequence] bigint NOT NULL,
         [PayloadJson] nvarchar(2000) NOT NULL,
         [PayloadHash] nvarchar(64) NOT NULL,
         [OccurrenceOrdinal] bigint NOT NULL,
         [Status] nvarchar(64) NOT NULL,
         [RawEventId] uniqueidentifier NULL,
         [ConflictId] uniqueidentifier NULL,
         [ReviewedByUserId] uniqueidentifier NULL,
         [ReviewedAtUtc] datetimeoffset NULL,
         CONSTRAINT PK_DeviceAttendanceRows PRIMARY KEY(DownloadId,Sequence),
         CONSTRAINT FK_DeviceAttendanceRows_DeviceAttendanceDownloads_DownloadId FOREIGN KEY([DownloadId]) REFERENCES dbo.DeviceAttendanceDownloads(Id),
         CONSTRAINT FK_DeviceAttendanceRows_RawAttendanceEvents_RawEventId FOREIGN KEY([RawEventId]) REFERENCES dbo.RawAttendanceEvents(Id),
         CONSTRAINT FK_DeviceAttendanceRows_RawEventConflicts_ConflictId FOREIGN KEY([ConflictId]) REFERENCES dbo.RawEventConflicts(Id),
         CONSTRAINT FK_DeviceAttendanceRows_Users_ReviewedByUserId FOREIGN KEY([ReviewedByUserId]) REFERENCES dbo.Users(Id),
         CONSTRAINT CK_DeviceAttendanceRows_Data CHECK ([Sequence]>0 AND [OccurrenceOrdinal]>0 AND ISJSON([PayloadJson])=1 AND LEN([PayloadHash])=64),
         CONSTRAINT CK_DeviceAttendanceRows_Receipt CHECK ((([ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL AND [Status]=N'received' AND [RawEventId] IS NULL AND [ConflictId] IS NULL) OR ([ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET,[ReviewedAtUtc])=0)) AND ([ConflictId] IS NULL OR [RawEventId] IS NOT NULL) AND (([Status] IN(N'imported',N'already-recorded',N'ingestion-conflict') AND [RawEventId] IS NOT NULL) OR ([Status] NOT IN(N'imported',N'already-recorded',N'ingestion-conflict') AND [RawEventId] IS NULL AND [ConflictId] IS NULL)))
        );
        CREATE UNIQUE INDEX IX_DeviceAttendanceRows_DownloadId_PayloadHash_OccurrenceOrdinal ON dbo.DeviceAttendanceRows(DownloadId,PayloadHash,OccurrenceOrdinal);
        CREATE INDEX IX_DeviceAttendanceRows_RawEventId ON dbo.DeviceAttendanceRows([RawEventId]);
        CREATE INDEX IX_DeviceAttendanceRows_ConflictId ON dbo.DeviceAttendanceRows([ConflictId]);
        CREATE INDEX IX_DeviceAttendanceRows_ReviewedByUserId ON dbo.DeviceAttendanceRows([ReviewedByUserId]);
        
        EXEC(N'CREATE TRIGGER dbo.TR_DeviceAttendanceDownloads_History ON dbo.DeviceAttendanceDownloads AFTER INSERT,UPDATE,DELETE AS
        BEGIN SET NOCOUNT ON;
         IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51021,''SDK extraction history retained.'',1;
         IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.Id=i.Id) AND i.State<>N''pending'') THROW 51021,''Extraction starts pending.'',1;
         IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(SELECT d.Id,d.DeviceId,d.SourceId,d.SiteId,d.CompanyId,d.RequestedByUserId,d.RequestedAtUtc,d.ExpectedDeviceVersion,d.FromUtc,d.UntilUtcExclusive,CONVERT(varbinary(max),d.TimeZoneId),d.InCode,d.OutCode,d.TimeoutSeconds EXCEPT SELECT i.Id,i.DeviceId,i.SourceId,i.SiteId,i.CompanyId,i.RequestedByUserId,i.RequestedAtUtc,i.ExpectedDeviceVersion,i.FromUtc,i.UntilUtcExclusive,CONVERT(varbinary(max),i.TimeZoneId),i.InCode,i.OutCode,i.TimeoutSeconds) OR
         (d.State=N''pending'' AND i.State NOT IN(N''running'',N''cancelled'',N''revoked'',N''device-changed'')) OR
         (d.State=N''running'' AND (i.State=N''pending'' OR i.ReceivedCount<d.ReceivedCount OR EXISTS(SELECT d.ServiceUserId,d.ClaimId EXCEPT SELECT i.ServiceUserId,i.ClaimId))) OR
         (d.State NOT IN(N''pending'',N''running'') AND EXISTS(SELECT CONVERT(varbinary(max),d.State),d.ServiceUserId,d.ClaimId,d.LeaseAtUtc,d.FinishedAtUtc,d.ReceivedCount EXCEPT SELECT CONVERT(varbinary(max),i.State),i.ServiceUserId,i.ClaimId,i.LeaseAtUtc,i.FinishedAtUtc,i.ReceivedCount))) THROW 51021,''Immutable extraction identity and terminal outcome.'',1;
        END');
        EXEC(N'CREATE TRIGGER dbo.TR_DeviceAttendanceRows_History ON dbo.DeviceAttendanceRows AFTER INSERT,UPDATE,DELETE AS
        BEGIN SET NOCOUNT ON;
         IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.DownloadId=d.DownloadId AND i.Sequence=d.Sequence)) THROW 51021,''SDK tuples retained.'',1;
         IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceAttendanceDownloads j ON j.Id=i.DownloadId WHERE
         (NOT EXISTS(SELECT 1 FROM deleted d WHERE d.DownloadId=i.DownloadId AND d.Sequence=i.Sequence) AND j.State<>N''running'') OR
         (EXISTS(SELECT 1 FROM deleted d WHERE d.DownloadId=i.DownloadId AND d.Sequence=i.Sequence) AND j.State<>N''succeeded'')) THROW 51021,''SDK tuples belong to active reads; review only complete reads.'',1;
         IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.DownloadId=d.DownloadId AND i.Sequence=d.Sequence WHERE d.RawEventId IS NOT NULL OR EXISTS(SELECT CONVERT(varbinary(max),d.PayloadJson),d.PayloadHash,d.OccurrenceOrdinal EXCEPT SELECT CONVERT(varbinary(max),i.PayloadJson),i.PayloadHash,i.OccurrenceOrdinal)) THROW 51021,''Original SDK tuples and ingested receipts are immutable.'',1;
        END');
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.DeviceAttendanceDownloads) THROW 51021,'SDK extraction history prevents destructive downgrade.',1;
        DROP TABLE dbo.DeviceAttendanceRows;
        DROP TABLE dbo.DeviceAttendanceDownloads;
        """);
}
