using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112120000_DeviceServiceHealth")]
public sealed class DeviceServiceHealth:Migration
{
 protected override void Up(MigrationBuilder m)
 {
  m.Sql("""
   CREATE TABLE dbo.DeviceServiceHeartbeats(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_DeviceServiceHeartbeats PRIMARY KEY,
    ServiceUserId uniqueidentifier NOT NULL,
    StartedAtUtc datetimeoffset NOT NULL,
    LastSeenAtUtc datetimeoffset NOT NULL,
    State nvarchar(20) NOT NULL,
    Version rowversion NOT NULL,
    CONSTRAINT FK_DeviceServiceHeartbeats_Users_ServiceUserId FOREIGN KEY(ServiceUserId) REFERENCES dbo.Users(Id),
    CONSTRAINT CK_DeviceServiceHeartbeats_State CHECK([State] COLLATE Latin1_General_100_BIN2 IN(N'running',N'stopped') AND DATALENGTH([State]) IN(14) AND DATEPART(TZOFFSET,[StartedAtUtc])=0 AND DATEPART(TZOFFSET,[LastSeenAtUtc])=0 AND [StartedAtUtc]<=[LastSeenAtUtc])
   );
   CREATE INDEX IX_DeviceServiceHeartbeats_ServiceUserId_LastSeenAtUtc ON dbo.DeviceServiceHeartbeats(ServiceUserId,LastSeenAtUtc);
   """);
  m.Sql("""
   CREATE TRIGGER dbo.TR_DeviceServiceHeartbeats_History ON dbo.DeviceServiceHeartbeats AFTER UPDATE,DELETE AS
   BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL OR i.ServiceUserId<>d.ServiceUserId OR i.StartedAtUtc<>d.StartedAtUtc OR i.LastSeenAtUtc<d.LastSeenAtUtc OR (d.State=N'stopped' AND (i.State<>d.State OR i.LastSeenAtUtc<>d.LastSeenAtUtc)))
     THROW 51025,'Service instance identity and stopped heartbeat evidence are retained.',1;
   END;
   """);
 }
 protected override void Down(MigrationBuilder m)=>m.Sql("""
  IF EXISTS(SELECT 1 FROM dbo.DeviceServiceHeartbeats) THROW 51025,'Service heartbeat history prevents destructive downgrade.',1;
  DROP TABLE dbo.DeviceServiceHeartbeats;
  """);
}
