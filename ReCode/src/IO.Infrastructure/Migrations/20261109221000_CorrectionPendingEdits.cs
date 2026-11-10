using System;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <summary>
    /// Pending correction requests without any recorded decision may be edited by
    /// their requester (proposed time, punch kind and reason only). Identity,
    /// policy, evidence and decided payloads stay immutable.
    /// </summary>
    [DbContext(typeof(IoDbContext))]
    [Migration("20261109221000_CorrectionPendingEdits")]
    public sealed class CorrectionPendingEdits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                EXEC(N'CREATE OR ALTER TRIGGER [dbo].[TR_CorrectionRequests_ImmutablePayload] ON [dbo].[CorrectionRequests]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
                    THROW 51008, ''Correction requests cannot be deleted.'', 1;
                IF NOT EXISTS (SELECT 1 FROM deleted) AND EXISTS (SELECT 1 FROM inserted WHERE State <> 0)
                    THROW 51008, ''Correction requests must start pending.'', 1;
                IF EXISTS (SELECT 1 FROM deleted) AND (UPDATE(Id) OR UPDATE(CompanyId) OR UPDATE(EmployeeId) OR
                    UPDATE(RequesterUserId) OR UPDATE(PolicyId) OR UPDATE(CorrectionSettingsId) OR UPDATE(WorkDate) OR UPDATE(ProposedAtUtc) OR
                    UPDATE(PunchKind) OR UPDATE(Reason) OR UPDATE(CreatedAtUtc))
                    THROW 51008, ''Correction request payload is immutable.'', 1;
                IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON d.Id = i.Id WHERE d.State <> 0 OR i.State NOT IN (1,2,3))
                    THROW 51008, ''Invalid correction request transition.'', 1;
                END');
                """);
        }
    }
}
