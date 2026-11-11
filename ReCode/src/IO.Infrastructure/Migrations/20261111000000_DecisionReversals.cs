using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IO.Infrastructure.Migrations;
[DbContext(typeof(IoDbContext))]
[Migration("20261111000000_DecisionReversals")]
public sealed class DecisionReversals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        ALTER TABLE dbo.CorrectionDecisions ADD [Sequence] int NOT NULL CONSTRAINT DF_CorrectionDecisions_Sequence DEFAULT 1;
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.CorrectionDecisions ADD CONSTRAINT CK_CorrectionDecisions_Sequence CHECK ([Sequence] > 0);
        """);
        migrationBuilder.Sql("""
        DROP INDEX IX_CorrectionDecisions_RequestId ON dbo.CorrectionDecisions;
        """);
        migrationBuilder.Sql("""
        CREATE UNIQUE INDEX IX_CorrectionDecisions_RequestId_Sequence ON dbo.CorrectionDecisions(RequestId, [Sequence]);
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.LeaveDecisions ADD [Sequence] int NOT NULL CONSTRAINT DF_LeaveDecisions_Sequence DEFAULT 1;
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.LeaveDecisions ADD CONSTRAINT CK_LeaveDecisions_Sequence CHECK ([Sequence] > 0);
        """);
        migrationBuilder.Sql("""
        DROP INDEX IX_LeaveDecisions_RequestId ON dbo.LeaveDecisions;
        """);
        migrationBuilder.Sql("""
        CREATE UNIQUE INDEX IX_LeaveDecisions_RequestId_Sequence ON dbo.LeaveDecisions(RequestId, [Sequence]);
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.LeaveDecisions ADD LedgerEntryId uniqueidentifier NULL;
        """);
        migrationBuilder.Sql("""
        -- Only an exact, unambiguous old consumption is linked. Ambiguous legacy rows stay NULL
        -- and cannot be reversed until independently reconciled; never guess or post a refund.
        DISABLE TRIGGER dbo.TR_LeaveDecisions_Immutable ON dbo.LeaveDecisions;
        """);
        migrationBuilder.Sql("""
        EXEC(N'UPDATE d SET LedgerEntryId = l.Id FROM dbo.LeaveDecisions d
        JOIN dbo.LeaveRequests r ON r.Id=d.RequestId
        JOIN dbo.LeaveLedgerEntries l ON l.EmployeeId=r.EmployeeId AND l.CompanyId=r.CompanyId
        AND l.LeaveTypeId=r.LeaveTypeId AND l.EffectiveDate=r.WorkDate AND l.Kind=2 AND l.Minutes=-r.Minutes
        AND l.DocumentNumber=''LEAVE-''+LEFT(REPLACE(CONVERT(varchar(36),r.Id),''-'',''''),12)
        WHERE d.Decision=1 AND r.Category=0 AND
        (SELECT COUNT(*) FROM dbo.LeaveLedgerEntries x WHERE x.EmployeeId=r.EmployeeId AND x.LeaveTypeId=r.LeaveTypeId
        AND x.EffectiveDate=r.WorkDate AND x.Kind=2 AND x.Minutes=-r.Minutes
        AND x.DocumentNumber=''LEAVE-''+LEFT(REPLACE(CONVERT(varchar(36),r.Id),''-'',''''),12))=1
        AND NOT EXISTS(SELECT 1 FROM dbo.LeaveRequests r2 WHERE r2.Id<>r.Id AND r2.EmployeeId=r.EmployeeId
         AND r2.LeaveTypeId=r.LeaveTypeId AND r2.WorkDate=r.WorkDate AND r2.Minutes=r.Minutes AND r2.Category=0
         AND LEFT(REPLACE(CONVERT(varchar(36),r2.Id),''-'',''''),12)=LEFT(REPLACE(CONVERT(varchar(36),r.Id),''-'',''''),12));');
        """);
        migrationBuilder.Sql("""
        ENABLE TRIGGER dbo.TR_LeaveDecisions_Immutable ON dbo.LeaveDecisions;
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.LeaveDecisions ADD CONSTRAINT FK_LeaveDecisions_LeaveLedgerEntries_LedgerEntryId FOREIGN KEY(LedgerEntryId) REFERENCES dbo.LeaveLedgerEntries(Id);
        """);
        migrationBuilder.Sql("""
        CREATE UNIQUE INDEX IX_LeaveDecisions_LedgerEntryId ON dbo.LeaveDecisions(LedgerEntryId) WHERE LedgerEntryId IS NOT NULL;
        """);
        migrationBuilder.Sql("""
        CREATE TABLE dbo.CorrectionDecisionReversals (
         Id uniqueidentifier NOT NULL CONSTRAINT PK_CorrectionDecisionReversals PRIMARY KEY,
         DecisionId uniqueidentifier NOT NULL,
         ActorUserId uniqueidentifier NOT NULL,
         Reason nvarchar(500) NOT NULL,
         AtUtc datetimeoffset NOT NULL,

         CONSTRAINT CK_CorrectionDecisionReversals_Utc CHECK(DATEPART(TZOFFSET,AtUtc)=0),
         CONSTRAINT FK_CorrectionDecisionReversals_CorrectionDecisions_DecisionId FOREIGN KEY(DecisionId) REFERENCES dbo.CorrectionDecisions(Id),
         CONSTRAINT FK_CorrectionDecisionReversals_Users_ActorUserId FOREIGN KEY(ActorUserId) REFERENCES dbo.Users(Id)
        );
        """);
        migrationBuilder.Sql("""
        CREATE UNIQUE INDEX IX_CorrectionDecisionReversals_DecisionId ON dbo.CorrectionDecisionReversals(DecisionId);
        """);
        migrationBuilder.Sql("""
        CREATE INDEX IX_CorrectionDecisionReversals_ActorUserId ON dbo.CorrectionDecisionReversals(ActorUserId);
        """);
        migrationBuilder.Sql("""
        EXEC(N'CREATE TRIGGER dbo.TR_CorrectionDecisionReversals_Immutable ON dbo.CorrectionDecisionReversals
        AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted)
        THROW 51010, ''Decision reversals are immutable.'', 1; END');
        """);
        migrationBuilder.Sql("""
        EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_CorrectionDecisionReversals] ON [dbo].[CorrectionDecisionReversals]
        AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
        IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
        BEGIN
         UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
         IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
        END END');
        """);
        migrationBuilder.Sql("""
        CREATE TABLE dbo.LeaveDecisionReversals (
         Id uniqueidentifier NOT NULL CONSTRAINT PK_LeaveDecisionReversals PRIMARY KEY,
         DecisionId uniqueidentifier NOT NULL,
         ActorUserId uniqueidentifier NOT NULL,
         Reason nvarchar(500) NOT NULL,
         AtUtc datetimeoffset NOT NULL,
         OriginalLedgerEntryId uniqueidentifier NULL, CompensationLedgerEntryId uniqueidentifier NULL,
         CONSTRAINT CK_LeaveDecisionReversals_Utc CHECK(DATEPART(TZOFFSET,AtUtc)=0),
         CONSTRAINT FK_LeaveDecisionReversals_LeaveDecisions_DecisionId FOREIGN KEY(DecisionId) REFERENCES dbo.LeaveDecisions(Id),
         CONSTRAINT FK_LeaveDecisionReversals_Users_ActorUserId FOREIGN KEY(ActorUserId) REFERENCES dbo.Users(Id)
        );
        """);
        migrationBuilder.Sql("""
        CREATE UNIQUE INDEX IX_LeaveDecisionReversals_DecisionId ON dbo.LeaveDecisionReversals(DecisionId);
        """);
        migrationBuilder.Sql("""
        CREATE INDEX IX_LeaveDecisionReversals_ActorUserId ON dbo.LeaveDecisionReversals(ActorUserId);
        """);
        migrationBuilder.Sql("""
        EXEC(N'CREATE TRIGGER dbo.TR_LeaveDecisionReversals_Immutable ON dbo.LeaveDecisionReversals
        AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted)
        THROW 51010, ''Decision reversals are immutable.'', 1; END');
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.LeaveDecisionReversals ADD
         CONSTRAINT FK_LeaveDecisionReversals_LeaveLedgerEntries_OriginalLedgerEntryId FOREIGN KEY(OriginalLedgerEntryId) REFERENCES dbo.LeaveLedgerEntries(Id),
         CONSTRAINT FK_LeaveDecisionReversals_LeaveLedgerEntries_CompensationLedgerEntryId FOREIGN KEY(CompensationLedgerEntryId) REFERENCES dbo.LeaveLedgerEntries(Id);
        """);
        migrationBuilder.Sql("""
        CREATE INDEX IX_LeaveDecisionReversals_OriginalLedgerEntryId ON dbo.LeaveDecisionReversals(OriginalLedgerEntryId);
        """);
        migrationBuilder.Sql("""
        CREATE UNIQUE INDEX IX_LeaveDecisionReversals_CompensationLedgerEntryId ON dbo.LeaveDecisionReversals(CompensationLedgerEntryId) WHERE CompensationLedgerEntryId IS NOT NULL;
        """);
        migrationBuilder.Sql("""
        EXEC(N'CREATE OR ALTER TRIGGER [dbo].[TR_CorrectionRequests_ImmutablePayload] ON [dbo].[CorrectionRequests]
                        AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                        IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
                            THROW 51008, ''Correction requests cannot be deleted.'', 1;
                        IF NOT EXISTS (SELECT 1 FROM deleted) AND EXISTS (SELECT 1 FROM inserted WHERE State <> 0)
                            THROW 51008, ''Correction requests must start pending.'', 1;
                        IF EXISTS (SELECT 1 FROM deleted) AND (UPDATE(Id) OR UPDATE(CompanyId) OR UPDATE(EmployeeId) OR
                            UPDATE(RequesterUserId) OR UPDATE(PolicyId) OR UPDATE(CorrectionSettingsId) OR UPDATE(WorkDate) OR UPDATE(CreatedAtUtc))
                            THROW 51008, ''Correction request identity and evidence are immutable.'', 1;
                        IF EXISTS (SELECT 1 FROM deleted) AND (UPDATE(ProposedAtUtc) OR UPDATE(PunchKind) OR UPDATE(Reason))
                            AND EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON d.Id = i.Id WHERE d.State <> 0 OR i.State <> 0
                                OR EXISTS (SELECT 1 FROM CorrectionDecisions cd WHERE cd.RequestId = d.Id))
                            THROW 51008, ''Correction request payload is immutable after a decision.'', 1;
                        IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON d.Id = i.Id WHERE (d.State <> 0 AND NOT (d.State IN (1,2) AND i.State=0 AND
                                EXISTS(SELECT 1 FROM CorrectionDecisions cd JOIN CorrectionDecisionReversals rv ON rv.DecisionId=cd.Id
                                       WHERE cd.RequestId=d.Id AND cd.[Sequence]=(SELECT MAX(z.[Sequence]) FROM CorrectionDecisions z WHERE z.RequestId=d.Id))
                                AND NOT EXISTS(SELECT 1 FROM CorrectionDecisions cd WHERE cd.RequestId=d.Id
                                               AND NOT EXISTS(SELECT 1 FROM CorrectionDecisionReversals rv WHERE rv.DecisionId=cd.Id))))
                                OR i.State NOT IN (0,1,2,3))
                            THROW 51008, ''Invalid correction request transition.'', 1;
                        END');
        """);
        migrationBuilder.Sql("""
        EXEC(N'CREATE OR ALTER TRIGGER [dbo].[TR_LeaveRequests_ImmutablePayload] ON [dbo].[LeaveRequests]
                        AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                        IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
                            THROW 51009, ''Leave requests cannot be deleted.'', 1;
                        IF NOT EXISTS (SELECT 1 FROM deleted) AND EXISTS (SELECT 1 FROM inserted WHERE State <> 0)
                            THROW 51009, ''Leave requests must start pending.'', 1;
                        IF EXISTS (SELECT 1 FROM deleted) AND (UPDATE(Id) OR UPDATE(CompanyId) OR UPDATE(EmployeeId) OR
                            UPDATE(RequesterUserId) OR UPDATE(LeaveTypeId) OR UPDATE(OperationId) OR UPDATE(Category) OR UPDATE(Kind) OR UPDATE(WorkDate) OR
                            UPDATE(FromAtUtc) OR UPDATE(UntilAtUtcExclusive) OR UPDATE(Minutes) OR UPDATE(Reason) OR UPDATE(CreatedAtUtc))
                            THROW 51009, ''Leave request payload is immutable.'', 1;
                        IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON d.Id = i.Id WHERE (d.State <> 0 AND NOT (d.State IN (1,2) AND i.State=0 AND
                                EXISTS(SELECT 1 FROM LeaveDecisions cd JOIN LeaveDecisionReversals rv ON rv.DecisionId=cd.Id
                                       WHERE cd.RequestId=d.Id AND cd.[Sequence]=(SELECT MAX(z.[Sequence]) FROM LeaveDecisions z WHERE z.RequestId=d.Id))
                                AND NOT EXISTS(SELECT 1 FROM LeaveDecisions cd WHERE cd.RequestId=d.Id
                                               AND NOT EXISTS(SELECT 1 FROM LeaveDecisionReversals rv WHERE rv.DecisionId=cd.Id))))
                                OR i.State NOT IN (0,1,2,3))
                            THROW 51009, ''Invalid leave request transition.'', 1;
                        END');
        """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        IF EXISTS(SELECT 1 FROM dbo.CorrectionDecisionReversals) OR EXISTS(SELECT 1 FROM dbo.LeaveDecisionReversals)
         OR EXISTS(SELECT 1 FROM dbo.CorrectionDecisions WHERE [Sequence]>1) OR EXISTS(SELECT 1 FROM dbo.LeaveDecisions WHERE [Sequence]>1)
         THROW 51010, 'Reversal history exists; restore a validated backup instead of destructive downgrade.', 1;
        """);
        migrationBuilder.Sql("""
        DROP TABLE dbo.LeaveDecisionReversals;
        """);
        migrationBuilder.Sql("""
        DROP TABLE dbo.CorrectionDecisionReversals;
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.LeaveDecisions DROP CONSTRAINT FK_LeaveDecisions_LeaveLedgerEntries_LedgerEntryId;
        """);
        migrationBuilder.Sql("""
        DROP INDEX IX_LeaveDecisions_LedgerEntryId ON dbo.LeaveDecisions;
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.LeaveDecisions DROP COLUMN LedgerEntryId;
        """);
        migrationBuilder.Sql("""
        DROP INDEX IX_CorrectionDecisions_RequestId_Sequence ON dbo.CorrectionDecisions;
        """);
        migrationBuilder.Sql("""
        CREATE UNIQUE INDEX IX_CorrectionDecisions_RequestId ON dbo.CorrectionDecisions(RequestId);
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.CorrectionDecisions DROP CONSTRAINT CK_CorrectionDecisions_Sequence;
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.CorrectionDecisions DROP CONSTRAINT DF_CorrectionDecisions_Sequence;
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.CorrectionDecisions DROP COLUMN [Sequence];
        """);
        migrationBuilder.Sql("""
        EXEC(N'CREATE OR ALTER TRIGGER [dbo].[TR_CorrectionRequests_ImmutablePayload] ON [dbo].[CorrectionRequests]
                        AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                        IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
                            THROW 51008, ''Correction requests cannot be deleted.'', 1;
                        IF NOT EXISTS (SELECT 1 FROM deleted) AND EXISTS (SELECT 1 FROM inserted WHERE State <> 0)
                            THROW 51008, ''Correction requests must start pending.'', 1;
                        IF EXISTS (SELECT 1 FROM deleted) AND (UPDATE(Id) OR UPDATE(CompanyId) OR UPDATE(EmployeeId) OR
                            UPDATE(RequesterUserId) OR UPDATE(PolicyId) OR UPDATE(CorrectionSettingsId) OR UPDATE(WorkDate) OR UPDATE(CreatedAtUtc))
                            THROW 51008, ''Correction request identity and evidence are immutable.'', 1;
                        IF EXISTS (SELECT 1 FROM deleted) AND (UPDATE(ProposedAtUtc) OR UPDATE(PunchKind) OR UPDATE(Reason))
                            AND EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON d.Id = i.Id WHERE d.State <> 0 OR i.State <> 0
                                OR EXISTS (SELECT 1 FROM CorrectionDecisions cd WHERE cd.RequestId = d.Id))
                            THROW 51008, ''Correction request payload is immutable after a decision.'', 1;
                        IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON d.Id = i.Id WHERE d.State <> 0 OR i.State NOT IN (0,1,2,3))
                            THROW 51008, ''Invalid correction request transition.'', 1;
                        END');
        """);
        migrationBuilder.Sql("""
        DROP INDEX IX_LeaveDecisions_RequestId_Sequence ON dbo.LeaveDecisions;
        """);
        migrationBuilder.Sql("""
        CREATE UNIQUE INDEX IX_LeaveDecisions_RequestId ON dbo.LeaveDecisions(RequestId);
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.LeaveDecisions DROP CONSTRAINT CK_LeaveDecisions_Sequence;
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.LeaveDecisions DROP CONSTRAINT DF_LeaveDecisions_Sequence;
        """);
        migrationBuilder.Sql("""
        ALTER TABLE dbo.LeaveDecisions DROP COLUMN [Sequence];
        """);
        migrationBuilder.Sql("""
        EXEC(N'CREATE OR ALTER TRIGGER [dbo].[TR_LeaveRequests_ImmutablePayload] ON [dbo].[LeaveRequests]
                        AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                        IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
                            THROW 51009, ''Leave requests cannot be deleted.'', 1;
                        IF NOT EXISTS (SELECT 1 FROM deleted) AND EXISTS (SELECT 1 FROM inserted WHERE State <> 0)
                            THROW 51009, ''Leave requests must start pending.'', 1;
                        IF EXISTS (SELECT 1 FROM deleted) AND (UPDATE(Id) OR UPDATE(CompanyId) OR UPDATE(EmployeeId) OR
                            UPDATE(RequesterUserId) OR UPDATE(LeaveTypeId) OR UPDATE(OperationId) OR UPDATE(Category) OR UPDATE(Kind) OR UPDATE(WorkDate) OR
                            UPDATE(FromAtUtc) OR UPDATE(UntilAtUtcExclusive) OR UPDATE(Minutes) OR UPDATE(Reason) OR UPDATE(CreatedAtUtc))
                            THROW 51009, ''Leave request payload is immutable.'', 1;
                        IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON d.Id = i.Id WHERE d.State <> 0 OR i.State NOT IN (0,1,2,3))
                            THROW 51009, ''Invalid leave request transition.'', 1;
                        END');
        """);
    }
}
