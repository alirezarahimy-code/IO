using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112130000_DeviceSettingsReadRetries")]
public sealed class DeviceSettingsReadRetries:Migration
{
 protected override void Up(MigrationBuilder m)=>m.Sql("""
ALTER TRIGGER dbo.TR_DeviceCommandRetries_History ON dbo.DeviceCommandRetries AFTER INSERT,UPDATE,DELETE AS
        BEGIN SET NOCOUNT ON;
         IF EXISTS(SELECT 1 FROM deleted) THROW 51023,'Command retry lineage is immutable.',1;
         IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceOperations p ON p.Id=i.PreviousOperationId JOIN dbo.DeviceOperations n ON n.Id=i.NextOperationId WHERE p.Kind COLLATE Latin1_General_100_BIN2 NOT IN(N'information.read',N'settings.read') OR n.Kind COLLATE Latin1_General_100_BIN2<>p.Kind COLLATE Latin1_General_100_BIN2 OR p.State NOT IN(N'failed',N'interrupted',N'timed-out',N'bridge-failed',N'unknown') OR p.FinishedAtUtc IS NULL OR n.State<>N'pending' OR p.DeviceId<>n.DeviceId OR p.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR n.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR p.Version<>i.ExpectedPreviousVersion OR n.RequestedByUserId<>i.RequestedByUserId OR n.TimeoutSeconds<>p.TimeoutSeconds OR n.TimeZoneId<>p.TimeZoneId OR n.TargetUtc IS NOT NULL OR n.RequestedAtUtc<>i.RequestedAtUtc) THROW 51023,'Only a new exact read intent may retry terminal typed information or settings reads.',1;
         IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceAttendanceDownloads p ON p.Id=i.PreviousAttendanceDownloadId JOIN dbo.DeviceAttendanceDownloads n ON n.Id=i.NextAttendanceDownloadId WHERE p.State NOT IN(N'failed',N'interrupted',N'timed-out',N'bridge-failed',N'incomplete') OR p.FinishedAtUtc IS NULL OR n.State<>N'pending' OR p.DeviceId<>n.DeviceId OR p.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR n.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR p.Version<>i.ExpectedPreviousVersion OR n.RequestedByUserId<>i.RequestedByUserId OR n.TimeoutSeconds<>p.TimeoutSeconds OR n.SourceId<>p.SourceId OR n.SiteId<>p.SiteId OR n.CompanyId<>p.CompanyId OR n.TimeZoneId<>p.TimeZoneId OR n.FromUtc<>p.FromUtc OR n.UntilUtcExclusive<>p.UntilUtcExclusive OR n.InCode<>p.InCode OR n.OutCode<>p.OutCode OR n.RequestedAtUtc<>i.RequestedAtUtc) THROW 51023,'Only a new exact read intent may retry terminal attendance reads.',1;
        END
""");
 protected override void Down(MigrationBuilder m)
 {
  m.Sql("IF EXISTS(SELECT 1 FROM dbo.DeviceCommandRetries r JOIN dbo.DeviceOperations p ON p.Id=r.PreviousOperationId WHERE p.Kind=N'settings.read') THROW 51026,'Settings read retry lineage prevents destructive downgrade.',1;");
  m.Sql("""
ALTER TRIGGER dbo.TR_DeviceCommandRetries_History ON dbo.DeviceCommandRetries AFTER INSERT,UPDATE,DELETE AS
        BEGIN SET NOCOUNT ON;
         IF EXISTS(SELECT 1 FROM deleted) THROW 51023,'Command retry lineage is immutable.',1;
         IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceOperations p ON p.Id=i.PreviousOperationId JOIN dbo.DeviceOperations n ON n.Id=i.NextOperationId WHERE p.Kind<>N'information.read' OR n.Kind<>p.Kind OR p.State NOT IN(N'failed',N'interrupted',N'timed-out',N'bridge-failed',N'unknown') OR p.FinishedAtUtc IS NULL OR n.State<>N'pending' OR p.DeviceId<>n.DeviceId OR p.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR n.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR p.Version<>i.ExpectedPreviousVersion OR n.RequestedByUserId<>i.RequestedByUserId OR n.TimeoutSeconds<>p.TimeoutSeconds OR n.TimeZoneId<>p.TimeZoneId OR n.TargetUtc IS NOT NULL OR n.RequestedAtUtc<>i.RequestedAtUtc) THROW 51023,'Only a new exact read intent may retry terminal information reads.',1;
         IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceAttendanceDownloads p ON p.Id=i.PreviousAttendanceDownloadId JOIN dbo.DeviceAttendanceDownloads n ON n.Id=i.NextAttendanceDownloadId WHERE p.State NOT IN(N'failed',N'interrupted',N'timed-out',N'bridge-failed',N'incomplete') OR p.FinishedAtUtc IS NULL OR n.State<>N'pending' OR p.DeviceId<>n.DeviceId OR p.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR n.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR p.Version<>i.ExpectedPreviousVersion OR n.RequestedByUserId<>i.RequestedByUserId OR n.TimeoutSeconds<>p.TimeoutSeconds OR n.SourceId<>p.SourceId OR n.SiteId<>p.SiteId OR n.CompanyId<>p.CompanyId OR n.TimeZoneId<>p.TimeZoneId OR n.FromUtc<>p.FromUtc OR n.UntilUtcExclusive<>p.UntilUtcExclusive OR n.InCode<>p.InCode OR n.OutCode<>p.OutCode OR n.RequestedAtUtc<>i.RequestedAtUtc) THROW 51023,'Only a new exact read intent may retry terminal attendance reads.',1;
        END
""");
 }
}
