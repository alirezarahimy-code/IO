using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IO.Infrastructure.Migrations;

[DbContext(typeof(IoDbContext))]
[Migration("20261112020000_DeviceConnections")]
public sealed class DeviceConnections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE dbo.DeviceConnections (
              Id uniqueidentifier NOT NULL CONSTRAINT PK_DeviceConnections PRIMARY KEY,
              DeviceId uniqueidentifier NOT NULL, RequestedByUserId uniqueidentifier NOT NULL,
              RequestedAtUtc datetimeoffset NOT NULL, ExpectedDeviceVersion varbinary(8) NOT NULL,
              TimeoutSeconds int NOT NULL, State nvarchar(32) NOT NULL,
              ServiceUserId uniqueidentifier NULL, ClaimId uniqueidentifier NULL,
              ClaimedAtUtc datetimeoffset NULL, FinishedAtUtc datetimeoffset NULL, Version rowversion NOT NULL,
              CONSTRAINT FK_DeviceConnections_Devices_DeviceId FOREIGN KEY(DeviceId) REFERENCES dbo.Devices(Id),
              CONSTRAINT FK_DeviceConnections_Users_RequestedByUserId FOREIGN KEY(RequestedByUserId) REFERENCES dbo.Users(Id),
              CONSTRAINT FK_DeviceConnections_Users_ServiceUserId FOREIGN KEY(ServiceUserId) REFERENCES dbo.Users(Id),
              CONSTRAINT CK_DeviceConnections_Timeout CHECK ([TimeoutSeconds] BETWEEN 1 AND 60 AND DATALENGTH([ExpectedDeviceVersion]) = 8),
              CONSTRAINT CK_DeviceConnections_State CHECK ([State] COLLATE Latin1_General_100_BIN2 IN (N'pending',N'running',N'cancelled',N'revoked',N'device-changed',N'tcp-reachable',N'unreachable',N'timed-out',N'unsupported',N'endpoint-denied',N'interrupted')),
              CONSTRAINT CK_DeviceConnections_Lifecycle CHECK ((([State] = N'pending' AND [ServiceUserId] IS NULL AND [ClaimId] IS NULL AND [ClaimedAtUtc] IS NULL AND [FinishedAtUtc] IS NULL) OR ([State] = N'running' AND [ServiceUserId] IS NOT NULL AND [ClaimId] IS NOT NULL AND [ClaimedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([State] IN (N'cancelled',N'revoked',N'device-changed') AND [FinishedAtUtc] IS NOT NULL AND [ServiceUserId] IS NULL AND [ClaimId] IS NULL AND [ClaimedAtUtc] IS NULL) OR ([State] NOT IN (N'pending',N'running',N'cancelled',N'revoked',N'device-changed') AND [FinishedAtUtc] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [ClaimId] IS NOT NULL AND [ClaimedAtUtc] IS NOT NULL AND [FinishedAtUtc] >= [ClaimedAtUtc])) AND DATEPART(TZOFFSET,[RequestedAtUtc]) = 0 AND DATEPART(TZOFFSET,[ClaimedAtUtc]) = 0 AND DATEPART(TZOFFSET,[FinishedAtUtc]) = 0 AND ([ClaimedAtUtc] IS NULL OR [ClaimedAtUtc] >= [RequestedAtUtc]) AND ([FinishedAtUtc] IS NULL OR [FinishedAtUtc] >= [RequestedAtUtc]))
            );
            CREATE INDEX IX_DeviceConnections_DeviceId_RequestedAtUtc ON dbo.DeviceConnections(DeviceId,RequestedAtUtc);
            CREATE UNIQUE INDEX IX_DeviceConnections_DeviceId ON dbo.DeviceConnections(DeviceId) WHERE State IN (N'pending', N'running');
            CREATE INDEX IX_DeviceConnections_RequestedByUserId ON dbo.DeviceConnections(RequestedByUserId);
            CREATE INDEX IX_DeviceConnections_ServiceUserId ON dbo.DeviceConnections(ServiceUserId);
            """);
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER dbo.TR_DeviceConnections_History ON dbo.DeviceConnections AFTER INSERT, UPDATE, DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.Id=i.Id) AND i.State<>N''pending'')
                THROW 51016, ''New connection diagnostics must be pending.'', 1;
              IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id))
                THROW 51016, ''Connection diagnostics retain history.'', 1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE
                d.State NOT IN (N''pending'',N''running'') OR (d.State=N''pending'' AND i.State NOT IN (N''running'',N''cancelled'',N''revoked'',N''device-changed'')) OR d.DeviceId<>i.DeviceId OR d.RequestedByUserId<>i.RequestedByUserId OR
                d.ExpectedDeviceVersion<>i.ExpectedDeviceVersion OR d.TimeoutSeconds<>i.TimeoutSeconds OR d.RequestedAtUtc<>i.RequestedAtUtc OR
                (d.State=N''running'' AND (i.State IN (N''pending'',N''running'') OR i.ServiceUserId<>d.ServiceUserId OR i.ClaimId<>d.ClaimId OR i.ClaimedAtUtc<>d.ClaimedAtUtc)))
                THROW 51016, ''Connection request identity and terminal history are immutable.'', 1;
            END');
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS(SELECT 1 FROM dbo.DeviceConnections)
              THROW 51016, 'Connection diagnostic history prevents destructive downgrade.', 1;
            DROP TABLE dbo.DeviceConnections;
            """);
    }
}
