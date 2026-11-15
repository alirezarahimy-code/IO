using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261114190000_ProtectedErases")]
public sealed class ProtectedErases:Migration
{
 protected override void Up(MigrationBuilder m)=>m.Sql("""
CREATE TABLE dbo.DeviceProtectedErases(
Id uniqueidentifier NOT NULL CONSTRAINT PK_DeviceProtectedErases PRIMARY KEY,
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
Person nvarchar(40) NULL,
ExpectedJson nvarchar(max) NULL,
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
CONSTRAINT FK_DeviceProtectedErases_Devices_DeviceId FOREIGN KEY(DeviceId) REFERENCES dbo.Devices(Id),
CONSTRAINT FK_DeviceProtectedErases_DeviceUserDownloads_ArchiveId FOREIGN KEY(ArchiveId) REFERENCES dbo.DeviceUserDownloads(Id),
CONSTRAINT FK_DeviceProtectedErases_DeviceProtectedErases_RecoveryOfId FOREIGN KEY(RecoveryOfId) REFERENCES dbo.DeviceProtectedErases(Id),
CONSTRAINT FK_DeviceProtectedErases_Users_RequestedByUserId FOREIGN KEY(RequestedByUserId) REFERENCES dbo.Users(Id),
CONSTRAINT FK_DeviceProtectedErases_Users_ApprovedByUserId FOREIGN KEY(ApprovedByUserId) REFERENCES dbo.Users(Id),
CONSTRAINT FK_DeviceProtectedErases_Users_ServiceUserId FOREIGN KEY(ServiceUserId) REFERENCES dbo.Users(Id),
CONSTRAINT CK_DeviceProtectedErases_Request CHECK(DATALENGTH(DeviceVersion)=8 AND DATALENGTH(ArchiveVersion)=8 AND ArchiveCount BETWEEN 0 AND 1000 AND DATALENGTH(ArchiveDigest)=128 AND ArchiveDigest COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND DATALENGTH(RequestHash)=128 AND RequestHash COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND ISJSON(ApprovalJson)=1 AND DATALENGTH(ApprovalJson)<=16384 AND DATALENGTH(EvidenceReference) BETWEEN 2 AND 400 AND QuiescedAtUtc<=RequestedAtUtc AND (RecoveryOfId IS NULL OR RecoveryOfId<>Id)),
CONSTRAINT CK_DeviceProtectedErases_Family CHECK(Family COLLATE Latin1_General_100_BIN2 IN(N'users',N'fingerprints',N'administrators') AND ((Family=N'administrators' AND Person IS NULL AND ExpectedJson IS NULL) OR (Family<>N'administrators' AND Person IS NOT NULL AND DATALENGTH(Person) BETWEEN 2 AND 80 AND Person COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%' AND ((ExpectedJson IS NOT NULL AND ISJSON(ExpectedJson)=1 AND DATALENGTH(ExpectedJson)<=4096) OR RecoveryOfId IS NOT NULL)))),
CONSTRAINT CK_DeviceProtectedErases_State CHECK(State COLLATE Latin1_General_100_BIN2 IN(N'proposed',N'pending',N'running',N'observed',N'released',N'cancelled',N'expired',N'revoked',N'user-deleted-verified',N'fingerprints-deleted-verified',N'administrators-cleared-verified',N'already-no-administrators',N'already-absent',N'already-empty',N'catalogue-mismatch',N'failed-before-write',N'unknown',N'unsupported',N'inspection-failed') AND (State NOT IN(N'observed',N'released',N'inspection-failed') OR RecoveryOfId IS NOT NULL) AND (RecoveryOfId IS NULL OR State IN(N'pending',N'running',N'observed',N'released',N'cancelled',N'expired',N'revoked',N'inspection-failed')) AND (State<>N'user-deleted-verified' OR Family=N'users') AND (State NOT IN(N'fingerprints-deleted-verified',N'already-empty') OR Family=N'fingerprints') AND (State NOT IN(N'administrators-cleared-verified',N'already-no-administrators') OR Family=N'administrators') AND (State<>N'already-absent' OR Family<>N'administrators')),
CONSTRAINT CK_DeviceProtectedErases_Review CHECK((ApprovedByUserId IS NULL AND ApprovedAtUtc IS NULL AND ReviewReference IS NULL AND ((RecoveryOfId IS NULL AND State IN(N'proposed',N'cancelled',N'expired')) OR (RecoveryOfId IS NOT NULL AND State<>N'released'))) OR (ApprovedByUserId IS NOT NULL AND ApprovedAtUtc IS NOT NULL AND DATALENGTH(ReviewReference) BETWEEN 2 AND 400 AND ReviewReference IS NOT NULL AND ApprovedByUserId<>RequestedByUserId AND ApprovedAtUtc>=RequestedAtUtc AND ((RecoveryOfId IS NULL AND State NOT IN(N'proposed',N'expired')) OR (RecoveryOfId IS NOT NULL AND State=N'released')))),
CONSTRAINT CK_DeviceProtectedErases_Lifecycle CHECK((State IN(N'proposed',N'pending') AND ServiceUserId IS NULL AND ClaimId IS NULL AND ClaimedAtUtc IS NULL AND CancelRequestedAtUtc IS NULL AND FinishedAtUtc IS NULL) OR (State=N'running' AND ServiceUserId IS NOT NULL AND ClaimId IS NOT NULL AND ClaimedAtUtc IS NOT NULL AND FinishedAtUtc IS NULL) OR (State IN(N'cancelled',N'expired',N'revoked') AND FinishedAtUtc IS NOT NULL AND ServiceUserId IS NULL AND ClaimId IS NULL AND ClaimedAtUtc IS NULL AND CancelRequestedAtUtc IS NULL) OR (State NOT IN(N'proposed',N'pending',N'running',N'cancelled',N'expired',N'revoked') AND FinishedAtUtc IS NOT NULL AND ServiceUserId IS NOT NULL AND ClaimId IS NOT NULL AND ClaimedAtUtc IS NOT NULL)),
CONSTRAINT CK_DeviceProtectedErases_Time CHECK(DATEPART(TZOFFSET,RequestedAtUtc)=0 AND DATEPART(TZOFFSET,QuiescedAtUtc)=0 AND DATEPART(TZOFFSET,ApprovedAtUtc)=0 AND DATEPART(TZOFFSET,ClaimedAtUtc)=0 AND DATEPART(TZOFFSET,FinishedAtUtc)=0 AND DATEPART(TZOFFSET,CancelRequestedAtUtc)=0 AND (FinishedAtUtc IS NULL OR FinishedAtUtc>=COALESCE(ClaimedAtUtc,RequestedAtUtc)) AND (ClaimedAtUtc IS NULL OR ClaimedAtUtc>=RequestedAtUtc) AND (CancelRequestedAtUtc IS NULL OR CancelRequestedAtUtc>=ClaimedAtUtc) AND (State NOT IN(N'user-deleted-verified',N'fingerprints-deleted-verified',N'administrators-cleared-verified',N'already-empty',N'already-absent',N'already-no-administrators',N'observed',N'released') OR CancelRequestedAtUtc IS NULL))
);
CREATE INDEX IX_DeviceProtectedErases_ArchiveId ON dbo.DeviceProtectedErases(ArchiveId);
CREATE INDEX IX_DeviceProtectedErases_RequestedByUserId ON dbo.DeviceProtectedErases(RequestedByUserId);
CREATE INDEX IX_DeviceProtectedErases_ApprovedByUserId ON dbo.DeviceProtectedErases(ApprovedByUserId);
CREATE INDEX IX_DeviceProtectedErases_ServiceUserId ON dbo.DeviceProtectedErases(ServiceUserId);
CREATE INDEX IX_DeviceProtectedErases_DeviceId_State ON dbo.DeviceProtectedErases(DeviceId,State);
CREATE UNIQUE INDEX IX_DeviceProtectedErases_RecoveryOfId ON dbo.DeviceProtectedErases(RecoveryOfId) WHERE State=N'released';
CREATE UNIQUE INDEX UX_DeviceProtectedErases_Device_Active ON dbo.DeviceProtectedErases(DeviceId) WHERE State IN(N'proposed',N'pending',N'running',N'observed');
EXEC(N'CREATE TRIGGER dbo.TR_DeviceProtectedErases_History ON dbo.DeviceProtectedErases AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL) THROW 51044,''Protected erase history cannot be deleted.'',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND ((i.RecoveryOfId IS NULL AND i.State<>N''proposed'') OR (i.RecoveryOfId IS NOT NULL AND i.State<>N''pending''))) THROW 51044,''Separate destructive review or read-only recovery inspection required.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE d.State NOT IN(N''proposed'',N''pending'',N''running'',N''observed'') OR EXISTS(SELECT d.DeviceId,d.ArchiveId,d.RecoveryOfId,d.DeviceVersion,d.ArchiveVersion,d.RequestedByUserId,d.RequestedAtUtc,d.QuiescedAtUtc,CONVERT(varbinary(max),d.EvidenceReference),CONVERT(varbinary(max),d.ApprovalJson),CONVERT(varbinary(max),d.ArchiveDigest),d.ArchiveCount,CONVERT(varbinary(max),d.RequestHash),CONVERT(varbinary(max),d.Family),CONVERT(varbinary(max),d.Person),CONVERT(varbinary(max),d.ExpectedJson) EXCEPT SELECT i.DeviceId,i.ArchiveId,i.RecoveryOfId,i.DeviceVersion,i.ArchiveVersion,i.RequestedByUserId,i.RequestedAtUtc,i.QuiescedAtUtc,CONVERT(varbinary(max),i.EvidenceReference),CONVERT(varbinary(max),i.ApprovalJson),CONVERT(varbinary(max),i.ArchiveDigest),i.ArchiveCount,CONVERT(varbinary(max),i.RequestHash),CONVERT(varbinary(max),i.Family),CONVERT(varbinary(max),i.Person),CONVERT(varbinary(max),i.ExpectedJson)) OR (d.State=N''proposed'' AND i.State NOT IN(N''pending'',N''cancelled'',N''expired'')) OR (d.State=N''pending'' AND i.State NOT IN(N''running'',N''cancelled'',N''expired'',N''revoked'')) OR (d.State=N''observed'' AND i.State<>N''released'') OR (d.State=N''running'' AND i.State IN(N''proposed'',N''pending'',N''released'',N''cancelled'',N''expired'',N''revoked'')) OR (d.ApprovedByUserId IS NOT NULL AND EXISTS(SELECT d.ApprovedByUserId,d.ApprovedAtUtc,CONVERT(varbinary(max),d.ReviewReference) EXCEPT SELECT i.ApprovedByUserId,i.ApprovedAtUtc,CONVERT(varbinary(max),i.ReviewReference))) OR (d.State IN(N''running'',N''observed'') AND EXISTS(SELECT d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc EXCEPT SELECT i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc)) OR (d.State=N''observed'' AND EXISTS(SELECT d.FinishedAtUtc EXCEPT SELECT i.FinishedAtUtc)) OR (d.CancelRequestedAtUtc IS NOT NULL AND EXISTS(SELECT d.CancelRequestedAtUtc EXCEPT SELECT i.CancelRequestedAtUtc))) THROW 51044,''Intent and terminal history immutable; no replay or retarget.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceUserDownloads a ON a.Id=i.ArchiveId WHERE a.DeviceId<>i.DeviceId OR a.State<>N''succeeded'' OR a.ExpectedDeviceVersion<>i.DeviceVersion OR a.ReceivedCount<>i.ArchiveCount OR CONVERT(varbinary(max),a.ReadFormat)<>CONVERT(varbinary(max),JSON_VALUE(i.ApprovalJson,''$.Inventory.UserFormat'')) OR a.RequestedAtUtc<i.QuiescedAtUtc OR a.FinishedAtUtc>i.RequestedAtUtc) THROW 51044,''Fresh complete same-device metadata archive after exclusive control required.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeviceProtectedErases p ON p.Id=i.RecoveryOfId WHERE p.State<>N''unknown'' OR p.RecoveryOfId IS NOT NULL OR p.DeviceId<>i.DeviceId OR p.DeviceVersion<>i.DeviceVersion OR p.Family<>i.Family OR EXISTS(SELECT CONVERT(varbinary(max),p.Person),CONVERT(varbinary(max),p.ApprovalJson) EXCEPT SELECT CONVERT(varbinary(max),i.Person),CONVERT(varbinary(max),i.ApprovalJson)) OR p.FinishedAtUtc>i.QuiescedAtUtc) THROW 51044,''Recovery pins exact original unknown family and subject; never original success.'',1;
END');
""");
 protected override void Down(MigrationBuilder m)=>m.Sql("IF EXISTS(SELECT 1 FROM dbo.DeviceProtectedErases) THROW 51044,'Protected erase history prevents downgrade.',1;DROP TABLE dbo.DeviceProtectedErases;");
}
