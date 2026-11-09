using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorrectionSettingsEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrectionSettingsId",
                table: "CorrectionRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CorrectionSettings_Id_CompanyId",
                table: "CorrectionSettings",
                columns: new[] { "Id", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionRequests_CorrectionSettingsId_CompanyId",
                table: "CorrectionRequests",
                columns: new[] { "CorrectionSettingsId", "CompanyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CorrectionRequests_CorrectionSettings_CorrectionSettingsId_CompanyId",
                table: "CorrectionRequests",
                columns: new[] { "CorrectionSettingsId", "CompanyId" },
                principalTable: "CorrectionSettings",
                principalColumns: new[] { "Id", "CompanyId" },
                onDelete: ReferentialAction.Restrict);
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
                    UPDATE(RequesterUserId) OR UPDATE(PolicyId) OR UPDATE(WorkDate) OR UPDATE(ProposedAtUtc) OR
                    UPDATE(PunchKind) OR UPDATE(Reason) OR UPDATE(CreatedAtUtc))
                    THROW 51008, ''Correction request payload is immutable.'', 1;
                IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON d.Id = i.Id WHERE d.State <> 0 OR i.State NOT IN (1,2,3))
                    THROW 51008, ''Invalid correction request transition.'', 1;
                END');
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_CorrectionRequests_CorrectionSettings_CorrectionSettingsId_CompanyId",
                table: "CorrectionRequests");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CorrectionSettings_Id_CompanyId",
                table: "CorrectionSettings");

            migrationBuilder.DropIndex(
                name: "IX_CorrectionRequests_CorrectionSettingsId_CompanyId",
                table: "CorrectionRequests");

            migrationBuilder.DropColumn(
                name: "CorrectionSettingsId",
                table: "CorrectionRequests");
        }
    }
}
