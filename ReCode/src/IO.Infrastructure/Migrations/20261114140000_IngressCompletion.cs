using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261114140000_IngressCompletion")]
public sealed class IngressCompletion:Migration
{
 protected override void Up(MigrationBuilder m)=>m.Sql("""
CREATE TABLE dbo.DevicePushCommits(
 Id uniqueidentifier NOT NULL CONSTRAINT PK_DevicePushCommits PRIMARY KEY,JournalId uniqueidentifier NOT NULL,DeviceId uniqueidentifier NOT NULL,LineNumber int NOT NULL,
 MappingId uniqueidentifier NOT NULL,EmployeeId uniqueidentifier NOT NULL,RawEventId uniqueidentifier NOT NULL,ReviewedByUserId uniqueidentifier NOT NULL,ReviewedAtUtc datetimeoffset NOT NULL,
 EventType nvarchar(3) NOT NULL,TrustEvidence nvarchar(200) NOT NULL,DuplicateReviewEvidence nvarchar(200) NOT NULL,RequestHash nvarchar(64) NOT NULL,PreviewHash nvarchar(64) NOT NULL,
 CONSTRAINT FK_DevicePushCommits_DevicePushJournal_JournalId FOREIGN KEY(JournalId) REFERENCES dbo.DevicePushJournal(Id),
 CONSTRAINT FK_DevicePushCommits_Devices_DeviceId FOREIGN KEY(DeviceId) REFERENCES dbo.Devices(Id),
 CONSTRAINT FK_DevicePushCommits_SourceEmployeeMappings_MappingId FOREIGN KEY(MappingId) REFERENCES dbo.SourceEmployeeMappings(Id),
 CONSTRAINT FK_DevicePushCommits_Employees_EmployeeId FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(Id),
 CONSTRAINT FK_DevicePushCommits_RawAttendanceEvents_RawEventId FOREIGN KEY(RawEventId) REFERENCES dbo.RawAttendanceEvents(Id),
 CONSTRAINT FK_DevicePushCommits_Users_ReviewedByUserId FOREIGN KEY(ReviewedByUserId) REFERENCES dbo.Users(Id),
 CONSTRAINT CK_DevicePushCommits_Bounds CHECK(LineNumber BETWEEN 1 AND 10000 AND EventType COLLATE Latin1_General_100_BIN2 IN(N'in',N'out') AND DATALENGTH(TrustEvidence) BETWEEN 2 AND 400 AND DATALENGTH(DuplicateReviewEvidence) BETWEEN 2 AND 400 AND DATALENGTH(RequestHash)=128 AND DATALENGTH(PreviewHash)=128 AND RequestHash COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND PreviewHash COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%' AND DATEPART(TZOFFSET,ReviewedAtUtc)=0)
);
CREATE UNIQUE INDEX IX_DevicePushCommits_JournalId_LineNumber ON dbo.DevicePushCommits(JournalId,LineNumber);
CREATE UNIQUE INDEX IX_DevicePushCommits_RawEventId ON dbo.DevicePushCommits(RawEventId);
CREATE INDEX IX_DevicePushCommits_DeviceId ON dbo.DevicePushCommits(DeviceId);
CREATE INDEX IX_DevicePushCommits_MappingId ON dbo.DevicePushCommits(MappingId);
CREATE INDEX IX_DevicePushCommits_EmployeeId ON dbo.DevicePushCommits(EmployeeId);
CREATE INDEX IX_DevicePushCommits_ReviewedByUserId ON dbo.DevicePushCommits(ReviewedByUserId);
EXEC(N'CREATE TRIGGER dbo.TR_DevicePushCommits_Immutable ON dbo.DevicePushCommits AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted) THROW 51039,''PUSH acceptance is immutable.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DevicePushJournal j ON j.Id=i.JournalId JOIN dbo.SourceEmployeeMappings m ON m.Id=i.MappingId JOIN dbo.RawAttendanceEvents r ON r.Id=i.RawEventId WHERE NOT EXISTS(SELECT 1 FROM dbo.PushGatewayReceipts g WHERE g.Id=j.Id) OR i.DeviceId<>j.DeviceId OR i.LineNumber>j.RowCount OR m.EmployeeId<>i.EmployeeId OR m.SourceId<>j.SourceId OR m.CompanyId<>j.CompanyId OR r.SourceId<>j.SourceId OR r.ReportedEventType COLLATE Latin1_General_100_BIN2<>i.EventType COLLATE Latin1_General_100_BIN2) THROW 51039,''PUSH commitment must pin gateway evidence, exact mapping and raw event.'',1;
END');
CREATE TABLE dbo.RemoteEnrollmentRecoveries(
 Id uniqueidentifier NOT NULL CONSTRAINT PK_RemoteEnrollmentRecoveries PRIMARY KEY,EnrollmentId uniqueidentifier NOT NULL,DeviceId uniqueidentifier NOT NULL,ProposedByUserId uniqueidentifier NOT NULL,ProposedAtUtc datetimeoffset NOT NULL,PhysicalResetAtUtc datetimeoffset NOT NULL,DeviceVersion varbinary(8) NOT NULL,
 ApprovalJson nvarchar(max) NOT NULL,ProcessStoppedEvidence nvarchar(200) NOT NULL,PhysicalResetEvidence nvarchar(200) NOT NULL,SlotObservationEvidence nvarchar(200) NOT NULL,ObservedSlot nvarchar(7) NOT NULL,
 ApprovedByUserId uniqueidentifier NULL,ApprovedAtUtc datetimeoffset NULL,ReviewEvidence nvarchar(200) NULL,Version rowversion NOT NULL,
 CONSTRAINT FK_RemoteEnrollmentRecoveries_RemoteEnrollments_EnrollmentId FOREIGN KEY(EnrollmentId) REFERENCES dbo.RemoteEnrollments(Id),
 CONSTRAINT FK_RemoteEnrollmentRecoveries_Devices_DeviceId FOREIGN KEY(DeviceId) REFERENCES dbo.Devices(Id),
 CONSTRAINT FK_RemoteEnrollmentRecoveries_Users_ProposedByUserId FOREIGN KEY(ProposedByUserId) REFERENCES dbo.Users(Id),
 CONSTRAINT FK_RemoteEnrollmentRecoveries_Users_ApprovedByUserId FOREIGN KEY(ApprovedByUserId) REFERENCES dbo.Users(Id),
 CONSTRAINT CK_RemoteEnrollmentRecoveries_Evidence CHECK(ISJSON(ApprovalJson)=1 AND DATALENGTH(ApprovalJson)<=32768 AND DATALENGTH(DeviceVersion)=8 AND DATALENGTH(ProcessStoppedEvidence) BETWEEN 2 AND 400 AND DATALENGTH(PhysicalResetEvidence) BETWEEN 2 AND 400 AND DATALENGTH(SlotObservationEvidence) BETWEEN 2 AND 400 AND ObservedSlot COLLATE Latin1_General_100_BIN2 IN(N'present',N'absent') AND DATEPART(TZOFFSET,ProposedAtUtc)=0 AND DATEPART(TZOFFSET,PhysicalResetAtUtc)=0 AND PhysicalResetAtUtc<=ProposedAtUtc),
 CONSTRAINT CK_RemoteEnrollmentRecoveries_Review CHECK((ApprovedByUserId IS NULL AND ApprovedAtUtc IS NULL AND ReviewEvidence IS NULL) OR (ApprovedByUserId IS NOT NULL AND ApprovedAtUtc IS NOT NULL AND ReviewEvidence IS NOT NULL AND ApprovedByUserId<>ProposedByUserId AND ApprovedAtUtc>=ProposedAtUtc AND DATEPART(TZOFFSET,ApprovedAtUtc)=0 AND DATALENGTH(ReviewEvidence) BETWEEN 2 AND 400))
);
CREATE UNIQUE INDEX IX_RemoteEnrollmentRecoveries_EnrollmentId ON dbo.RemoteEnrollmentRecoveries(EnrollmentId) WHERE ApprovedAtUtc IS NOT NULL;
CREATE INDEX IX_RemoteEnrollmentRecoveries_DeviceId ON dbo.RemoteEnrollmentRecoveries(DeviceId);
CREATE INDEX IX_RemoteEnrollmentRecoveries_ProposedByUserId ON dbo.RemoteEnrollmentRecoveries(ProposedByUserId);
CREATE INDEX IX_RemoteEnrollmentRecoveries_ApprovedByUserId ON dbo.RemoteEnrollmentRecoveries(ApprovedByUserId);
EXEC(N'CREATE TRIGGER dbo.TR_RemoteEnrollmentRecoveries_History ON dbo.RemoteEnrollmentRecoveries AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL) THROW 51040,''Recovery evidence cannot be deleted.'',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND i.ApprovedAtUtc IS NOT NULL) THROW 51040,''Recovery requires independent second transaction.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE d.ApprovedAtUtc IS NOT NULL OR i.ApprovedAtUtc IS NULL OR EXISTS(SELECT d.EnrollmentId,d.DeviceId,d.ProposedByUserId,d.ProposedAtUtc,d.PhysicalResetAtUtc,d.DeviceVersion,d.ApprovalJson COLLATE Latin1_General_100_BIN2,DATALENGTH(d.ApprovalJson),d.ProcessStoppedEvidence COLLATE Latin1_General_100_BIN2,DATALENGTH(d.ProcessStoppedEvidence),d.PhysicalResetEvidence COLLATE Latin1_General_100_BIN2,DATALENGTH(d.PhysicalResetEvidence),d.SlotObservationEvidence COLLATE Latin1_General_100_BIN2,DATALENGTH(d.SlotObservationEvidence),d.ObservedSlot COLLATE Latin1_General_100_BIN2,DATALENGTH(d.ObservedSlot) EXCEPT SELECT i.EnrollmentId,i.DeviceId,i.ProposedByUserId,i.ProposedAtUtc,i.PhysicalResetAtUtc,i.DeviceVersion,i.ApprovalJson COLLATE Latin1_General_100_BIN2,DATALENGTH(i.ApprovalJson),i.ProcessStoppedEvidence COLLATE Latin1_General_100_BIN2,DATALENGTH(i.ProcessStoppedEvidence),i.PhysicalResetEvidence COLLATE Latin1_General_100_BIN2,DATALENGTH(i.PhysicalResetEvidence),i.SlotObservationEvidence COLLATE Latin1_General_100_BIN2,DATALENGTH(i.SlotObservationEvidence),i.ObservedSlot COLLATE Latin1_General_100_BIN2,DATALENGTH(i.ObservedSlot))) THROW 51040,''Only one-way independent countersignature is allowed.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.RemoteEnrollments e ON e.Id=i.EnrollmentId JOIN dbo.Devices d ON d.Id=i.DeviceId WHERE e.State<>N''unknown'' OR e.DeviceId<>i.DeviceId OR e.ExpectedDeviceVersion<>i.DeviceVersion OR d.Version<>i.DeviceVersion OR i.PhysicalResetAtUtc<e.FinishedAtUtc) THROW 51040,''Recovery requires unchanged unknown job and later physical reset.'',1;
END');
""");
 protected override void Down(MigrationBuilder m)=>m.Sql("""
 IF EXISTS(SELECT 1 FROM dbo.DevicePushCommits) OR EXISTS(SELECT 1 FROM dbo.RemoteEnrollmentRecoveries) THROW 51040,'Immutable evidence prevents downgrade.',1;
 DROP TABLE dbo.RemoteEnrollmentRecoveries;DROP TABLE dbo.DevicePushCommits;
 """);
}
