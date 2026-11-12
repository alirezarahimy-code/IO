using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112090000_DevicePollSchedules")]
public sealed class DevicePollSchedules:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        CREATE TABLE dbo.DevicePollSchedules(
         [Id] uniqueidentifier NOT NULL,
         [DeviceId] uniqueidentifier NOT NULL,
         [SourceId] uniqueidentifier NOT NULL,
         [SiteId] uniqueidentifier NOT NULL,
         [CompanyId] uniqueidentifier NOT NULL,
         [TimeZoneId] nvarchar(100) NOT NULL,
         [CreatedByUserId] uniqueidentifier NOT NULL,
         [CreatedAtUtc] datetimeoffset NOT NULL,
         [ExpectedDeviceVersion] varbinary(8) NOT NULL,
         [StartUtc] datetimeoffset NOT NULL,
         [EverySeconds] int NOT NULL,
         [WindowSeconds] int NOT NULL,
         [LagSeconds] int NOT NULL,
         [TimeoutSeconds] int NOT NULL,
         [InCode] int NOT NULL,
         [OutCode] int NOT NULL,
         [AutoCommit] bit NOT NULL,
         [Enabled] bit NOT NULL,
         [CursorUtc] datetimeoffset NOT NULL,
         [NextDueUtc] datetimeoffset NOT NULL,
         [ActiveRunId] uniqueidentifier NULL,
         [Failures] int NOT NULL,
         [LastOutcome] nvarchar(32) NOT NULL,
         [Version] rowversion NOT NULL,
         CONSTRAINT PK_DevicePollSchedules PRIMARY KEY(Id),
         CONSTRAINT FK_DevicePollSchedules_Devices_DeviceId FOREIGN KEY([DeviceId]) REFERENCES dbo.Devices(Id),
         CONSTRAINT FK_DevicePollSchedules_EventSources_SourceId FOREIGN KEY([SourceId]) REFERENCES dbo.EventSources(Id),
         CONSTRAINT FK_DevicePollSchedules_Sites_SiteId FOREIGN KEY([SiteId]) REFERENCES dbo.Sites(Id),
         CONSTRAINT FK_DevicePollSchedules_Companies_CompanyId FOREIGN KEY([CompanyId]) REFERENCES dbo.Companies(Id),
         CONSTRAINT FK_DevicePollSchedules_Users_CreatedByUserId FOREIGN KEY([CreatedByUserId]) REFERENCES dbo.Users(Id),
         CONSTRAINT CK_DevicePollSchedules_Config CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND [EverySeconds] BETWEEN 15 AND 86400 AND [WindowSeconds] BETWEEN 60 AND 86400 AND [LagSeconds] BETWEEN 0 AND 86400 AND [TimeoutSeconds] BETWEEN 1 AND 600 AND [InCode] BETWEEN 0 AND 255 AND [OutCode] BETWEEN 0 AND 255 AND [InCode]<>[OutCode] AND [Failures]>=0 AND [CursorUtc]>=[StartUtc] AND ([Enabled]=1 OR [ActiveRunId] IS NULL)),
         CONSTRAINT CK_DevicePollSchedules_Utc CHECK (DATEPART(TZOFFSET,[CreatedAtUtc])=0 AND DATEPART(TZOFFSET,[StartUtc])=0 AND DATEPART(TZOFFSET,[CursorUtc])=0 AND DATEPART(TZOFFSET,[NextDueUtc])=0)
        );
        CREATE UNIQUE INDEX IX_DevicePollSchedules_DeviceId ON dbo.DevicePollSchedules(DeviceId) WHERE Enabled=1;
        CREATE INDEX IX_DevicePollSchedules_NextDueUtc_Id ON dbo.DevicePollSchedules(NextDueUtc,Id);
        CREATE INDEX IX_DevicePollSchedules_SourceId ON dbo.DevicePollSchedules([SourceId]);
        CREATE INDEX IX_DevicePollSchedules_SiteId ON dbo.DevicePollSchedules([SiteId]);
        CREATE INDEX IX_DevicePollSchedules_CompanyId ON dbo.DevicePollSchedules([CompanyId]);
        CREATE INDEX IX_DevicePollSchedules_CreatedByUserId ON dbo.DevicePollSchedules([CreatedByUserId]);
        CREATE INDEX IX_DevicePollSchedules_ActiveRunId ON dbo.DevicePollSchedules([ActiveRunId]);
        CREATE TABLE dbo.DevicePollRuns(
         [Id] uniqueidentifier NOT NULL,
         [ScheduleId] uniqueidentifier NOT NULL,
         [DownloadId] uniqueidentifier NOT NULL,
         [FromUtc] datetimeoffset NOT NULL,
         [UntilUtcExclusive] datetimeoffset NOT NULL,
         [CreatedAtUtc] datetimeoffset NOT NULL,
         [NextAfter] bigint NOT NULL,
         [Outcome] nvarchar(32) NOT NULL,
         [FinishedAtUtc] datetimeoffset NULL,
         CONSTRAINT PK_DevicePollRuns PRIMARY KEY(Id),
         CONSTRAINT FK_DevicePollRuns_DevicePollSchedules_ScheduleId FOREIGN KEY([ScheduleId]) REFERENCES dbo.DevicePollSchedules(Id),
         CONSTRAINT FK_DevicePollRuns_DeviceAttendanceDownloads_DownloadId FOREIGN KEY([DownloadId]) REFERENCES dbo.DeviceAttendanceDownloads(Id),
         CONSTRAINT CK_DevicePollRuns_Data CHECK ([FromUtc]<[UntilUtcExclusive] AND [NextAfter]>=0 AND DATEPART(TZOFFSET,[CreatedAtUtc])=0 AND DATEPART(TZOFFSET,[FromUtc])=0 AND DATEPART(TZOFFSET,[UntilUtcExclusive])=0 AND DATEPART(TZOFFSET,[FinishedAtUtc])=0 AND (([Outcome] IN(N'queued',N'collecting',N'reviewing') AND [FinishedAtUtc] IS NULL) OR ([Outcome] NOT IN(N'queued',N'collecting',N'reviewing') AND [FinishedAtUtc] IS NOT NULL AND [FinishedAtUtc]>=[CreatedAtUtc])))
        );
        CREATE UNIQUE INDEX IX_DevicePollRuns_DownloadId ON dbo.DevicePollRuns(DownloadId);
        CREATE INDEX IX_DevicePollRuns_ScheduleId_CreatedAtUtc_Id ON dbo.DevicePollRuns(ScheduleId,CreatedAtUtc,Id);
        ALTER TABLE dbo.DevicePollSchedules ADD CONSTRAINT FK_DevicePollSchedules_DevicePollRuns_ActiveRunId FOREIGN KEY(ActiveRunId) REFERENCES dbo.DevicePollRuns(Id);
        
        EXEC(N'CREATE TRIGGER dbo.TR_DevicePollSchedules_History ON dbo.DevicePollSchedules AFTER INSERT,UPDATE,DELETE AS
        BEGIN SET NOCOUNT ON;
         IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51022,''Poll schedule history retained.'',1;
         IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(SELECT d.Id,d.DeviceId,d.SourceId,d.SiteId,d.CompanyId,CONVERT(varbinary(max),d.TimeZoneId),d.CreatedByUserId,d.CreatedAtUtc,d.ExpectedDeviceVersion,d.StartUtc,d.EverySeconds,d.WindowSeconds,d.LagSeconds,d.TimeoutSeconds,d.InCode,d.OutCode,d.AutoCommit EXCEPT SELECT i.Id,i.DeviceId,i.SourceId,i.SiteId,i.CompanyId,CONVERT(varbinary(max),i.TimeZoneId),i.CreatedByUserId,i.CreatedAtUtc,i.ExpectedDeviceVersion,i.StartUtc,i.EverySeconds,i.WindowSeconds,i.LagSeconds,i.TimeoutSeconds,i.InCode,i.OutCode,i.AutoCommit) OR i.CursorUtc<d.CursorUtc) THROW 51022,''Schedule config and acknowledged cursor are immutable or monotonic.'',1;
         IF EXISTS(SELECT 1 FROM inserted i WHERE i.ActiveRunId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.DevicePollRuns r WHERE r.Id=i.ActiveRunId AND r.ScheduleId=i.Id AND r.FromUtc=i.CursorUtc AND r.FinishedAtUtc IS NULL)) THROW 51022,''Active run must belong to this cursor and schedule.'',1;
         IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE i.CursorUtc>d.CursorUtc AND NOT EXISTS(SELECT 1 FROM dbo.DevicePollRuns r JOIN dbo.DeviceAttendanceDownloads j ON j.Id=r.DownloadId WHERE r.Id=d.ActiveRunId AND r.ScheduleId=d.Id AND r.Outcome=N''succeeded'' AND r.FinishedAtUtc IS NOT NULL AND r.FromUtc=d.CursorUtc AND r.UntilUtcExclusive=i.CursorUtc AND j.State=N''succeeded'' AND (d.AutoCommit=0 OR r.NextAfter>=j.ReceivedCount))) THROW 51022,''Cursor advances only over a successfully acquired and reviewed run.'',1;
        END');
        EXEC(N'CREATE TRIGGER dbo.TR_DevicePollRuns_History ON dbo.DevicePollRuns AFTER INSERT,UPDATE,DELETE AS
        BEGIN SET NOCOUNT ON;
         IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51022,''Poll runs retained.'',1;
         IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceAttendanceDownloads j ON j.Id=i.DownloadId JOIN dbo.DevicePollSchedules s ON s.Id=i.ScheduleId WHERE j.DeviceId<>s.DeviceId OR j.RequestedByUserId<>s.CreatedByUserId OR j.ExpectedDeviceVersion<>s.ExpectedDeviceVersion OR j.FromUtc<>i.FromUtc OR j.UntilUtcExclusive<>i.UntilUtcExclusive OR i.NextAfter>j.ReceivedCount OR (i.Outcome=N''succeeded'' AND (j.State<>N''succeeded'' OR (s.AutoCommit=1 AND i.NextAfter<j.ReceivedCount)))) THROW 51022,''Run evidence must match its real extraction.'',1;
         IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(SELECT d.Id,d.ScheduleId,d.DownloadId,d.FromUtc,d.UntilUtcExclusive,d.CreatedAtUtc EXCEPT SELECT i.Id,i.ScheduleId,i.DownloadId,i.FromUtc,i.UntilUtcExclusive,i.CreatedAtUtc) OR d.FinishedAtUtc IS NOT NULL OR i.NextAfter<d.NextAfter) THROW 51022,''Run identity and terminal evidence immutable.'',1;
        END');
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.DevicePollSchedules) THROW 51022,'Poll history prevents destructive downgrade.',1;
        ALTER TABLE dbo.DevicePollSchedules DROP CONSTRAINT FK_DevicePollSchedules_DevicePollRuns_ActiveRunId;
        DROP TABLE dbo.DevicePollRuns;
        DROP TABLE dbo.DevicePollSchedules;
        """);
}
