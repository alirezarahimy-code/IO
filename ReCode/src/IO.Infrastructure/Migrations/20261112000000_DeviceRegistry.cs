using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IO.Infrastructure.Migrations;

[DbContext(typeof(IoDbContext))]
[Migration("20261112000000_DeviceRegistry")]
public sealed class DeviceRegistry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE dbo.Devices (
              Id uniqueidentifier NOT NULL CONSTRAINT PK_Devices PRIMARY KEY,
              SiteId uniqueidentifier NOT NULL, SourceId uniqueidentifier NOT NULL,
              Number nvarchar(40) NOT NULL, Name nvarchar(200) NOT NULL, Model nvarchar(120) NOT NULL,
              Profile nvarchar(32) NOT NULL, Host nvarchar(253) NULL, Port int NULL, Enabled bit NOT NULL,
              CreationFingerprint nvarchar(64) NOT NULL, CreatedByUserId uniqueidentifier NOT NULL,
              CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NOT NULL, Version rowversion NOT NULL,
              CONSTRAINT CK_Devices_Number CHECK (LEN([Number]) BETWEEN 1 AND 40 AND [Number] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Number],1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
              CONSTRAINT CK_Devices_Profile CHECK (([Profile] = N'zk-standalone' AND [Host] IS NOT NULL AND [Port] IS NOT NULL AND [Port] BETWEEN 1 AND 65535) OR ([Profile] IN (N'zk-push', N'offline-file') AND [Host] IS NULL AND [Port] IS NULL)),
              CONSTRAINT CK_Devices_Utc CHECK (DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET,[UpdatedAtUtc]) = 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]),
              CONSTRAINT FK_Devices_Sites_SiteId FOREIGN KEY(SiteId) REFERENCES dbo.Sites(Id),
              CONSTRAINT FK_Devices_EventSources_SourceId FOREIGN KEY(SourceId) REFERENCES dbo.EventSources(Id),
              CONSTRAINT FK_Devices_Users_CreatedByUserId FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(Id)
            );
            CREATE UNIQUE INDEX IX_Devices_Number ON dbo.Devices(Number);
            CREATE UNIQUE INDEX IX_Devices_SourceId ON dbo.Devices(SourceId);
            CREATE INDEX IX_Devices_SiteId ON dbo.Devices(SiteId);
            CREATE INDEX IX_Devices_CreatedByUserId ON dbo.Devices(CreatedByUserId);
            CREATE TABLE dbo.DeviceSecrets (
              DeviceId uniqueidentifier NOT NULL CONSTRAINT PK_DeviceSecrets PRIMARY KEY,
              ProtectedValue nvarchar(2048) NOT NULL,
              CONSTRAINT FK_DeviceSecrets_Devices_DeviceId FOREIGN KEY(DeviceId) REFERENCES dbo.Devices(Id)
            );
            """);
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER dbo.TR_Devices_Identity ON dbo.Devices AFTER UPDATE, DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted) AND NOT EXISTS(SELECT 1 FROM inserted)
                THROW 51014, ''Device registry history cannot be deleted; disable instead.'', 1;
              IF UPDATE(Id) OR UPDATE(SourceId) OR UPDATE(CreationFingerprint) OR UPDATE(CreatedByUserId) OR UPDATE(CreatedAtUtc)
                THROW 51014, ''Device identity and creation evidence are immutable.'', 1;
            END');
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS(SELECT 1 FROM dbo.Devices) THROW 51014, 'Registered devices prevent destructive downgrade.', 1;
            DROP TABLE dbo.DeviceSecrets;
            DROP TABLE dbo.Devices;
            """);
    }
}
