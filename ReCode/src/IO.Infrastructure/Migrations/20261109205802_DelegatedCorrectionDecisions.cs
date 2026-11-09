using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DelegatedCorrectionDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CorrectionDelegationGrants",
                columns: table => new
                {
                    DelegationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrincipalUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectionDelegationGrants", x => x.DelegationId);
                    table.CheckConstraint("CK_CorrectionDelegationGrants_Utc", "DATEPART(TZOFFSET, [GrantedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_CorrectionDelegationGrants_Delegations_DelegationId",
                        column: x => x.DelegationId,
                        principalTable: "Delegations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrectionDelegationGrants_Users_PrincipalUserId",
                        column: x => x.PrincipalUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DelegatedCorrectionDecisions",
                columns: table => new
                {
                    DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelegationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegatedCorrectionDecisions", x => x.DecisionId);
                    table.ForeignKey(
                        name: "FK_DelegatedCorrectionDecisions_CorrectionDecisions_DecisionId",
                        column: x => x.DecisionId,
                        principalTable: "CorrectionDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DelegatedCorrectionDecisions_CorrectionDelegationGrants_DelegationId",
                        column: x => x.DelegationId,
                        principalTable: "CorrectionDelegationGrants",
                        principalColumn: "DelegationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionDelegationGrants_PrincipalUserId",
                table: "CorrectionDelegationGrants",
                column: "PrincipalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegatedCorrectionDecisions_DelegationId",
                table: "DelegatedCorrectionDecisions",
                column: "DelegationId");
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_CorrectionDelegationGrants_Immutable] ON [dbo].[CorrectionDelegationGrants]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51032, ''Correction delegation grants are immutable.'', 1; END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_DelegatedCorrectionDecisions_Immutable] ON [dbo].[DelegatedCorrectionDecisions]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51033, ''Delegated correction decision evidence is immutable.'', 1; END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_DelegatedCorrectionDecisions_Immutable]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_CorrectionDelegationGrants_Immutable]");
            migrationBuilder.DropTable(
                name: "DelegatedCorrectionDecisions");

            migrationBuilder.DropTable(
                name: "CorrectionDelegationGrants");
        }
    }
}
