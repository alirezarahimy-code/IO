using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261116120000_AllDataErases")]
public sealed class AllDataErases:Migration
{
 protected override void Up(MigrationBuilder m)=>m.Sql("""
CREATE TABLE dbo.DeviceAllDataErases(
Id uniqueidentifier NOT NULL CONSTRAINT PK_DeviceAllDataErases PRIMARY KEY,
DeviceId uniqueidentifier NOT NULL,
ArchiveId uniqueidentifier NOT NULL,
RecoveryOfId uniqueidentifier NULL,
DeviceVersion varbinary(8) NOT NULL,
ArchiveVersion varbinary(8) NOT NULL,
RequestedByUserId uniqueidentifier NOT NULL,
RequestedAtUtc datetimeoffset NOT NULL,
QuiescedAtUtc datetimeoffset NOT NULL,
EvidenceReference nvarchar(200) NOT NULL,
ApprovalJson nvarchar(max) NOT NULL,
ArchiveDigest nvarchar(64) NOT NULL,
ArchiveCount int NOT NULL,
Family nvarchar(30) NOT NULL,
AttendanceArchiveId uniqueidentifier NOT NULL,
AttendanceArchiveVersion varbinary(8) NOT NULL,
AttendanceCount int NOT NULL,
AttendanceDigest nvarchar(64) NOT NULL,
RequestHash nvarchar(64) NOT NULL,
State nvarchar(40) NOT NULL,
ApprovedByUserId uniqueidentifier NULL,
ApprovedAtUtc datetimeoffset NULL,
ReviewReference nvarchar(200) NULL,
ServiceUserId uniqueidentifier NULL,
ClaimId uniqueidentifier NULL,
ClaimedAtUtc datetimeoffset NULL,
CancelRequestedAtUtc datetimeoffset NULL,
FinishedAtUtc datetimeoffset NULL,
Version rowversion NOT NULL,
CONSTRAINT FK_DeviceAllDataErases_Devices_DeviceId FOREIGN KEY(DeviceId) REFERENCES dbo.Devices(Id),
CONSTRAINT FK_DeviceAllDataErases_DeviceUserDownloads_ArchiveId FOREIGN KEY(ArchiveId) REFERENCES dbo.DeviceUserDownloads(Id),
CONSTRAINT FK_DeviceAllDataErases_DeviceAllDataErases_RecoveryOfId FOREIGN KEY(RecoveryOfId) REFERENCES dbo.DeviceAllDataErases(Id),
CONSTRAINT FK_DeviceAllDataErases_Users_RequestedByUserId FOREIGN KEY(RequestedByUserId) REFERENCES dbo.Users(Id),
CONSTRAINT FK_DeviceAllDataErases_Users_ApprovedByUserId FOREIGN KEY(ApprovedByUserId) REFERENCES dbo.Users(Id),
CONSTRAINT FK_DeviceAllDataErases_Users_ServiceUserId FOREIGN KEY(ServiceUserId) REFERENCES dbo.Users(Id),
CONSTRAINT FK_DeviceAllDataErases_AttendanceArchive FOREIGN KEY(AttendanceArchiveId) REFERENCES dbo.DeviceAttendanceDownloads(Id),
CONSTRAINT CK_DeviceAllDataErases_Request CHECK(DATALENGTH(AttendanceArchiveVersion)=8 AND AttendanceCount BETWEEN 0 AND 100000 AND DATALENGTH(AttendanceDigest)=128 AND AttendanceDigest COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND DATALENGTH(DeviceVersion)=8 AND DATALENGTH(ArchiveVersion)=8 AND ArchiveCount BETWEEN 0 AND 1000 AND DATALENGTH(ArchiveDigest)=128 AND ArchiveDigest COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND DATALENGTH(RequestHash)=128 AND RequestHash COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND ISJSON(ApprovalJson)=1 AND DATALENGTH(ApprovalJson)<=16384 AND DATALENGTH(EvidenceReference) BETWEEN 2 AND 400 AND QuiescedAtUtc<=RequestedAtUtc AND (RecoveryOfId IS NULL OR RecoveryOfId<>Id)),
CONSTRAINT CK_DeviceAllDataErases_Family CHECK(Family COLLATE Latin1_General_100_BIN2=N'all-data'),
CONSTRAINT CK_DeviceAllDataErases_State CHECK(State COLLATE Latin1_General_100_BIN2 IN(N'proposed',N'pending',N'running',N'observed',N'released',N'cancelled',N'expired',N'revoked',N'all-data-cleared-verified',N'already-no-profile-data',N'archive-mismatch',N'failed-before-write',N'unknown',N'unsupported',N'inspection-failed') AND (State NOT IN(N'observed',N'released',N'inspection-failed') OR RecoveryOfId IS NOT NULL) AND (RecoveryOfId IS NULL OR State IN(N'pending',N'running',N'observed',N'released',N'cancelled',N'expired',N'revoked',N'inspection-failed'))),
CONSTRAINT CK_DeviceAllDataErases_Review CHECK((ApprovedByUserId IS NULL AND ApprovedAtUtc IS NULL AND ReviewReference IS NULL AND ((RecoveryOfId IS NULL AND State IN(N'proposed',N'cancelled',N'expired')) OR (RecoveryOfId IS NOT NULL AND State<>N'released'))) OR (ApprovedByUserId IS NOT NULL AND ApprovedAtUtc IS NOT NULL AND DATALENGTH(ReviewReference) BETWEEN 2 AND 400 AND ReviewReference IS NOT NULL AND ApprovedByUserId<>RequestedByUserId AND ApprovedAtUtc>=RequestedAtUtc AND ((RecoveryOfId IS NULL AND State NOT IN(N'proposed',N'expired')) OR (RecoveryOfId IS NOT NULL AND State=N'released')))),
CONSTRAINT CK_DeviceAllDataErases_Lifecycle CHECK((State IN(N'proposed',N'pending') AND ServiceUserId IS NULL AND ClaimId IS NULL AND ClaimedAtUtc IS NULL AND CancelRequestedAtUtc IS NULL AND FinishedAtUtc IS NULL) OR (State=N'running' AND ServiceUserId IS NOT NULL AND ClaimId IS NOT NULL AND ClaimedAtUtc IS NOT NULL AND FinishedAtUtc IS NULL) OR (State IN(N'cancelled',N'expired',N'revoked') AND FinishedAtUtc IS NOT NULL AND ServiceUserId IS NULL AND ClaimId IS NULL AND ClaimedAtUtc IS NULL AND CancelRequestedAtUtc IS NULL) OR (State NOT IN(N'proposed',N'pending',N'running',N'cancelled',N'expired',N'revoked') AND FinishedAtUtc IS NOT NULL AND ServiceUserId IS NOT NULL AND ClaimId IS NOT NULL AND ClaimedAtUtc IS NOT NULL)),
CONSTRAINT CK_DeviceAllDataErases_Time CHECK(DATEPART(TZOFFSET,RequestedAtUtc)=0 AND DATEPART(TZOFFSET,QuiescedAtUtc)=0 AND DATEPART(TZOFFSET,ApprovedAtUtc)=0 AND DATEPART(TZOFFSET,ClaimedAtUtc)=0 AND DATEPART(TZOFFSET,FinishedAtUtc)=0 AND DATEPART(TZOFFSET,CancelRequestedAtUtc)=0 AND (FinishedAtUtc IS NULL OR FinishedAtUtc>=COALESCE(ClaimedAtUtc,RequestedAtUtc)) AND (ClaimedAtUtc IS NULL OR ClaimedAtUtc>=RequestedAtUtc) AND (CancelRequestedAtUtc IS NULL OR CancelRequestedAtUtc>=ClaimedAtUtc) AND (State NOT IN(N'all-data-cleared-verified',N'already-no-profile-data',N'observed',N'released') OR CancelRequestedAtUtc IS NULL))
);
CREATE INDEX IX_DeviceAllDataErases_AttendanceArchiveId ON dbo.DeviceAllDataErases(AttendanceArchiveId);
CREATE INDEX IX_DeviceAllDataErases_ArchiveId ON dbo.DeviceAllDataErases(ArchiveId);
CREATE INDEX IX_DeviceAllDataErases_RequestedByUserId ON dbo.DeviceAllDataErases(RequestedByUserId);
CREATE INDEX IX_DeviceAllDataErases_ApprovedByUserId ON dbo.DeviceAllDataErases(ApprovedByUserId);
CREATE INDEX IX_DeviceAllDataErases_ServiceUserId ON dbo.DeviceAllDataErases(ServiceUserId);
CREATE INDEX IX_DeviceAllDataErases_DeviceId_State ON dbo.DeviceAllDataErases(DeviceId,State);
CREATE UNIQUE INDEX IX_DeviceAllDataErases_RecoveryOfId ON dbo.DeviceAllDataErases(RecoveryOfId) WHERE State=N'released';
CREATE UNIQUE INDEX UX_DeviceAllDataErases_Device_Active ON dbo.DeviceAllDataErases(DeviceId) WHERE State IN(N'proposed',N'pending',N'running',N'observed');
EXEC(N'CREATE TRIGGER dbo.TR_DeviceAllDataErases_History ON dbo.DeviceAllDataErases AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL) THROW 51045,''Protected erase history cannot be deleted.'',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND ((i.RecoveryOfId IS NULL AND i.State<>N''proposed'') OR (i.RecoveryOfId IS NOT NULL AND i.State<>N''pending''))) THROW 51045,''Separate destructive review or read-only recovery inspection required.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE d.State NOT IN(N''proposed'',N''pending'',N''running'',N''observed'') OR EXISTS(SELECT d.DeviceId,d.ArchiveId,d.RecoveryOfId,d.DeviceVersion,d.ArchiveVersion,d.RequestedByUserId,d.RequestedAtUtc,d.QuiescedAtUtc,CONVERT(varbinary(max),d.EvidenceReference),CONVERT(varbinary(max),d.ApprovalJson),CONVERT(varbinary(max),d.ArchiveDigest),d.ArchiveCount,CONVERT(varbinary(max),d.RequestHash),CONVERT(varbinary(max),d.Family),d.AttendanceArchiveId,d.AttendanceArchiveVersion,d.AttendanceCount,CONVERT(varbinary(max),d.AttendanceDigest) EXCEPT SELECT i.DeviceId,i.ArchiveId,i.RecoveryOfId,i.DeviceVersion,i.ArchiveVersion,i.RequestedByUserId,i.RequestedAtUtc,i.QuiescedAtUtc,CONVERT(varbinary(max),i.EvidenceReference),CONVERT(varbinary(max),i.ApprovalJson),CONVERT(varbinary(max),i.ArchiveDigest),i.ArchiveCount,CONVERT(varbinary(max),i.RequestHash),CONVERT(varbinary(max),i.Family),i.AttendanceArchiveId,i.AttendanceArchiveVersion,i.AttendanceCount,CONVERT(varbinary(max),i.AttendanceDigest)) OR (d.State=N''proposed'' AND i.State NOT IN(N''pending'',N''cancelled'',N''expired'')) OR (d.State=N''pending'' AND i.State NOT IN(N''running'',N''cancelled'',N''expired'',N''revoked'')) OR (d.State=N''observed'' AND i.State NOT IN(N''released'',N''inspection-failed'')) OR (d.State=N''running'' AND i.State IN(N''proposed'',N''pending'',N''released'',N''cancelled'',N''expired'',N''revoked'')) OR (d.ApprovedByUserId IS NOT NULL AND EXISTS(SELECT d.ApprovedByUserId,d.ApprovedAtUtc,CONVERT(varbinary(max),d.ReviewReference) EXCEPT SELECT i.ApprovedByUserId,i.ApprovedAtUtc,CONVERT(varbinary(max),i.ReviewReference))) OR (d.State IN(N''running'',N''observed'') AND EXISTS(SELECT d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc EXCEPT SELECT i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc)) OR (d.State=N''observed'' AND EXISTS(SELECT d.FinishedAtUtc EXCEPT SELECT i.FinishedAtUtc)) OR (d.CancelRequestedAtUtc IS NOT NULL AND EXISTS(SELECT d.CancelRequestedAtUtc EXCEPT SELECT i.CancelRequestedAtUtc))) THROW 51045,''Intent and terminal history immutable; no replay or retarget.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceUserDownloads a ON a.Id=i.ArchiveId WHERE a.DeviceId<>i.DeviceId OR a.State<>N''succeeded'' OR a.ExpectedDeviceVersion<>i.DeviceVersion OR a.ReceivedCount<>i.ArchiveCount OR CONVERT(varbinary(max),a.ReadFormat)<>CONVERT(varbinary(max),JSON_VALUE(i.ApprovalJson,''$.Inventory.UserFormat'')) OR a.RequestedAtUtc<i.QuiescedAtUtc OR a.FinishedAtUtc>i.RequestedAtUtc) THROW 51045,''Fresh complete same-device metadata archive after exclusive control required.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceAllDataErases p ON p.Id=i.RecoveryOfId WHERE p.State<>N''unknown'' OR p.RecoveryOfId IS NOT NULL OR p.DeviceId<>i.DeviceId OR p.DeviceVersion<>i.DeviceVersion OR p.Family<>i.Family OR EXISTS(SELECT CONVERT(varbinary(max),p.ApprovalJson) EXCEPT SELECT CONVERT(varbinary(max),i.ApprovalJson)) OR p.FinishedAtUtc>i.QuiescedAtUtc) THROW 51045,''Recovery pins exact original unknown family and subject; never original success.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceAttendanceDownloads a ON a.Id=i.AttendanceArchiveId WHERE a.DeviceId<>i.DeviceId OR a.State<>N''succeeded'' OR a.ExpectedDeviceVersion<>i.DeviceVersion OR a.ReceivedCount<>i.AttendanceCount OR CONVERT(varbinary(max),a.ReadFormat)<>CONVERT(varbinary(max),JSON_VALUE(i.ApprovalJson,''$.Attendance.ReadFormat'')) OR a.RequestedAtUtc<i.QuiescedAtUtc OR a.FinishedAtUtc>i.RequestedAtUtc) THROW 51045,''Complete same-installation native attendance archive required.'',1;
END');
""");
 protected override void Down(MigrationBuilder m)=>m.Sql("IF EXISTS(SELECT 1 FROM dbo.DeviceAllDataErases) THROW 51045,'Protected erase history prevents downgrade.',1;DROP TABLE dbo.DeviceAllDataErases;");
}
