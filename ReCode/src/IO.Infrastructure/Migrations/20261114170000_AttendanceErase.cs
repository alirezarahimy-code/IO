using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261114170000_AttendanceErase")]
public sealed class AttendanceErase:Migration
{
 protected override void Up(MigrationBuilder m)=>m.Sql("""
CREATE TABLE dbo.DeviceAttendanceErases(
Id uniqueidentifier NOT NULL CONSTRAINT PK_DeviceAttendanceErases PRIMARY KEY,
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
CONSTRAINT FK_DeviceAttendanceErases_Devices_DeviceId FOREIGN KEY(DeviceId) REFERENCES dbo.Devices(Id),
CONSTRAINT FK_DeviceAttendanceErases_DeviceAttendanceDownloads_ArchiveId FOREIGN KEY(ArchiveId) REFERENCES dbo.DeviceAttendanceDownloads(Id),
CONSTRAINT FK_DeviceAttendanceErases_DeviceAttendanceErases_RecoveryOfId FOREIGN KEY(RecoveryOfId) REFERENCES dbo.DeviceAttendanceErases(Id),
CONSTRAINT FK_DeviceAttendanceErases_Users_RequestedByUserId FOREIGN KEY(RequestedByUserId) REFERENCES dbo.Users(Id),
CONSTRAINT FK_DeviceAttendanceErases_Users_ApprovedByUserId FOREIGN KEY(ApprovedByUserId) REFERENCES dbo.Users(Id),
CONSTRAINT FK_DeviceAttendanceErases_Users_ServiceUserId FOREIGN KEY(ServiceUserId) REFERENCES dbo.Users(Id),
CONSTRAINT CK_DeviceAttendanceErases_Request CHECK(DATALENGTH(DeviceVersion)=8 AND DATALENGTH(ArchiveVersion)=8 AND DATALENGTH(EvidenceReference) BETWEEN 2 AND 400 AND ISJSON(ApprovalJson)=1 AND DATALENGTH(ApprovalJson)<=16384 AND ArchiveCount BETWEEN 0 AND 100000 AND DATALENGTH(ArchiveDigest)=128 AND ArchiveDigest COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND DATALENGTH(RequestHash)=128 AND RequestHash COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND QuiescedAtUtc<=RequestedAtUtc AND (RecoveryOfId IS NULL OR RecoveryOfId<>Id)),
CONSTRAINT CK_DeviceAttendanceErases_State CHECK(State COLLATE Latin1_General_100_BIN2 IN(N'proposed',N'pending',N'running',N'cancelled',N'expired',N'revoked',N'deleted-verified',N'already-empty',N'archive-mismatch',N'failed-before-write',N'unknown',N'unsupported',N'released') AND (RecoveryOfId IS NULL OR State IN(N'proposed',N'cancelled',N'expired',N'released')) AND (State<>N'released' OR RecoveryOfId IS NOT NULL)),
CONSTRAINT CK_DeviceAttendanceErases_Review CHECK((ApprovedByUserId IS NULL AND ApprovedAtUtc IS NULL AND ReviewReference IS NULL AND State IN(N'proposed',N'cancelled',N'expired')) OR (ApprovedByUserId IS NOT NULL AND ApprovedAtUtc IS NOT NULL AND ReviewReference IS NOT NULL AND DATALENGTH(ReviewReference) BETWEEN 2 AND 400 AND ApprovedByUserId<>RequestedByUserId AND ApprovedAtUtc>=RequestedAtUtc AND State NOT IN(N'proposed',N'expired'))),
CONSTRAINT CK_DeviceAttendanceErases_Lifecycle CHECK((State IN(N'proposed',N'pending') AND ServiceUserId IS NULL AND ClaimId IS NULL AND ClaimedAtUtc IS NULL AND CancelRequestedAtUtc IS NULL AND FinishedAtUtc IS NULL) OR (State=N'running' AND ServiceUserId IS NOT NULL AND ClaimId IS NOT NULL AND ClaimedAtUtc IS NOT NULL AND ClaimedAtUtc>=ApprovedAtUtc AND FinishedAtUtc IS NULL) OR (State IN(N'cancelled',N'expired',N'revoked',N'released') AND FinishedAtUtc IS NOT NULL AND ServiceUserId IS NULL AND ClaimId IS NULL AND ClaimedAtUtc IS NULL AND CancelRequestedAtUtc IS NULL) OR (State IN(N'deleted-verified',N'already-empty',N'archive-mismatch',N'failed-before-write',N'unknown',N'unsupported') AND FinishedAtUtc IS NOT NULL AND ServiceUserId IS NOT NULL AND ClaimId IS NOT NULL AND ClaimedAtUtc IS NOT NULL)),
CONSTRAINT CK_DeviceAttendanceErases_Time CHECK(DATEPART(TZOFFSET,RequestedAtUtc)=0 AND DATEPART(TZOFFSET,QuiescedAtUtc)=0 AND DATEPART(TZOFFSET,ApprovedAtUtc)=0 AND DATEPART(TZOFFSET,ClaimedAtUtc)=0 AND DATEPART(TZOFFSET,FinishedAtUtc)=0 AND DATEPART(TZOFFSET,CancelRequestedAtUtc)=0 AND (FinishedAtUtc IS NULL OR FinishedAtUtc>=COALESCE(ClaimedAtUtc,ApprovedAtUtc,RequestedAtUtc)) AND (CancelRequestedAtUtc IS NULL OR CancelRequestedAtUtc>=ClaimedAtUtc) AND (State NOT IN(N'deleted-verified',N'already-empty') OR CancelRequestedAtUtc IS NULL))
);
CREATE INDEX IX_DeviceAttendanceErases_ArchiveId ON dbo.DeviceAttendanceErases(ArchiveId);
CREATE INDEX IX_DeviceAttendanceErases_RequestedByUserId ON dbo.DeviceAttendanceErases(RequestedByUserId);
CREATE INDEX IX_DeviceAttendanceErases_ApprovedByUserId ON dbo.DeviceAttendanceErases(ApprovedByUserId);
CREATE INDEX IX_DeviceAttendanceErases_ServiceUserId ON dbo.DeviceAttendanceErases(ServiceUserId);
CREATE INDEX IX_DeviceAttendanceErases_DeviceId_State ON dbo.DeviceAttendanceErases(DeviceId,State);
CREATE UNIQUE INDEX IX_DeviceAttendanceErases_RecoveryOfId ON dbo.DeviceAttendanceErases(RecoveryOfId) WHERE State=N'released';
CREATE UNIQUE INDEX UX_DeviceAttendanceErases_Device_Active ON dbo.DeviceAttendanceErases(DeviceId) WHERE State IN(N'proposed',N'pending',N'running');
EXEC(N'CREATE TRIGGER dbo.TR_DeviceAttendanceErases_History ON dbo.DeviceAttendanceErases AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL) THROW 51043,''Attendance erase/recovery history cannot be deleted.'',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND i.State<>N''proposed'') THROW 51043,''Erase starts proposed; independent second transaction required.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE d.State NOT IN(N''proposed'',N''pending'',N''running'') OR EXISTS(SELECT d.DeviceId,d.ArchiveId,d.RecoveryOfId,d.DeviceVersion,d.ArchiveVersion,d.RequestedByUserId,d.RequestedAtUtc,d.QuiescedAtUtc,CONVERT(varbinary(max),d.EvidenceReference),CONVERT(varbinary(max),d.ApprovalJson),CONVERT(varbinary(max),d.ArchiveDigest),d.ArchiveCount,CONVERT(varbinary(max),d.RequestHash) EXCEPT SELECT i.DeviceId,i.ArchiveId,i.RecoveryOfId,i.DeviceVersion,i.ArchiveVersion,i.RequestedByUserId,i.RequestedAtUtc,i.QuiescedAtUtc,CONVERT(varbinary(max),i.EvidenceReference),CONVERT(varbinary(max),i.ApprovalJson),CONVERT(varbinary(max),i.ArchiveDigest),i.ArchiveCount,CONVERT(varbinary(max),i.RequestHash)) OR (d.State=N''proposed'' AND i.State NOT IN(N''pending'',N''cancelled'',N''expired'',N''released'')) OR (d.State=N''pending'' AND i.State NOT IN(N''running'',N''cancelled'',N''revoked'')) OR (d.State=N''running'' AND i.State NOT IN(N''running'',N''deleted-verified'',N''already-empty'',N''archive-mismatch'',N''failed-before-write'',N''unknown'',N''unsupported'')) OR (d.State<>N''proposed'' AND EXISTS(SELECT d.ApprovedByUserId,d.ApprovedAtUtc,CONVERT(varbinary(max),d.ReviewReference) EXCEPT SELECT i.ApprovedByUserId,i.ApprovedAtUtc,CONVERT(varbinary(max),i.ReviewReference))) OR (d.State=N''running'' AND EXISTS(SELECT d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc EXCEPT SELECT i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc)) OR (d.CancelRequestedAtUtc IS NOT NULL AND EXISTS(SELECT d.CancelRequestedAtUtc EXCEPT SELECT i.CancelRequestedAtUtc))) THROW 51043,''Intent immutable, one-way two-person review, terminal result final; no replay.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceAttendanceDownloads a ON a.Id=i.ArchiveId JOIN dbo.Devices d ON d.Id=i.DeviceId WHERE a.DeviceId<>i.DeviceId OR a.State<>N''succeeded'' OR a.ExpectedDeviceVersion<>i.DeviceVersion OR a.ReceivedCount<>i.ArchiveCount OR CONVERT(varbinary(max),a.ReadFormat)<>CONVERT(varbinary(max),JSON_VALUE(i.ApprovalJson,''$.ReadFormat'')) OR a.RequestedAtUtc<i.QuiescedAtUtc OR a.FinishedAtUtc>i.RequestedAtUtc) THROW 51043,''Complete same-device durable archive after capture quiescence required.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceAttendanceErases p ON p.Id=i.RecoveryOfId WHERE p.State<>N''unknown'' OR p.RecoveryOfId IS NOT NULL OR p.DeviceId<>i.DeviceId OR p.DeviceVersion<>i.DeviceVersion OR p.FinishedAtUtc>i.QuiescedAtUtc) THROW 51043,''Recovery pins original unknown and post-reset observation; never original success.'',1;
END');
""");
 protected override void Down(MigrationBuilder m)=>m.Sql("IF EXISTS(SELECT 1 FROM dbo.DeviceAttendanceErases) THROW 51043,'Attendance erase/recovery history prevents downgrade.',1;DROP TABLE dbo.DeviceAttendanceErases;");
}
