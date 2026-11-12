using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IO.Infrastructure.Migrations;

[DbContext(typeof(IoDbContext))]
[Migration("20261112030000_ZkConnectionProbe")]
public sealed class ZkConnectionProbe : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE dbo.DeviceConnections DROP CONSTRAINT CK_DeviceConnections_State;
        ALTER TABLE dbo.DeviceConnections ADD CONSTRAINT CK_DeviceConnections_State CHECK (
            [State] COLLATE Latin1_General_100_BIN2 IN (N'pending',N'running',N'cancelled',N'revoked',N'device-changed',N'tcp-reachable',N'unreachable',N'timed-out',N'unsupported',N'endpoint-denied',N'interrupted',N'sdk-serial-matched',N'sdk-identity-mismatch',N'sdk-unavailable',N'credential-unavailable',N'bridge-failed'));
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.DeviceConnections WHERE State IN (N'sdk-serial-matched',N'sdk-identity-mismatch',N'sdk-unavailable',N'credential-unavailable',N'bridge-failed'))
            THROW 51016, 'Native probe history prevents downgrade.', 1;
        ALTER TABLE dbo.DeviceConnections DROP CONSTRAINT CK_DeviceConnections_State;
        ALTER TABLE dbo.DeviceConnections ADD CONSTRAINT CK_DeviceConnections_State CHECK (
            [State] COLLATE Latin1_General_100_BIN2 IN (N'pending',N'running',N'cancelled',N'revoked',N'device-changed',N'tcp-reachable',N'unreachable',N'timed-out',N'unsupported',N'endpoint-denied',N'interrupted'));
        """);
}
