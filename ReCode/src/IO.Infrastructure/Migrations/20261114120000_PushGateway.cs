using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261114120000_PushGateway")]
public sealed class PushGateway:Migration
{
 protected override void Up(MigrationBuilder m)=>m.Sql("""
 CREATE TABLE dbo.PushGatewayBindings(
 Id uniqueidentifier NOT NULL CONSTRAINT PK_PushGatewayBindings PRIMARY KEY,
 GatewayId uniqueidentifier NOT NULL,DeviceId uniqueidentifier NOT NULL,SourceId uniqueidentifier NOT NULL,
 ServiceUserId uniqueidentifier NOT NULL,CreatedByUserId uniqueidentifier NOT NULL,CreatedAtUtc datetimeoffset NOT NULL,
 ExpectedDeviceVersion varbinary(8) NOT NULL,Serial nvarchar(100) NOT NULL,TimeZoneId nvarchar(100) NOT NULL,
 KeyVersion nvarchar(64) NOT NULL,ProtectedKey nvarchar(4000) NOT NULL,
 RevokedByUserId uniqueidentifier NULL,RevokedAtUtc datetimeoffset NULL,Version rowversion NOT NULL,
 CONSTRAINT FK_PushGatewayBindings_Devices_DeviceId FOREIGN KEY(DeviceId) REFERENCES dbo.Devices(Id),
 CONSTRAINT FK_PushGatewayBindings_EventSources_SourceId FOREIGN KEY(SourceId) REFERENCES dbo.EventSources(Id),
 CONSTRAINT FK_PushGatewayBindings_Users_ServiceUserId FOREIGN KEY(ServiceUserId) REFERENCES dbo.Users(Id),
 CONSTRAINT FK_PushGatewayBindings_Users_CreatedByUserId FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(Id),
 CONSTRAINT FK_PushGatewayBindings_Users_RevokedByUserId FOREIGN KEY(RevokedByUserId) REFERENCES dbo.Users(Id),
 CONSTRAINT CK_PushGatewayBindings_Bounds CHECK(DATALENGTH(ExpectedDeviceVersion)=8 AND DATALENGTH(Serial) BETWEEN 2 AND 200 AND DATALENGTH(TimeZoneId) BETWEEN 2 AND 200 AND DATALENGTH(KeyVersion) BETWEEN 2 AND 128 AND DATALENGTH(ProtectedKey) BETWEEN 2 AND 8000 AND DATEPART(TZOFFSET,CreatedAtUtc)=0 AND ((RevokedByUserId IS NULL AND RevokedAtUtc IS NULL) OR (RevokedByUserId IS NOT NULL AND RevokedAtUtc>=CreatedAtUtc AND DATEPART(TZOFFSET,RevokedAtUtc)=0)))
 );
 CREATE UNIQUE INDEX IX_PushGatewayBindings_GatewayId_DeviceId ON dbo.PushGatewayBindings(GatewayId,DeviceId) WHERE RevokedAtUtc IS NULL;
 CREATE INDEX IX_PushGatewayBindings_DeviceId ON dbo.PushGatewayBindings(DeviceId);
 CREATE INDEX IX_PushGatewayBindings_SourceId ON dbo.PushGatewayBindings(SourceId);
 CREATE INDEX IX_PushGatewayBindings_ServiceUserId ON dbo.PushGatewayBindings(ServiceUserId);
 CREATE INDEX IX_PushGatewayBindings_CreatedByUserId ON dbo.PushGatewayBindings(CreatedByUserId);
 CREATE INDEX IX_PushGatewayBindings_RevokedByUserId ON dbo.PushGatewayBindings(RevokedByUserId);
 CREATE TABLE dbo.PushGatewayReceipts(
 Id uniqueidentifier NOT NULL CONSTRAINT PK_PushGatewayReceipts PRIMARY KEY,
 BindingId uniqueidentifier NOT NULL,GatewayId uniqueidentifier NOT NULL,DeviceId uniqueidentifier NOT NULL,TransferId uniqueidentifier NOT NULL,RequestFingerprint nvarchar(64) NOT NULL,
 CONSTRAINT FK_PushGatewayReceipts_PushGatewayBindings_BindingId FOREIGN KEY(BindingId) REFERENCES dbo.PushGatewayBindings(Id),
 CONSTRAINT FK_PushGatewayReceipts_DevicePushJournal_Id FOREIGN KEY(Id) REFERENCES dbo.DevicePushJournal(Id),
 CONSTRAINT CK_PushGatewayReceipts_Fingerprint CHECK(DATALENGTH(RequestFingerprint)=128 AND RequestFingerprint COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%')
 );
 CREATE UNIQUE INDEX IX_PushGatewayReceipts_GatewayId_DeviceId_TransferId ON dbo.PushGatewayReceipts(GatewayId,DeviceId,TransferId);
 CREATE INDEX IX_PushGatewayReceipts_BindingId ON dbo.PushGatewayReceipts(BindingId);
 EXEC(N'CREATE TRIGGER dbo.TR_PushGatewayBindings_History ON dbo.PushGatewayBindings AFTER INSERT,UPDATE,DELETE AS
 BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL) THROW 51037,''Gateway binding history cannot be deleted.'',1;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE EXISTS(SELECT d.GatewayId,d.DeviceId,d.SourceId,d.ServiceUserId,d.CreatedByUserId,d.CreatedAtUtc,d.ExpectedDeviceVersion,d.Serial COLLATE Latin1_General_100_BIN2,DATALENGTH(d.Serial),d.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(d.TimeZoneId),d.KeyVersion COLLATE Latin1_General_100_BIN2,DATALENGTH(d.KeyVersion),d.ProtectedKey COLLATE Latin1_General_100_BIN2,DATALENGTH(d.ProtectedKey) EXCEPT SELECT i.GatewayId,i.DeviceId,i.SourceId,i.ServiceUserId,i.CreatedByUserId,i.CreatedAtUtc,i.ExpectedDeviceVersion,i.Serial COLLATE Latin1_General_100_BIN2,DATALENGTH(i.Serial),i.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(i.TimeZoneId),i.KeyVersion COLLATE Latin1_General_100_BIN2,DATALENGTH(i.KeyVersion),i.ProtectedKey COLLATE Latin1_General_100_BIN2,DATALENGTH(i.ProtectedKey)) OR (d.RevokedAtUtc IS NOT NULL AND EXISTS(SELECT d.RevokedAtUtc,d.RevokedByUserId EXCEPT SELECT i.RevokedAtUtc,i.RevokedByUserId))) THROW 51037,''Only one-way gateway revocation is permitted.'',1;
 END');
 EXEC(N'CREATE TRIGGER dbo.TR_PushGatewayReceipts_Immutable ON dbo.PushGatewayReceipts AFTER INSERT,UPDATE,DELETE AS
 BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted) THROW 51037,''Gateway receipts are immutable.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.PushGatewayBindings b ON b.Id=i.BindingId JOIN dbo.DevicePushJournal j ON j.Id=i.Id WHERE i.GatewayId<>b.GatewayId OR i.DeviceId<>b.DeviceId OR j.DeviceId<>b.DeviceId OR j.SourceId<>b.SourceId OR j.RetainedByUserId<>b.ServiceUserId OR j.ExpectedDeviceVersion<>b.ExpectedDeviceVersion OR b.RevokedAtUtc IS NOT NULL OR EXISTS(SELECT j.Serial COLLATE Latin1_General_100_BIN2,DATALENGTH(j.Serial),j.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(j.TimeZoneId) EXCEPT SELECT b.Serial COLLATE Latin1_General_100_BIN2,DATALENGTH(b.Serial),b.TimeZoneId COLLATE Latin1_General_100_BIN2,DATALENGTH(b.TimeZoneId))) THROW 51037,''Gateway receipt must pin committed evidence and active binding.'',1;
 END');
 """);
 protected override void Down(MigrationBuilder m)=>m.Sql("""
 IF EXISTS(SELECT 1 FROM dbo.PushGatewayBindings) OR EXISTS(SELECT 1 FROM dbo.PushGatewayReceipts) THROW 51037,'Gateway history prevents downgrade.',1;
 DROP TABLE dbo.PushGatewayReceipts;DROP TABLE dbo.PushGatewayBindings;
 """);
}
