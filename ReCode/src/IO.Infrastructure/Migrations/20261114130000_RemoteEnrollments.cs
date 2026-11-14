using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261114130000_RemoteEnrollments")]
public sealed class RemoteEnrollments:Migration
{
 protected override void Up(MigrationBuilder m)=>m.Sql("""
 CREATE TABLE dbo.RemoteEnrollments(
 Id uniqueidentifier NOT NULL CONSTRAINT PK_RemoteEnrollments PRIMARY KEY,DeviceId uniqueidentifier NOT NULL,EmployeeId uniqueidentifier NOT NULL,SourceId uniqueidentifier NOT NULL,
 RequestedByUserId uniqueidentifier NOT NULL,RequestedAtUtc datetimeoffset NOT NULL,ExpectedDeviceVersion varbinary(8) NOT NULL,PersonIdentifier nvarchar(100) NOT NULL,ApprovalJson nvarchar(8192) NOT NULL,TimeoutSeconds int NOT NULL,
 State nvarchar(40) NOT NULL,ServiceUserId uniqueidentifier NULL,ClaimId uniqueidentifier NULL,ClaimedAtUtc datetimeoffset NULL,CancelRequestedAtUtc datetimeoffset NULL,FinishedAtUtc datetimeoffset NULL,ResultJson nvarchar(4096) NULL,Version rowversion NOT NULL,
 CONSTRAINT FK_RemoteEnrollments_Devices_DeviceId FOREIGN KEY(DeviceId) REFERENCES dbo.Devices(Id),
 CONSTRAINT FK_RemoteEnrollments_Employees_EmployeeId FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(Id),
 CONSTRAINT FK_RemoteEnrollments_EventSources_SourceId FOREIGN KEY(SourceId) REFERENCES dbo.EventSources(Id),
 CONSTRAINT FK_RemoteEnrollments_Users_RequestedByUserId FOREIGN KEY(RequestedByUserId) REFERENCES dbo.Users(Id),
 CONSTRAINT FK_RemoteEnrollments_Users_ServiceUserId FOREIGN KEY(ServiceUserId) REFERENCES dbo.Users(Id),
 CONSTRAINT CK_RemoteEnrollments_Request CHECK(DATALENGTH(ExpectedDeviceVersion)=8 AND TimeoutSeconds BETWEEN 10 AND 60 AND DATALENGTH(PersonIdentifier) BETWEEN 2 AND 200 AND PersonIdentifier COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%' AND ISJSON(ApprovalJson)=1 AND DATEPART(TZOFFSET,RequestedAtUtc)=0),
 CONSTRAINT CK_RemoteEnrollments_State CHECK(State COLLATE Latin1_General_100_BIN2 IN(N'pending',N'running',N'unknown',N'completed',N'unsupported',N'identity-mismatch',N'presence-unavailable',N'existing-slot-refused',N'failed-before-dispatch',N'cancelled-before-dispatch',N'blocked-policy-or-model',N'revoked',N'device-changed') AND ((State=N'pending' AND ClaimId IS NULL AND ServiceUserId IS NULL AND ClaimedAtUtc IS NULL AND FinishedAtUtc IS NULL AND CancelRequestedAtUtc IS NULL) OR (State=N'running' AND ClaimId IS NOT NULL AND ServiceUserId IS NOT NULL AND ClaimedAtUtc>=RequestedAtUtc AND FinishedAtUtc IS NULL) OR (State NOT IN(N'pending',N'running') AND FinishedAtUtc>=RequestedAtUtc))),
 CONSTRAINT CK_RemoteEnrollments_Result CHECK((State=N'completed' AND ResultJson IS NOT NULL AND ISJSON(ResultJson)=1 AND CancelRequestedAtUtc IS NULL AND ClaimId IS NOT NULL) OR (State<>N'completed' AND ResultJson IS NULL))
 );
 CREATE INDEX IX_RemoteEnrollments_DeviceId_State ON dbo.RemoteEnrollments(DeviceId,State);
 CREATE INDEX IX_RemoteEnrollments_EmployeeId ON dbo.RemoteEnrollments(EmployeeId);
 CREATE INDEX IX_RemoteEnrollments_SourceId ON dbo.RemoteEnrollments(SourceId);
 CREATE INDEX IX_RemoteEnrollments_RequestedByUserId ON dbo.RemoteEnrollments(RequestedByUserId);
 CREATE INDEX IX_RemoteEnrollments_ServiceUserId ON dbo.RemoteEnrollments(ServiceUserId);
 EXEC(N'CREATE TRIGGER dbo.TR_RemoteEnrollments_History ON dbo.RemoteEnrollments AFTER INSERT,UPDATE,DELETE AS
 BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL) THROW 51038,''Enrollment history cannot be deleted.'',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND i.State<>N''pending'') THROW 51038,''New enrollment must be pending.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE d.State NOT IN(N''pending'',N''running'') OR EXISTS(SELECT d.DeviceId,d.EmployeeId,d.SourceId,d.RequestedByUserId,d.RequestedAtUtc,d.ExpectedDeviceVersion,d.PersonIdentifier COLLATE Latin1_General_100_BIN2,DATALENGTH(d.PersonIdentifier),d.ApprovalJson COLLATE Latin1_General_100_BIN2,DATALENGTH(d.ApprovalJson),d.TimeoutSeconds EXCEPT SELECT i.DeviceId,i.EmployeeId,i.SourceId,i.RequestedByUserId,i.RequestedAtUtc,i.ExpectedDeviceVersion,i.PersonIdentifier COLLATE Latin1_General_100_BIN2,DATALENGTH(i.PersonIdentifier),i.ApprovalJson COLLATE Latin1_General_100_BIN2,DATALENGTH(i.ApprovalJson),i.TimeoutSeconds) OR (d.State=N''running'' AND (i.State IN(N''pending'',N''revoked'',N''device-changed'') OR EXISTS(SELECT d.ServiceUserId,d.ClaimId,d.ClaimedAtUtc EXCEPT SELECT i.ServiceUserId,i.ClaimId,i.ClaimedAtUtc))) OR (d.CancelRequestedAtUtc IS NOT NULL AND EXISTS(SELECT d.CancelRequestedAtUtc EXCEPT SELECT i.CancelRequestedAtUtc))) THROW 51038,''Enrollment is single-dispatch with immutable intent and terminal state.'',1;
 IF EXISTS(SELECT 1 FROM inserted i WHERE i.State=N''completed'' AND ((SELECT COUNT(*) FROM OPENJSON(i.ResultJson))<>5 OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ResultJson,''$.OperationId'')) IS NULL OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ResultJson,''$.OperationId''))<>i.Id OR JSON_VALUE(i.ResultJson,''$.PersonIdentifier'') IS NULL OR EXISTS(SELECT JSON_VALUE(i.ResultJson,''$.PersonIdentifier'') COLLATE Latin1_General_100_BIN2,DATALENGTH(JSON_VALUE(i.ResultJson,''$.PersonIdentifier'')) EXCEPT SELECT i.PersonIdentifier COLLATE Latin1_General_100_BIN2,DATALENGTH(i.PersonIdentifier)) OR TRY_CONVERT(int,JSON_VALUE(i.ResultJson,''$.FingerIndex'')) IS NULL OR TRY_CONVERT(int,JSON_VALUE(i.ResultJson,''$.FingerIndex''))<>TRY_CONVERT(int,JSON_VALUE(i.ApprovalJson,''$.FingerIndex'')) OR TRY_CONVERT(int,JSON_VALUE(i.ResultJson,''$.ActionCode'')) IS NULL OR TRY_CONVERT(int,JSON_VALUE(i.ResultJson,''$.ActionCode''))<>TRY_CONVERT(int,JSON_VALUE(i.ApprovalJson,''$.SuccessActionCode'')) OR TRY_CONVERT(int,JSON_VALUE(i.ResultJson,''$.TemplateLength'')) IS NULL OR TRY_CONVERT(int,JSON_VALUE(i.ResultJson,''$.TemplateLength'')) NOT BETWEEN 1 AND 1048576)) THROW 51038,''Completion needs exact correlated observation, never template bytes or a bare ACK.'',1;
 END');
 """);
 protected override void Down(MigrationBuilder m)=>m.Sql("""
 IF EXISTS(SELECT 1 FROM dbo.RemoteEnrollments) THROW 51038,'Enrollment history prevents downgrade.',1;
 DROP TABLE dbo.RemoteEnrollments;
 """);
}
