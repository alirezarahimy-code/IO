using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261113120000_DevicePollReadFormats")]
public sealed class DevicePollReadFormats:Migration
{
 protected override void Up(MigrationBuilder m)
 {
  m.AddColumn<string>(name:"ReadFormat",table:"DevicePollSchedules",type:"nvarchar(32)",maxLength:32,nullable:false,defaultValue:"ssr-v1");
  m.AddCheckConstraint(name:"CK_DevicePollSchedules_Format",table:"DevicePollSchedules",sql:"[ReadFormat] COLLATE Latin1_General_100_BIN2 IN(N'ssr-v1',N'legacy-int32-minute-v1') AND DATALENGTH([ReadFormat])=CASE WHEN [ReadFormat]=N'ssr-v1' THEN 12 ELSE 44 END");
  // Do not relabel a manually linked historical legacy extraction as SSR.
  m.Sql("IF EXISTS(SELECT 1 FROM dbo.DevicePollRuns r JOIN dbo.DevicePollSchedules s ON s.Id=r.ScheduleId JOIN dbo.DeviceAttendanceDownloads j ON j.Id=r.DownloadId WHERE CONVERT(varbinary(max),s.ReadFormat)<>CONVERT(varbinary(max),j.ReadFormat)) THROW 51032,'Existing poll run format does not match its schedule; explicit historical review required.',1;");
  m.Sql("""
CREATE TRIGGER dbo.TR_DevicePollSchedules_Format ON dbo.DevicePollSchedules AFTER UPDATE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE CONVERT(varbinary(max),i.ReadFormat)<>CONVERT(varbinary(max),d.ReadFormat)) THROW 51032,'Approved poll read format is immutable.',1;
END
""");
  m.Sql("""
CREATE TRIGGER dbo.TR_DevicePollRuns_Format ON dbo.DevicePollRuns AFTER INSERT,UPDATE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted r JOIN dbo.DevicePollSchedules s ON s.Id=r.ScheduleId JOIN dbo.DeviceAttendanceDownloads j ON j.Id=r.DownloadId WHERE CONVERT(varbinary(max),s.ReadFormat)<>CONVERT(varbinary(max),j.ReadFormat)) THROW 51032,'Poll run must use the exact approved read format.',1;
END
""");
 }
 protected override void Down(MigrationBuilder m)
 {
  m.Sql("IF EXISTS(SELECT 1 FROM dbo.DevicePollSchedules WHERE ReadFormat<>N'ssr-v1') THROW 51032,'Legacy poll approval history prevents destructive downgrade.',1;");
  m.Sql("DROP TRIGGER dbo.TR_DevicePollRuns_Format; DROP TRIGGER dbo.TR_DevicePollSchedules_Format;");
  m.DropCheckConstraint(name:"CK_DevicePollSchedules_Format",table:"DevicePollSchedules");
  m.DropColumn(name:"ReadFormat",table:"DevicePollSchedules");
 }
}
