using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261113110000_DeviceLegacyAttendanceReads")]
public sealed class DeviceLegacyAttendanceReads:Migration
{
 protected override void Up(MigrationBuilder m)
 {
  m.AddColumn<string>(name:"ReadFormat",table:"DeviceAttendanceDownloads",type:"nvarchar(32)",maxLength:32,nullable:false,defaultValue:"ssr-v1");
  m.AddCheckConstraint(name:"CK_DeviceAttendanceDownloads_Format",table:"DeviceAttendanceDownloads",sql:"[ReadFormat] COLLATE Latin1_General_100_BIN2 IN(N'ssr-v1',N'legacy-int32-minute-v1') AND DATALENGTH([ReadFormat])=CASE WHEN [ReadFormat]=N'ssr-v1' THEN 12 ELSE 44 END");
  m.Sql("""
CREATE TRIGGER dbo.TR_DeviceAttendanceDownloads_Format ON dbo.DeviceAttendanceDownloads AFTER UPDATE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE CONVERT(varbinary(max),i.ReadFormat)<>CONVERT(varbinary(max),d.ReadFormat)) THROW 51029,'Attendance read format is immutable.',1;
END
""");
 }
 protected override void Down(MigrationBuilder m)
 {
  m.Sql("IF EXISTS(SELECT 1 FROM dbo.DeviceAttendanceDownloads WHERE ReadFormat<>N'ssr-v1') THROW 51029,'Legacy attendance provenance prevents destructive downgrade.',1;");
  m.Sql("DROP TRIGGER dbo.TR_DeviceAttendanceDownloads_Format;");
  m.DropCheckConstraint(name:"CK_DeviceAttendanceDownloads_Format",table:"DeviceAttendanceDownloads");
  m.DropColumn(name:"ReadFormat",table:"DeviceAttendanceDownloads");
 }
}
