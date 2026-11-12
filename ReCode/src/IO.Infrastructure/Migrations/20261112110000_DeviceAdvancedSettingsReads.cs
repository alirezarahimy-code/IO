using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112110000_DeviceAdvancedSettingsReads")]
public sealed class DeviceAdvancedSettingsReads:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Request;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Result;
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Request CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 60 AND (([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read') AND [TargetUtc] IS NULL) OR ([Kind]=N'clock.set' AND [TargetUtc] IS NOT NULL AND DATEPART(TZOFFSET,[TargetUtc])=0 AND DATEPART(NANOSECOND,[TargetUtc])=0)));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Result CHECK (([ResultJson] IS NULL OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read') AND [State]=N'succeeded' AND ISJSON([ResultJson])=1 AND DATALENGTH([ResultJson])<=8000)) AND ([Kind] COLLATE Latin1_General_100_BIN2 NOT IN(N'information.read',N'settings.read') OR [State]<>N'succeeded' OR [ResultJson] IS NOT NULL));
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.DeviceOperations WHERE Kind=N'settings.read') THROW 51024,'Advanced settings snapshots prevent destructive downgrade.',1;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Request;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Result;
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Request CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 60 AND (([Kind]=N'information.read' AND [TargetUtc] IS NULL) OR ([Kind]=N'clock.set' AND [TargetUtc] IS NOT NULL AND DATEPART(TZOFFSET,[TargetUtc])=0 AND DATEPART(NANOSECOND,[TargetUtc])=0)));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Result CHECK (([ResultJson] IS NULL OR ([Kind]=N'information.read' AND [State]=N'succeeded' AND ISJSON([ResultJson])=1 AND DATALENGTH([ResultJson])<=8000)) AND ([Kind]<>N'information.read' OR [State]<>N'succeeded' OR [ResultJson] IS NOT NULL));
        """);
}
