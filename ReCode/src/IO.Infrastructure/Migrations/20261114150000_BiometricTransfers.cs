using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261114150000_BiometricTransfers")]
public sealed class BiometricTransfers:Migration
{
 protected override void Up(MigrationBuilder m)=>m.Sql("""
CREATE TABLE dbo.DeviceBiometricTransfers(
 Id uniqueidentifier NOT NULL CONSTRAINT PK_DeviceBiometricTransfers PRIMARY KEY,SourceDeviceId uniqueidentifier NOT NULL,TargetDeviceId uniqueidentifier NOT NULL,EmployeeId uniqueidentifier NOT NULL,RequestedByUserId uniqueidentifier NOT NULL,RequestedAtUtc datetimeoffset NOT NULL,
 SourceDeviceVersion varbinary(8) NOT NULL,TargetDeviceVersion varbinary(8) NOT NULL,SourcePerson nvarchar(100) NOT NULL,TargetPerson nvarchar(100) NOT NULL,ApprovalJson nvarchar(max) NOT NULL,RequestHash nvarchar(64) NOT NULL,
 State nvarchar(40) NOT NULL,TimeoutSeconds int NOT NULL,ServiceUserId uniqueidentifier NULL,ClaimId uniqueidentifier NULL,ClaimedAtUtc datetimeoffset NULL,CancelRequestedAtUtc datetimeoffset NULL,FinishedAtUtc datetimeoffset NULL,Version rowversion NOT NULL,
 CONSTRAINT FK_DeviceBiometricTransfers_Devices_SourceDeviceId FOREIGN KEY(SourceDeviceId) REFERENCES dbo.Devices(Id),
 CONSTRAINT FK_DeviceBiometricTransfers_Devices_TargetDeviceId FOREIGN KEY(TargetDeviceId) REFERENCES dbo.Devices(Id),
 CONSTRAINT FK_DeviceBiometricTransfers_Employees_EmployeeId FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(Id),
 CONSTRAINT FK_DeviceBiometricTransfers_Users_RequestedByUserId FOREIGN KEY(RequestedByUserId) REFERENCES dbo.Users(Id),
 CONSTRAINT FK_DeviceBiometricTransfers_Users_ServiceUserId FOREIGN KEY(ServiceUserId) REFERENCES dbo.Users(Id),
 CONSTRAINT CK_DeviceBiometricTransfers_Intent CHECK(SourceDeviceId<>TargetDeviceId AND DATALENGTH(SourceDeviceVersion)=8 AND DATALENGTH(TargetDeviceVersion)=8 AND DATALENGTH(SourcePerson) BETWEEN 2 AND 200 AND DATALENGTH(TargetPerson) BETWEEN 2 AND 200 AND SourcePerson COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%' AND TargetPerson COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%' AND TimeoutSeconds BETWEEN 1 AND 60 AND ISJSON(ApprovalJson)=1 AND DATALENGTH(ApprovalJson)<=16384 AND DATALENGTH(RequestHash)=128 AND RequestHash COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND DATEPART(TZOFFSET,RequestedAtUtc)=0),
 CONSTRAINT CK_DeviceBiometricTransfers_Policy CHECK(JSON_VALUE(ApprovalJson,'$.AllowTransfer') IS NOT NULL AND JSON_VALUE(ApprovalJson,'$.AllowTransfer')=N'true' AND JSON_VALUE(ApprovalJson,'$.Kind') IS NOT NULL AND JSON_VALUE(ApprovalJson,'$.Kind') COLLATE Latin1_General_100_BIN2 IN(N'fingerprint',N'face') AND JSON_VALUE(ApprovalJson,'$.AuthorizationReference') IS NOT NULL AND JSON_VALUE(ApprovalJson,'$.RetentionReference') IS NOT NULL AND JSON_VALUE(ApprovalJson,'$.ExclusiveControlReference') IS NOT NULL),
 CONSTRAINT CK_DeviceBiometricTransfers_State CHECK(State COLLATE Latin1_General_100_BIN2 IN(N'pending',N'running',N'cancelled-before-dispatch',N'revoked-before-dispatch',N'copied',N'unchanged',N'existing-slot-refused',N'source-empty',N'source-changed',N'presence-unavailable',N'identity-mismatch',N'user-unavailable',N'failed-before-write',N'unknown',N'unsupported') AND ((State=N'pending' AND ServiceUserId IS NULL AND ClaimId IS NULL AND ClaimedAtUtc IS NULL AND CancelRequestedAtUtc IS NULL AND FinishedAtUtc IS NULL) OR (State=N'running' AND ServiceUserId IS NOT NULL AND ClaimId IS NOT NULL AND ClaimedAtUtc IS NOT NULL AND FinishedAtUtc IS NULL AND ClaimedAtUtc>=RequestedAtUtc) OR (State NOT IN(N'pending',N'running') AND FinishedAtUtc IS NOT NULL AND FinishedAtUtc>=RequestedAtUtc))),
 CONSTRAINT CK_DeviceBiometricTransfers_Time CHECK(DATEPART(TZOFFSET,ClaimedAtUtc)=0 AND DATEPART(TZOFFSET,FinishedAtUtc)=0 AND DATEPART(TZOFFSET,CancelRequestedAtUtc)=0 AND (CancelRequestedAtUtc IS NULL OR (ClaimId IS NOT NULL AND CancelRequestedAtUtc>=ClaimedAtUtc)) AND (State NOT IN(N'copied',N'unchanged') OR (ClaimId IS NOT NULL AND CancelRequestedAtUtc IS NULL)))
);
CREATE INDEX IX_DeviceBiometricTransfers_SourceDeviceId_State ON dbo.DeviceBiometricTransfers(SourceDeviceId,State);
CREATE INDEX IX_DeviceBiometricTransfers_TargetDeviceId_State ON dbo.DeviceBiometricTransfers(TargetDeviceId,State);
CREATE INDEX IX_DeviceBiometricTransfers_EmployeeId ON dbo.DeviceBiometricTransfers(EmployeeId);
CREATE INDEX IX_DeviceBiometricTransfers_RequestedByUserId ON dbo.DeviceBiometricTransfers(RequestedByUserId);
CREATE INDEX IX_DeviceBiometricTransfers_ServiceUserId ON dbo.DeviceBiometricTransfers(ServiceUserId);
EXEC(N'CREATE TRIGGER dbo.TR_DeviceBiometricTransfers_History ON dbo.DeviceBiometricTransfers AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL) THROW 51041,''Biometric intent/result history cannot be deleted.'',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND i.State<>N''pending'') THROW 51041,''Biometric job must begin pending.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE d.State NOT IN(N''pending'',N''running'') OR EXISTS(SELECT d.SourceDeviceId,d.TargetDeviceId,d.EmployeeId,d.RequestedByUserId,d.RequestedAtUtc,d.SourceDeviceVersion,d.TargetDeviceVersion,d.SourcePerson COLLATE Latin1_General_100_BIN2,DATALENGTH(d.SourcePerson),d.TargetPerson COLLATE Latin1_General_100_BIN2,DATALENGTH(d.TargetPerson),d.ApprovalJson COLLATE Latin1_General_100_BIN2,DATALENGTH(d.ApprovalJson),d.RequestHash COLLATE Latin1_General_100_BIN2,DATALENGTH(d.RequestHash),d.TimeoutSeconds EXCEPT SELECT i.SourceDeviceId,i.TargetDeviceId,i.EmployeeId,i.RequestedByUserId,i.RequestedAtUtc,i.SourceDeviceVersion,i.TargetDeviceVersion,i.SourcePerson COLLATE Latin1_General_100_BIN2,DATALENGTH(i.SourcePerson),i.TargetPerson COLLATE Latin1_General_100_BIN2,DATALENGTH(i.TargetPerson),i.ApprovalJson COLLATE Latin1_General_100_BIN2,DATALENGTH(i.ApprovalJson),i.RequestHash COLLATE Latin1_General_100_BIN2,DATALENGTH(i.RequestHash),i.TimeoutSeconds) OR (d.State=N''pending'' AND i.State NOT IN(N''pending'',N''running'',N''cancelled-before-dispatch'',N''revoked-before-dispatch'')) OR (d.State=N''running'' AND (i.State IN(N''pending'',N''revoked-before-dispatch'',N''cancelled-before-dispatch'') OR EXISTS(SELECT d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc EXCEPT SELECT i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc))) OR (d.CancelRequestedAtUtc IS NOT NULL AND EXISTS(SELECT d.CancelRequestedAtUtc EXCEPT SELECT i.CancelRequestedAtUtc))) THROW 51041,''Biometric intent immutable, terminal state final, no replay.'',1;
END');
""");
 protected override void Down(MigrationBuilder m)=>m.Sql("""
 IF EXISTS(SELECT 1 FROM dbo.DeviceBiometricTransfers) THROW 51041,'Biometric history prevents downgrade.',1;
 DROP TABLE dbo.DeviceBiometricTransfers;
 """);
}
