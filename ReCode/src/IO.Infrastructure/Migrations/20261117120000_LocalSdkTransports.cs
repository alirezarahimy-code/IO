using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261117120000_LocalSdkTransports")]
public sealed class LocalSdkTransports:Migration
{
 protected override void Up(MigrationBuilder m)=>m.Sql("""
ALTER TABLE dbo.Devices DROP CONSTRAINT CK_Devices_Profile;
ALTER TABLE dbo.Devices ADD CONSTRAINT CK_Devices_Profile CHECK ((([Profile] = N'zk-standalone' AND [Host] IS NOT NULL AND [Port] IS NOT NULL AND [Port] BETWEEN 1 AND 65535) OR ([Profile] IN (N'zk-push', N'offline-file') AND [Host] IS NULL AND [Port] IS NULL)) OR ([Profile] IN (N'zk-usb-direct', N'zk-serial-direct') AND [Host] IS NOT NULL AND LEN([Host]) BETWEEN 1 AND 63 AND DATALENGTH([Host])=2*LEN([Host]) AND [Host] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^A-Z0-9-]%' AND LEFT([Host],1) COLLATE Latin1_General_100_BIN2 LIKE N'[A-Z]' AND [Port] IS NOT NULL AND (([Profile]=N'zk-usb-direct' AND [Port]=0) OR ([Profile]=N'zk-serial-direct' AND [Port] BETWEEN 1 AND 256))));
CREATE UNIQUE INDEX IX_Devices_Host_Port ON dbo.Devices(Host,Port) WHERE Profile IN (N'zk-usb-direct',N'zk-serial-direct');
""");
 protected override void Down(MigrationBuilder m)=>m.Sql("""
IF EXISTS(SELECT 1 FROM dbo.Devices WHERE Profile IN (N'zk-usb-direct',N'zk-serial-direct')) THROW 51046,'Local physical endpoint registrations prevent downgrade.',1;
DROP INDEX IX_Devices_Host_Port ON dbo.Devices;
ALTER TABLE dbo.Devices DROP CONSTRAINT CK_Devices_Profile;
ALTER TABLE dbo.Devices ADD CONSTRAINT CK_Devices_Profile CHECK (([Profile] = N'zk-standalone' AND [Host] IS NOT NULL AND [Port] IS NOT NULL AND [Port] BETWEEN 1 AND 65535) OR ([Profile] IN (N'zk-push', N'offline-file') AND [Host] IS NULL AND [Port] IS NULL));
""");
}
