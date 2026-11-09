using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorrectionWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CorrectionPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApproverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectionPolicies", x => x.Id);
                    table.UniqueConstraint("AK_CorrectionPolicies_Id_CompanyId", x => new { x.Id, x.CompanyId });
                    table.CheckConstraint("CK_CorrectionPolicies_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_CorrectionPolicies_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrectionPolicies_Users_ApproverUserId",
                        column: x => x.ApproverUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrectionPolicies_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CorrectionRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PunchKind = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectionRequests", x => x.Id);
                    table.CheckConstraint("CK_CorrectionRequests_Kind", "[PunchKind] IN (0,1)");
                    table.CheckConstraint("CK_CorrectionRequests_State", "[State] IN (0,1,2,3)");
                    table.CheckConstraint("CK_CorrectionRequests_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ProposedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_CorrectionRequests_CorrectionPolicies_PolicyId_CompanyId",
                        columns: x => new { x.PolicyId, x.CompanyId },
                        principalTable: "CorrectionPolicies",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrectionRequests_Employees_EmployeeId_CompanyId",
                        columns: x => new { x.EmployeeId, x.CompanyId },
                        principalTable: "Employees",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrectionRequests_Users_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CorrectionDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectionDecisions", x => x.Id);
                    table.CheckConstraint("CK_CorrectionDecisions_Decision", "[Decision] IN (1,2,3)");
                    table.CheckConstraint("CK_CorrectionDecisions_Utc", "DATEPART(TZOFFSET, [AtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_CorrectionDecisions_CorrectionRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "CorrectionRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrectionDecisions_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionDecisions_ActorUserId",
                table: "CorrectionDecisions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionDecisions_RequestId",
                table: "CorrectionDecisions",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionPolicies_ApproverUserId",
                table: "CorrectionPolicies",
                column: "ApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionPolicies_CompanyId",
                table: "CorrectionPolicies",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionPolicies_CreatedByUserId",
                table: "CorrectionPolicies",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionRequests_EmployeeId_CompanyId",
                table: "CorrectionRequests",
                columns: new[] { "EmployeeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionRequests_EmployeeId_WorkDate_State",
                table: "CorrectionRequests",
                columns: new[] { "EmployeeId", "WorkDate", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionRequests_PolicyId_CompanyId",
                table: "CorrectionRequests",
                columns: new[] { "PolicyId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionRequests_RequesterUserId",
                table: "CorrectionRequests",
                column: "RequesterUserId");
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_CorrectionPolicies_Immutable] ON [dbo].[CorrectionPolicies]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51006, ''Correction policies are immutable.'', 1; END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_CorrectionDecisions_Immutable] ON [dbo].[CorrectionDecisions]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51007, ''Correction decisions are immutable.'', 1; END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_CorrectionRequests_ImmutablePayload] ON [dbo].[CorrectionRequests]
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrectionDecisions");

            migrationBuilder.DropTable(
                name: "CorrectionRequests");

            migrationBuilder.DropTable(
                name: "CorrectionPolicies");
        }
    }
}
