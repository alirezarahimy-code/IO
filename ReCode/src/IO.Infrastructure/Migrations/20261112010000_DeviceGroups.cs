using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IO.Infrastructure.Migrations;

[DbContext(typeof(IoDbContext))]
[Migration("20261112010000_DeviceGroups")]
public sealed class DeviceGroups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE dbo.DeviceGroups (
              Id uniqueidentifier NOT NULL CONSTRAINT PK_DeviceGroups PRIMARY KEY,
              CompanyId uniqueidentifier NOT NULL, ParentId uniqueidentifier NULL, Name nvarchar(150) NOT NULL,
              CreationFingerprint nvarchar(64) NOT NULL, CreatedByUserId uniqueidentifier NOT NULL,
              CreatedAtUtc datetimeoffset NOT NULL, UpdatedAtUtc datetimeoffset NOT NULL,
              DeletedAtUtc datetimeoffset NULL, DeletedByUserId uniqueidentifier NULL, DeletedFromVersion varbinary(8) NULL,
              Version rowversion NOT NULL,
              CONSTRAINT CK_DeviceGroups_Parent CHECK ([ParentId] IS NULL OR [ParentId] <> [Id]),
              CONSTRAINT CK_DeviceGroups_Deletion CHECK (([DeletedAtUtc] IS NULL AND [DeletedByUserId] IS NULL AND [DeletedFromVersion] IS NULL) OR ([DeletedAtUtc] IS NOT NULL AND [DeletedByUserId] IS NOT NULL AND [DeletedFromVersion] IS NOT NULL AND DATALENGTH([DeletedFromVersion]) = 8)),
              CONSTRAINT CK_DeviceGroups_Utc CHECK (DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET,[UpdatedAtUtc]) = 0 AND DATEPART(TZOFFSET,[DeletedAtUtc]) = 0 AND [UpdatedAtUtc] >= [CreatedAtUtc] AND ([DeletedAtUtc] IS NULL OR [DeletedAtUtc] >= [CreatedAtUtc])),
              CONSTRAINT FK_DeviceGroups_Companies_CompanyId FOREIGN KEY(CompanyId) REFERENCES dbo.Companies(Id),
              CONSTRAINT FK_DeviceGroups_DeviceGroups_ParentId FOREIGN KEY(ParentId) REFERENCES dbo.DeviceGroups(Id),
              CONSTRAINT FK_DeviceGroups_Users_CreatedByUserId FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(Id),
              CONSTRAINT FK_DeviceGroups_Users_DeletedByUserId FOREIGN KEY(DeletedByUserId) REFERENCES dbo.Users(Id)
            );
            CREATE UNIQUE INDEX IX_DeviceGroups_CompanyId_ParentId_Name ON dbo.DeviceGroups(CompanyId,ParentId,Name) WHERE DeletedAtUtc IS NULL;
            CREATE INDEX IX_DeviceGroups_ParentId ON dbo.DeviceGroups(ParentId);
            CREATE INDEX IX_DeviceGroups_CreatedByUserId ON dbo.DeviceGroups(CreatedByUserId);
            CREATE INDEX IX_DeviceGroups_DeletedByUserId ON dbo.DeviceGroups(DeletedByUserId);
            ALTER TABLE dbo.Devices ADD GroupId uniqueidentifier NULL;
            ALTER TABLE dbo.Devices ADD CONSTRAINT FK_Devices_DeviceGroups_GroupId FOREIGN KEY(GroupId) REFERENCES dbo.DeviceGroups(Id);
            CREATE INDEX IX_Devices_GroupId ON dbo.Devices(GroupId);
            """);
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER dbo.TR_DeviceGroups_Integrity ON dbo.DeviceGroups AFTER INSERT, UPDATE, DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id))
                THROW 51015, ''Groups retain tombstones; physical deletion is forbidden.'', 1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE
                d.DeletedAtUtc IS NOT NULL OR i.CompanyId<>d.CompanyId OR i.CreationFingerprint<>d.CreationFingerprint OR i.CreatedByUserId<>d.CreatedByUserId OR i.CreatedAtUtc<>d.CreatedAtUtc)
                THROW 51015, ''Group identity and deleted history are immutable.'', 1;
              IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceGroups p ON p.Id=i.ParentId WHERE i.DeletedAtUtc IS NULL AND (p.CompanyId<>i.CompanyId OR p.DeletedAtUtc IS NOT NULL))
                THROW 51015, ''Group parent must be active in the same company.'', 1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE i.DeletedAtUtc IS NOT NULL AND
                (EXISTS(SELECT 1 FROM dbo.DeviceGroups c WHERE c.ParentId=i.Id AND c.DeletedAtUtc IS NULL) OR EXISTS(SELECT 1 FROM dbo.Devices d WHERE d.GroupId=i.Id)))
                THROW 51015, ''Only empty leaf groups can be deleted.'', 1;
              DECLARE @cycle bit=0;
              ;WITH ancestors AS (
                SELECT i.Id AS Origin,i.ParentId FROM inserted i WHERE i.DeletedAtUtc IS NULL
                UNION ALL SELECT a.Origin,p.ParentId FROM ancestors a JOIN dbo.DeviceGroups p ON p.Id=a.ParentId WHERE a.ParentId<>a.Origin
              ) SELECT TOP(1) @cycle=1 FROM ancestors WHERE ParentId=Origin OPTION(MAXRECURSION 0);
              IF @cycle=1 THROW 51015, ''Group hierarchy cycles are forbidden.'', 1;
            END');
            """);
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER dbo.TR_Devices_GroupIntegrity ON dbo.Devices AFTER INSERT, UPDATE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.Sites s ON s.Id=i.SiteId JOIN dbo.DeviceGroups g ON g.Id=i.GroupId WHERE g.CompanyId<>s.CompanyId OR g.DeletedAtUtc IS NOT NULL)
                THROW 51015, ''Device group must be active in the installation company.'', 1;
            END');
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS(SELECT 1 FROM dbo.DeviceGroups) OR EXISTS(SELECT 1 FROM dbo.Devices WHERE GroupId IS NOT NULL)
              THROW 51015, 'Group history prevents destructive downgrade.', 1;
            DROP TRIGGER dbo.TR_Devices_GroupIntegrity;
            ALTER TABLE dbo.Devices DROP CONSTRAINT FK_Devices_DeviceGroups_GroupId;
            DROP INDEX IX_Devices_GroupId ON dbo.Devices;
            ALTER TABLE dbo.Devices DROP COLUMN GroupId;
            DROP TABLE dbo.DeviceGroups;
            """);
    }
}
