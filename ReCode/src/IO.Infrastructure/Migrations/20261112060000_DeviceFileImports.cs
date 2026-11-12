using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261112060000_DeviceFileImports")]
public sealed class DeviceFileImports:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE dbo.DeviceFileImports(
              [Id] uniqueidentifier NOT NULL,
              [DeviceId] uniqueidentifier NOT NULL,
              [SourceId] uniqueidentifier NOT NULL,
              [SiteId] uniqueidentifier NOT NULL,
              [CompanyId] uniqueidentifier NOT NULL,
              [CreatedByUserId] uniqueidentifier NOT NULL,
              [CreatedAtUtc] datetimeoffset NOT NULL,
              [ExpectedDeviceVersion] varbinary(8) NOT NULL,
              [OriginalFile] varbinary(max) NOT NULL,
              [FileHash] nvarchar(64) NOT NULL,
              [Calendar] nvarchar(16) NOT NULL,
              [TimeZoneId] nvarchar(100) NOT NULL,
              [MachineNumber] int NOT NULL,
              [InCode] int NOT NULL,
              [OutCode] int NOT NULL,
              [LineCount] int NOT NULL,
              [LastReviewedAtUtc] datetimeoffset NULL,
              [Version] rowversion NOT NULL,
              CONSTRAINT PK_DeviceFileImports PRIMARY KEY (Id),
              CONSTRAINT FK_DeviceFileImports_Devices_DeviceId FOREIGN KEY([DeviceId]) REFERENCES dbo.[Devices](Id),
              CONSTRAINT FK_DeviceFileImports_EventSources_SourceId FOREIGN KEY([SourceId]) REFERENCES dbo.[EventSources](Id),
              CONSTRAINT FK_DeviceFileImports_Sites_SiteId FOREIGN KEY([SiteId]) REFERENCES dbo.[Sites](Id),
              CONSTRAINT FK_DeviceFileImports_Companies_CompanyId FOREIGN KEY([CompanyId]) REFERENCES dbo.[Companies](Id),
              CONSTRAINT FK_DeviceFileImports_Users_CreatedByUserId FOREIGN KEY([CreatedByUserId]) REFERENCES dbo.[Users](Id),
              CONSTRAINT CK_DeviceFileImports_File CHECK (DATALENGTH([OriginalFile]) BETWEEN 1 AND 1048576 AND DATALENGTH([ExpectedDeviceVersion])=8 AND LEN([FileHash])=64 AND [LineCount] BETWEEN 1 AND 10000),
              CONSTRAINT CK_DeviceFileImports_Interpretation CHECK ([Calendar] COLLATE Latin1_General_100_BIN2 IN(N'gregorian',N'persian') AND [MachineNumber]>0 AND [InCode] BETWEEN 0 AND 255 AND [OutCode] BETWEEN 0 AND 255 AND [InCode]<>[OutCode]),
              CONSTRAINT CK_DeviceFileImports_Utc CHECK (DATEPART(TZOFFSET,[CreatedAtUtc])=0 AND DATEPART(TZOFFSET,[LastReviewedAtUtc])=0 AND ([LastReviewedAtUtc] IS NULL OR [LastReviewedAtUtc]>=[CreatedAtUtc]))
            );
            CREATE UNIQUE INDEX IX_DeviceFileImports_DeviceId_FileHash ON dbo.DeviceFileImports(DeviceId,FileHash);
            CREATE INDEX IX_DeviceFileImports_DeviceId ON dbo.DeviceFileImports(DeviceId);
            CREATE INDEX IX_DeviceFileImports_SourceId ON dbo.DeviceFileImports(SourceId);
            CREATE INDEX IX_DeviceFileImports_SiteId ON dbo.DeviceFileImports(SiteId);
            CREATE INDEX IX_DeviceFileImports_CompanyId ON dbo.DeviceFileImports(CompanyId);
            CREATE INDEX IX_DeviceFileImports_CreatedByUserId ON dbo.DeviceFileImports(CreatedByUserId);
            CREATE TABLE dbo.DeviceFileImportLines(
              [ImportId] uniqueidentifier NOT NULL,
              [LineNumber] int NOT NULL,
              [Status] nvarchar(64) NOT NULL,
              [RawEventId] uniqueidentifier NULL,
              [ConflictId] uniqueidentifier NULL,
              [ReviewedByUserId] uniqueidentifier NOT NULL,
              [ReviewedAtUtc] datetimeoffset NOT NULL,
              CONSTRAINT PK_DeviceFileImportLines PRIMARY KEY (ImportId,LineNumber),
              CONSTRAINT FK_DeviceFileImportLines_DeviceFileImports_ImportId FOREIGN KEY([ImportId]) REFERENCES dbo.[DeviceFileImports](Id),
              CONSTRAINT FK_DeviceFileImportLines_RawAttendanceEvents_RawEventId FOREIGN KEY([RawEventId]) REFERENCES dbo.[RawAttendanceEvents](Id),
              CONSTRAINT FK_DeviceFileImportLines_RawEventConflicts_ConflictId FOREIGN KEY([ConflictId]) REFERENCES dbo.[RawEventConflicts](Id),
              CONSTRAINT FK_DeviceFileImportLines_Users_ReviewedByUserId FOREIGN KEY([ReviewedByUserId]) REFERENCES dbo.[Users](Id),
              CONSTRAINT CK_DeviceFileImportLines_Receipt CHECK ([LineNumber] BETWEEN 1 AND 10000 AND LEN([Status])>0 AND DATEPART(TZOFFSET,[ReviewedAtUtc])=0 AND ([ConflictId] IS NULL OR [RawEventId] IS NOT NULL) AND (([Status] IN(N'imported',N'already-recorded') AND [RawEventId] IS NOT NULL AND [ConflictId] IS NULL) OR ([Status]=N'ingestion-conflict' AND [RawEventId] IS NOT NULL AND [ConflictId] IS NOT NULL) OR ([Status] NOT IN(N'imported',N'already-recorded',N'ingestion-conflict') AND [RawEventId] IS NULL AND [ConflictId] IS NULL)))
            );
            CREATE INDEX IX_DeviceFileImportLines_RawEventId ON dbo.DeviceFileImportLines(RawEventId);
            CREATE INDEX IX_DeviceFileImportLines_ConflictId ON dbo.DeviceFileImportLines(ConflictId);
            CREATE INDEX IX_DeviceFileImportLines_ReviewedByUserId ON dbo.DeviceFileImportLines(ReviewedByUserId);
            """);
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER dbo.TR_DeviceFileImports_History ON dbo.DeviceFileImports AFTER INSERT,UPDATE,DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id)) THROW 51019,''Original files retain history.'',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(SELECT d.DeviceId,d.SourceId,d.SiteId,d.CompanyId,d.CreatedByUserId,d.CreatedAtUtc,d.ExpectedDeviceVersion,d.OriginalFile,DATALENGTH(d.OriginalFile),d.FileHash COLLATE Latin1_General_100_BIN2,DATALENGTH(d.FileHash),d.Calendar COLLATE Latin1_General_100_BIN2,DATALENGTH(d.Calendar),d.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(d.TimeZoneId),d.MachineNumber,d.InCode,d.OutCode,d.LineCount EXCEPT SELECT i.DeviceId,i.SourceId,i.SiteId,i.CompanyId,i.CreatedByUserId,i.CreatedAtUtc,i.ExpectedDeviceVersion,i.OriginalFile,DATALENGTH(i.OriginalFile),i.FileHash COLLATE Latin1_General_100_BIN2,DATALENGTH(i.FileHash),i.Calendar COLLATE Latin1_General_100_BIN2,DATALENGTH(i.Calendar),i.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(i.TimeZoneId),i.MachineNumber,i.InCode,i.OutCode,i.LineCount)) THROW 51019,''Original file and interpretation are immutable.'',1;
            END');
            EXEC(N'CREATE TRIGGER dbo.TR_DeviceFileImportLines_History ON dbo.DeviceFileImportLines AFTER INSERT,UPDATE,DELETE AS
            BEGIN SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.ImportId=d.ImportId AND i.LineNumber=d.LineNumber)) THROW 51019,''File receipts are retained.'',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.ImportId=d.ImportId AND i.LineNumber=d.LineNumber WHERE d.RawEventId IS NOT NULL) THROW 51019,''Ingested receipts are immutable.'',1;
              IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceFileImports f ON f.Id=i.ImportId LEFT JOIN dbo.RawAttendanceEvents r ON r.Id=i.RawEventId LEFT JOIN dbo.RawEventConflicts c ON c.Id=i.ConflictId WHERE i.LineNumber>f.LineCount OR (i.RawEventId IS NOT NULL AND (r.Id IS NULL OR r.SourceId<>f.SourceId)) OR (i.ConflictId IS NOT NULL AND (c.OriginalEventId<>i.RawEventId OR c.SourceId<>f.SourceId))) THROW 51019,''Receipt must link the same retained file source.'',1;
            END');
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.DeviceFileImports) THROW 51019,'Original file evidence prevents destructive downgrade.',1;
        DROP TABLE dbo.DeviceFileImportLines;
        DROP TABLE dbo.DeviceFileImports;
        """);
}
