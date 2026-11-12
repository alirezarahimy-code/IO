using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112150000_DeviceLegacyUserReads")]
public sealed class DeviceLegacyUserReads:Migration
{
 protected override void Up(MigrationBuilder m)
 {
  m.AddColumn<string>(name:"ReadFormat",table:"DeviceUserDownloads",type:"nvarchar(32)",maxLength:32,nullable:false,defaultValue:"ssr-v1");
  m.AddCheckConstraint(name:"CK_DeviceUserDownloads_Format",table:"DeviceUserDownloads",sql:"[ReadFormat] COLLATE Latin1_General_100_BIN2 IN(N'ssr-v1',N'legacy-int32-v1') AND DATALENGTH([ReadFormat])=CASE WHEN [ReadFormat]=N'ssr-v1' THEN 12 ELSE 30 END");
  m.Sql("""
ALTER TRIGGER dbo.TR_DeviceUserDownloads_History ON dbo.DeviceUserDownloads AFTER INSERT,UPDATE,DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51018,'Download history is retained.',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.Id=i.Id) AND i.State<>N'pending') THROW 51018,'Downloads start pending.',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(SELECT d.DeviceId,d.SourceId,d.CompanyId,d.RequestedByUserId,d.RequestedAtUtc,d.ExpectedDeviceVersion,d.TimeoutSeconds,CONVERT(varbinary(max),d.ReadFormat) EXCEPT SELECT i.DeviceId,i.SourceId,i.CompanyId,i.RequestedByUserId,i.RequestedAtUtc,i.ExpectedDeviceVersion,i.TimeoutSeconds,CONVERT(varbinary(max),i.ReadFormat)) OR
                 d.State NOT IN(N'pending',N'running') OR
                 (d.State=N'pending' AND i.State NOT IN(N'running',N'cancelled',N'revoked',N'device-changed')) OR
                 (d.State=N'running' AND (i.State=N'pending' OR i.ReceivedCount<d.ReceivedCount OR EXISTS(SELECT d.ServiceUserId,d.ClaimId EXCEPT SELECT i.ServiceUserId,i.ClaimId))))
                 THROW 51018,'Request identity and completed snapshots are immutable.',1;
            END
""");
 }
 protected override void Down(MigrationBuilder m)
 {
  m.Sql("IF EXISTS(SELECT 1 FROM dbo.DeviceUserDownloads WHERE ReadFormat<>N'ssr-v1') THROW 51028,'Legacy user identity history prevents destructive downgrade.',1;");
  m.Sql("""
ALTER TRIGGER dbo.TR_DeviceUserDownloads_History ON dbo.DeviceUserDownloads AFTER INSERT,UPDATE,DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51018,'Download history is retained.',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.Id=i.Id) AND i.State<>N'pending') THROW 51018,'Downloads start pending.',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(SELECT d.DeviceId,d.SourceId,d.CompanyId,d.RequestedByUserId,d.RequestedAtUtc,d.ExpectedDeviceVersion,d.TimeoutSeconds EXCEPT SELECT i.DeviceId,i.SourceId,i.CompanyId,i.RequestedByUserId,i.RequestedAtUtc,i.ExpectedDeviceVersion,i.TimeoutSeconds) OR
                 d.State NOT IN(N'pending',N'running') OR
                 (d.State=N'pending' AND i.State NOT IN(N'running',N'cancelled',N'revoked',N'device-changed')) OR
                 (d.State=N'running' AND (i.State=N'pending' OR i.ReceivedCount<d.ReceivedCount OR EXISTS(SELECT d.ServiceUserId,d.ClaimId EXCEPT SELECT i.ServiceUserId,i.ClaimId))))
                 THROW 51018,'Request identity and completed snapshots are immutable.',1;
            END
""");
  m.DropCheckConstraint(name:"CK_DeviceUserDownloads_Format",table:"DeviceUserDownloads");
  m.DropColumn(name:"ReadFormat",table:"DeviceUserDownloads");
 }
}
