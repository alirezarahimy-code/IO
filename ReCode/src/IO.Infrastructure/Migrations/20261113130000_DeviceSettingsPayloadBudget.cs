using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;

[DbContext(typeof(IoDbContext))]
[Migration("20261113130000_DeviceSettingsPayloadBudget")]
public sealed class DeviceSettingsPayloadBudget:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Result;
        ALTER TABLE dbo.DeviceOperations ALTER COLUMN ResultJson nvarchar(max) NULL;
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Result CHECK (([ResultJson] IS NULL OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read',N'network.inspect',N'settings.idle.set') AND [State]=N'succeeded' AND ISJSON([ResultJson])=1 AND DATALENGTH([ResultJson])<=CASE WHEN [Kind] COLLATE Latin1_General_100_BIN2=N'settings.read' AND DATALENGTH([Kind])=26 THEN 65536 ELSE 8000 END)) AND ([Kind] COLLATE Latin1_General_100_BIN2 NOT IN(N'information.read',N'settings.read',N'network.inspect',N'settings.idle.set') OR [State]<>N'succeeded' OR [ResultJson] IS NOT NULL));
        """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Never truncate, delete or rewrite terminal observations to make a DOWN pass.
        migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.DeviceOperations WHERE DATALENGTH(ResultJson)>8000)
            THROW 51033,'Retained settings observations exceed the earlier result budget; downgrade would lose history.',1;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Result;
        ALTER TABLE dbo.DeviceOperations ALTER COLUMN ResultJson nvarchar(4000) NULL;
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Result CHECK (([ResultJson] IS NULL OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read',N'network.inspect',N'settings.idle.set') AND [State]=N'succeeded' AND ISJSON([ResultJson])=1 AND DATALENGTH([ResultJson])<=8000)) AND ([Kind] COLLATE Latin1_General_100_BIN2 NOT IN(N'information.read',N'settings.read',N'network.inspect',N'settings.idle.set') OR [State]<>N'succeeded' OR [ResultJson] IS NOT NULL));
        """);
    }
}
