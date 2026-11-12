using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112180000_DeviceRestart")]
public sealed class DeviceRestart:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Request;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Resolution;
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Request CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 60 AND (([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read',N'device.restart') AND [TargetUtc] IS NULL) OR ([Kind]=N'clock.set' AND [TargetUtc] IS NOT NULL AND DATEPART(TZOFFSET,[TargetUtc])=0 AND DATEPART(NANOSECOND,[TargetUtc])=0)));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Resolution CHECK (([ResolutionInfoId] IS NULL AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL) OR ([Kind] COLLATE Latin1_General_100_BIN2 IN(N'clock.set',N'device.restart') AND [State]=N'unknown' AND [ResolutionInfoId] IS NOT NULL AND [ResolutionInfoId]<>[Id] AND [ResolvedByUserId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET,[ResolvedAtUtc])=0 AND [ResolvedAtUtc]>=[FinishedAtUtc]));
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.DeviceOperations WHERE Kind=N'device.restart') THROW 51029,'Restart history prevents destructive downgrade.',1;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Request;
        ALTER TABLE dbo.DeviceOperations DROP CONSTRAINT CK_DeviceOperations_Resolution;
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Request CHECK (DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 60 AND (([Kind] COLLATE Latin1_General_100_BIN2 IN(N'information.read',N'settings.read') AND [TargetUtc] IS NULL) OR ([Kind]=N'clock.set' AND [TargetUtc] IS NOT NULL AND DATEPART(TZOFFSET,[TargetUtc])=0 AND DATEPART(NANOSECOND,[TargetUtc])=0)));
        ALTER TABLE dbo.DeviceOperations ADD CONSTRAINT CK_DeviceOperations_Resolution CHECK (([ResolutionInfoId] IS NULL AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL) OR ([Kind]=N'clock.set' AND [State]=N'unknown' AND [ResolutionInfoId] IS NOT NULL AND [ResolutionInfoId]<>[Id] AND [ResolvedByUserId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET,[ResolvedAtUtc])=0 AND [ResolvedAtUtc]>=[FinishedAtUtc]));
        """);
}
