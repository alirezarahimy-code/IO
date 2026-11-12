using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112100000_DeviceCommandHistory")]
public sealed class DeviceCommandHistory:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        CREATE TABLE dbo.DeviceCommandRetries(
         [Id] uniqueidentifier NOT NULL,
         [PreviousOperationId] uniqueidentifier NULL,
         [NextOperationId] uniqueidentifier NULL,
         [PreviousAttendanceDownloadId] uniqueidentifier NULL,
         [NextAttendanceDownloadId] uniqueidentifier NULL,
         [RequestedByUserId] uniqueidentifier NOT NULL,
         [RequestedAtUtc] datetimeoffset NOT NULL,
         [ExpectedPreviousVersion] varbinary(8) NOT NULL,
         [ExpectedDeviceVersion] varbinary(8) NOT NULL,
         CONSTRAINT PK_DeviceCommandRetries PRIMARY KEY(Id),
         CONSTRAINT FK_DeviceCommandRetries_DeviceOperations_PreviousOperationId FOREIGN KEY([PreviousOperationId]) REFERENCES dbo.DeviceOperations(Id),
         CONSTRAINT FK_DeviceCommandRetries_DeviceOperations_NextOperationId FOREIGN KEY([NextOperationId]) REFERENCES dbo.DeviceOperations(Id),
         CONSTRAINT FK_DeviceCommandRetries_DeviceAttendanceDownloads_PreviousAttendanceDownloadId FOREIGN KEY([PreviousAttendanceDownloadId]) REFERENCES dbo.DeviceAttendanceDownloads(Id),
         CONSTRAINT FK_DeviceCommandRetries_DeviceAttendanceDownloads_NextAttendanceDownloadId FOREIGN KEY([NextAttendanceDownloadId]) REFERENCES dbo.DeviceAttendanceDownloads(Id),
         CONSTRAINT FK_DeviceCommandRetries_Users_RequestedByUserId FOREIGN KEY([RequestedByUserId]) REFERENCES dbo.Users(Id),
         CONSTRAINT CK_DeviceCommandRetries_Data CHECK (DATALENGTH([ExpectedPreviousVersion])=8 AND DATALENGTH([ExpectedDeviceVersion])=8 AND DATEPART(TZOFFSET,[RequestedAtUtc])=0 AND (([PreviousOperationId] IS NOT NULL AND [NextOperationId] IS NOT NULL AND [NextOperationId]=[Id] AND [PreviousOperationId]<>[NextOperationId] AND [PreviousAttendanceDownloadId] IS NULL AND [NextAttendanceDownloadId] IS NULL) OR ([PreviousAttendanceDownloadId] IS NOT NULL AND [NextAttendanceDownloadId] IS NOT NULL AND [NextAttendanceDownloadId]=[Id] AND [PreviousAttendanceDownloadId]<>[NextAttendanceDownloadId] AND [PreviousOperationId] IS NULL AND [NextOperationId] IS NULL)))
        );
        CREATE UNIQUE INDEX IX_DeviceCommandRetries_PreviousOperationId ON dbo.DeviceCommandRetries([PreviousOperationId]) WHERE [PreviousOperationId] IS NOT NULL;
        CREATE UNIQUE INDEX IX_DeviceCommandRetries_NextOperationId ON dbo.DeviceCommandRetries([NextOperationId]) WHERE [NextOperationId] IS NOT NULL;
        CREATE UNIQUE INDEX IX_DeviceCommandRetries_PreviousAttendanceDownloadId ON dbo.DeviceCommandRetries([PreviousAttendanceDownloadId]) WHERE [PreviousAttendanceDownloadId] IS NOT NULL;
        CREATE UNIQUE INDEX IX_DeviceCommandRetries_NextAttendanceDownloadId ON dbo.DeviceCommandRetries([NextAttendanceDownloadId]) WHERE [NextAttendanceDownloadId] IS NOT NULL;
        CREATE INDEX IX_DeviceCommandRetries_RequestedByUserId ON dbo.DeviceCommandRetries([RequestedByUserId]);
        EXEC(N'CREATE TRIGGER dbo.TR_DeviceCommandRetries_History ON dbo.DeviceCommandRetries AFTER INSERT,UPDATE,DELETE AS
        BEGIN SET NOCOUNT ON;
         IF EXISTS(SELECT 1 FROM deleted) THROW 51023,''Command retry lineage is immutable.'',1;
         IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceOperations p ON p.Id=i.PreviousOperationId JOIN dbo.DeviceOperations n ON n.Id=i.NextOperationId WHERE p.Kind<>N''information.read'' OR n.Kind<>p.Kind OR p.State NOT IN(N''failed'',N''interrupted'',N''timed-out'',N''bridge-failed'',N''unknown'') OR p.FinishedAtUtc IS NULL OR n.State<>N''pending'' OR p.DeviceId<>n.DeviceId OR p.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR n.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR p.Version<>i.ExpectedPreviousVersion OR n.RequestedByUserId<>i.RequestedByUserId OR n.TimeoutSeconds<>p.TimeoutSeconds OR n.TimeZoneId<>p.TimeZoneId OR n.TargetUtc IS NOT NULL OR n.RequestedAtUtc<>i.RequestedAtUtc) THROW 51023,''Only a new exact read intent may retry terminal information reads.'',1;
         IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceAttendanceDownloads p ON p.Id=i.PreviousAttendanceDownloadId JOIN dbo.DeviceAttendanceDownloads n ON n.Id=i.NextAttendanceDownloadId WHERE p.State NOT IN(N''failed'',N''interrupted'',N''timed-out'',N''bridge-failed'',N''incomplete'') OR p.FinishedAtUtc IS NULL OR n.State<>N''pending'' OR p.DeviceId<>n.DeviceId OR p.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR n.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR p.Version<>i.ExpectedPreviousVersion OR n.RequestedByUserId<>i.RequestedByUserId OR n.TimeoutSeconds<>p.TimeoutSeconds OR n.SourceId<>p.SourceId OR n.SiteId<>p.SiteId OR n.CompanyId<>p.CompanyId OR n.TimeZoneId<>p.TimeZoneId OR n.FromUtc<>p.FromUtc OR n.UntilUtcExclusive<>p.UntilUtcExclusive OR n.InCode<>p.InCode OR n.OutCode<>p.OutCode OR n.RequestedAtUtc<>i.RequestedAtUtc) THROW 51023,''Only a new exact read intent may retry terminal attendance reads.'',1;
        END');
        EXEC(N'CREATE VIEW dbo.DeviceCommandHistory AS
        SELECT CAST(N''connection'' AS nvarchar(32)) AS Queue,Id,DeviceId,CAST(N''connection.test'' AS nvarchar(32)) AS Kind,State,RequestedByUserId,RequestedAtUtc,FinishedAtUtc,ExpectedDeviceVersion,CONVERT(varbinary(8),Version) AS Version FROM dbo.DeviceConnections
        UNION ALL
        SELECT CAST(N''operation'' AS nvarchar(32)) AS Queue,Id,DeviceId,CAST(Kind AS nvarchar(32)) AS Kind,State,RequestedByUserId,RequestedAtUtc,FinishedAtUtc,ExpectedDeviceVersion,CONVERT(varbinary(8),Version) AS Version FROM dbo.DeviceOperations
        UNION ALL
        SELECT CAST(N''users-download'' AS nvarchar(32)) AS Queue,Id,DeviceId,CAST(N''users.read'' AS nvarchar(32)) AS Kind,State,RequestedByUserId,RequestedAtUtc,FinishedAtUtc,ExpectedDeviceVersion,CONVERT(varbinary(8),Version) AS Version FROM dbo.DeviceUserDownloads
        UNION ALL
        SELECT CAST(N''users-upload'' AS nvarchar(32)) AS Queue,Id,DeviceId,CAST(N''users.write'' AS nvarchar(32)) AS Kind,State,RequestedByUserId,RequestedAtUtc,FinishedAtUtc,ExpectedDeviceVersion,CONVERT(varbinary(8),Version) AS Version FROM dbo.DeviceUserUploads
        UNION ALL
        SELECT CAST(N''attendance-download'' AS nvarchar(32)) AS Queue,Id,DeviceId,CAST(N''attendance.read'' AS nvarchar(32)) AS Kind,State,RequestedByUserId,RequestedAtUtc,FinishedAtUtc,ExpectedDeviceVersion,CONVERT(varbinary(8),Version) AS Version FROM dbo.DeviceAttendanceDownloads
        ');
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.DeviceCommandRetries) THROW 51023,'Command retry lineage prevents destructive downgrade.',1;
        DROP VIEW dbo.DeviceCommandHistory;
        DROP TABLE dbo.DeviceCommandRetries;
        """);
}
