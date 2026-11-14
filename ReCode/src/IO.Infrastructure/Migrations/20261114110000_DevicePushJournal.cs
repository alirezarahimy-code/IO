using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261114110000_DevicePushJournal")]
public sealed class DevicePushJournal:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE dbo.DevicePushJournal(
         [Id] uniqueidentifier NOT NULL,
         [DeviceId] uniqueidentifier NOT NULL,
         [SourceId] uniqueidentifier NOT NULL,
         [SiteId] uniqueidentifier NOT NULL,
         [CompanyId] uniqueidentifier NOT NULL,
         [RetainedByUserId] uniqueidentifier NOT NULL,
         [RetainedAtUtc] datetimeoffset NOT NULL,
         [ExpectedDeviceVersion] varbinary(8) NOT NULL,
         [OriginalBody] varbinary(max) NOT NULL,
         [Serial] nvarchar(100) NOT NULL,
         [Stamp] nvarchar(200) NULL,
         [Contract] nvarchar(64) NOT NULL,
         [TimeZoneId] nvarchar(100) NOT NULL,
         [PayloadHash] nvarchar(64) NOT NULL,
         [RowsJson] nvarchar(max) NOT NULL,
         [RowCount] int NOT NULL,
         [ParsedCount] int NOT NULL,
         CONSTRAINT PK_DevicePushJournal PRIMARY KEY(Id),
         CONSTRAINT CK_DevicePushJournal_Bounds CHECK (DATALENGTH([OriginalBody]) BETWEEN 1 AND 1048576 AND DATALENGTH([ExpectedDeviceVersion])=8 AND [RowCount] BETWEEN 1 AND 10000 AND [ParsedCount] BETWEEN 0 AND [RowCount]),
         CONSTRAINT CK_DevicePushJournal_Hash CHECK (DATALENGTH([PayloadHash])=128 AND [PayloadHash] COLLATE Latin1_General_100_BIN2=CONVERT(nvarchar(64),HASHBYTES('SHA2_256',[OriginalBody]),2) COLLATE Latin1_General_100_BIN2),
         CONSTRAINT CK_DevicePushJournal_Contract CHECK ([Contract] COLLATE Latin1_General_100_BIN2=N'zk-attendance-push-20200325-attlog-seven-utf8-v1' AND DATALENGTH([Contract])=96 AND DATALENGTH([Serial]) BETWEEN 2 AND 200 AND DATALENGTH([TimeZoneId]) BETWEEN 2 AND 200 AND ISJSON([RowsJson])=1),
         CONSTRAINT CK_DevicePushJournal_Utc CHECK (DATEPART(TZOFFSET,[RetainedAtUtc])=0),
         CONSTRAINT FK_DevicePushJournal_Devices_DeviceId FOREIGN KEY([DeviceId]) REFERENCES dbo.[Devices](Id),
         CONSTRAINT FK_DevicePushJournal_EventSources_SourceId FOREIGN KEY([SourceId]) REFERENCES dbo.[EventSources](Id),
         CONSTRAINT FK_DevicePushJournal_Sites_SiteId FOREIGN KEY([SiteId]) REFERENCES dbo.[Sites](Id),
         CONSTRAINT FK_DevicePushJournal_Companies_CompanyId FOREIGN KEY([CompanyId]) REFERENCES dbo.[Companies](Id),
         CONSTRAINT FK_DevicePushJournal_Users_RetainedByUserId FOREIGN KEY([RetainedByUserId]) REFERENCES dbo.[Users](Id)
        );
        CREATE INDEX IX_DevicePushJournal_DeviceId ON dbo.DevicePushJournal(DeviceId);
        CREATE INDEX IX_DevicePushJournal_SourceId ON dbo.DevicePushJournal(SourceId);
        CREATE INDEX IX_DevicePushJournal_SiteId ON dbo.DevicePushJournal(SiteId);
        CREATE INDEX IX_DevicePushJournal_CompanyId ON dbo.DevicePushJournal(CompanyId);
        CREATE INDEX IX_DevicePushJournal_RetainedByUserId ON dbo.DevicePushJournal(RetainedByUserId);
        CREATE INDEX IX_DevicePushJournal_DeviceId_PayloadHash ON dbo.DevicePushJournal(DeviceId,PayloadHash);
        EXEC(N'CREATE TRIGGER dbo.TR_DevicePushJournal_Immutable ON dbo.DevicePushJournal AFTER INSERT,UPDATE,DELETE AS
        BEGIN SET NOCOUNT ON;
         IF EXISTS(SELECT 1 FROM deleted) THROW 51028,''PUSH original evidence and retention receipts are immutable.'',1;
         IF EXISTS(SELECT 1 FROM inserted i WHERE LEFT(i.RowsJson,1)<>N''['' OR (SELECT COUNT(*) FROM OPENJSON(i.RowsJson))<>i.RowCount OR (SELECT COUNT(*) FROM OPENJSON(i.RowsJson) WITH(Status nvarchar(64)) j WHERE j.Status COLLATE Latin1_General_100_BIN2=N''parsed-requires-mapping-review'')<>i.ParsedCount) THROW 51028,''PUSH row snapshot counts must match evidence.'',1;
        END');
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.DevicePushJournal) THROW 51028,'Retained PUSH evidence prevents destructive downgrade.',1;
        DROP TABLE dbo.DevicePushJournal;
        """);
}
