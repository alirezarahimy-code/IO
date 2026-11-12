using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112140000_DevicePollOverlap")]
public sealed class DevicePollOverlap:Migration
{
 protected override void Up(MigrationBuilder m)
 {
  m.AddColumn<int>(name:"OverlapSeconds",table:"DevicePollSchedules",type:"int",nullable:false,defaultValue:0);
  m.AddCheckConstraint(name:"CK_DevicePollSchedules_Overlap",table:"DevicePollSchedules",sql:"[OverlapSeconds]>=0 AND [OverlapSeconds]<=[WindowSeconds]");
  m.Sql("""
ALTER TRIGGER dbo.TR_DevicePollSchedules_History ON dbo.DevicePollSchedules AFTER INSERT,UPDATE,DELETE AS
        BEGIN SET NOCOUNT ON;
         IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51022,'Poll schedule history retained.',1;
         IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(SELECT d.Id,d.DeviceId,d.SourceId,d.SiteId,d.CompanyId,CONVERT(varbinary(max),d.TimeZoneId),d.CreatedByUserId,d.CreatedAtUtc,d.ExpectedDeviceVersion,d.StartUtc,d.EverySeconds,d.WindowSeconds,d.LagSeconds,d.TimeoutSeconds,d.InCode,d.OutCode,d.AutoCommit,d.OverlapSeconds EXCEPT SELECT i.Id,i.DeviceId,i.SourceId,i.SiteId,i.CompanyId,CONVERT(varbinary(max),i.TimeZoneId),i.CreatedByUserId,i.CreatedAtUtc,i.ExpectedDeviceVersion,i.StartUtc,i.EverySeconds,i.WindowSeconds,i.LagSeconds,i.TimeoutSeconds,i.InCode,i.OutCode,i.AutoCommit,i.OverlapSeconds) OR i.CursorUtc<d.CursorUtc) THROW 51022,'Schedule config and acknowledged cursor are immutable or monotonic.',1;
         IF EXISTS(SELECT 1 FROM inserted i WHERE i.ActiveRunId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.DevicePollRuns r WHERE r.Id=i.ActiveRunId AND r.ScheduleId=i.Id AND r.FromUtc=(CASE WHEN DATEDIFF_BIG(second,i.StartUtc,i.CursorUtc)<i.OverlapSeconds THEN i.StartUtc ELSE DATEADD(second,-i.OverlapSeconds,i.CursorUtc) END) AND r.UntilUtcExclusive>i.CursorUtc AND DATEDIFF_BIG(second,i.CursorUtc,r.UntilUtcExclusive)<=i.WindowSeconds AND r.FinishedAtUtc IS NULL)) THROW 51022,'Active run must belong to this cursor and schedule.',1;
         IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE i.CursorUtc>d.CursorUtc AND NOT EXISTS(SELECT 1 FROM dbo.DevicePollRuns r JOIN dbo.DeviceAttendanceDownloads j ON j.Id=r.DownloadId WHERE r.Id=d.ActiveRunId AND r.ScheduleId=d.Id AND r.Outcome=N'succeeded' AND r.FinishedAtUtc IS NOT NULL AND r.FromUtc=(CASE WHEN DATEDIFF_BIG(second,d.StartUtc,d.CursorUtc)<d.OverlapSeconds THEN d.StartUtc ELSE DATEADD(second,-d.OverlapSeconds,d.CursorUtc) END) AND DATEDIFF_BIG(second,d.CursorUtc,r.UntilUtcExclusive)<=d.WindowSeconds AND r.UntilUtcExclusive=i.CursorUtc AND j.State=N'succeeded' AND (d.AutoCommit=0 OR r.NextAfter>=j.ReceivedCount))) THROW 51022,'Cursor advances only over a successfully acquired and reviewed run.',1;
        END
""");
 }
 protected override void Down(MigrationBuilder m)
 {
  m.Sql("IF EXISTS(SELECT 1 FROM dbo.DevicePollSchedules WHERE OverlapSeconds<>0) THROW 51027,'Approved overlap history prevents destructive downgrade.',1;");
  m.Sql("""
ALTER TRIGGER dbo.TR_DevicePollSchedules_History ON dbo.DevicePollSchedules AFTER INSERT,UPDATE,DELETE AS
        BEGIN SET NOCOUNT ON;
         IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51022,'Poll schedule history retained.',1;
         IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(SELECT d.Id,d.DeviceId,d.SourceId,d.SiteId,d.CompanyId,CONVERT(varbinary(max),d.TimeZoneId),d.CreatedByUserId,d.CreatedAtUtc,d.ExpectedDeviceVersion,d.StartUtc,d.EverySeconds,d.WindowSeconds,d.LagSeconds,d.TimeoutSeconds,d.InCode,d.OutCode,d.AutoCommit EXCEPT SELECT i.Id,i.DeviceId,i.SourceId,i.SiteId,i.CompanyId,CONVERT(varbinary(max),i.TimeZoneId),i.CreatedByUserId,i.CreatedAtUtc,i.ExpectedDeviceVersion,i.StartUtc,i.EverySeconds,i.WindowSeconds,i.LagSeconds,i.TimeoutSeconds,i.InCode,i.OutCode,i.AutoCommit) OR i.CursorUtc<d.CursorUtc) THROW 51022,'Schedule config and acknowledged cursor are immutable or monotonic.',1;
         IF EXISTS(SELECT 1 FROM inserted i WHERE i.ActiveRunId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.DevicePollRuns r WHERE r.Id=i.ActiveRunId AND r.ScheduleId=i.Id AND r.FromUtc=i.CursorUtc AND r.FinishedAtUtc IS NULL)) THROW 51022,'Active run must belong to this cursor and schedule.',1;
         IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE i.CursorUtc>d.CursorUtc AND NOT EXISTS(SELECT 1 FROM dbo.DevicePollRuns r JOIN dbo.DeviceAttendanceDownloads j ON j.Id=r.DownloadId WHERE r.Id=d.ActiveRunId AND r.ScheduleId=d.Id AND r.Outcome=N'succeeded' AND r.FinishedAtUtc IS NOT NULL AND r.FromUtc=d.CursorUtc AND r.UntilUtcExclusive=i.CursorUtc AND j.State=N'succeeded' AND (d.AutoCommit=0 OR r.NextAfter>=j.ReceivedCount))) THROW 51022,'Cursor advances only over a successfully acquired and reviewed run.',1;
        END
""");
  m.DropCheckConstraint(name:"CK_DevicePollSchedules_Overlap",table:"DevicePollSchedules");
  m.DropColumn(name:"OverlapSeconds",table:"DevicePollSchedules");
 }
}
