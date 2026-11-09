using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DelegationRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Delegations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrincipalUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrincipalEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelegateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelegateEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntilExclusive = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delegations", x => x.Id);
                    table.CheckConstraint("CK_Delegations_DifferentPeople", "[PrincipalUserId] <> [DelegateUserId] AND [PrincipalEmployeeId] <> [DelegateEmployeeId]");
                    table.CheckConstraint("CK_Delegations_Interval", "[ValidFrom] < [ValidUntilExclusive]");
                    table.CheckConstraint("CK_Delegations_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_Delegations_Employees_DelegateEmployeeId_CompanyId",
                        columns: x => new { x.DelegateEmployeeId, x.CompanyId },
                        principalTable: "Employees",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Delegations_Employees_PrincipalEmployeeId_CompanyId",
                        columns: x => new { x.PrincipalEmployeeId, x.CompanyId },
                        principalTable: "Employees",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Delegations_Users_DelegateUserId",
                        column: x => x.DelegateUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Delegations_Users_PrincipalUserId",
                        column: x => x.PrincipalUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DelegationRevocations",
                columns: table => new
                {
                    DelegationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationRevocations", x => x.DelegationId);
                    table.CheckConstraint("CK_DelegationRevocations_Utc", "DATEPART(TZOFFSET, [RevokedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_DelegationRevocations_Delegations_DelegationId",
                        column: x => x.DelegationId,
                        principalTable: "Delegations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DelegationRevocations_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationRevocations_ActorUserId",
                table: "DelegationRevocations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_DelegateEmployeeId_CompanyId",
                table: "Delegations",
                columns: new[] { "DelegateEmployeeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_DelegateUserId",
                table: "Delegations",
                column: "DelegateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_PrincipalEmployeeId_CompanyId",
                table: "Delegations",
                columns: new[] { "PrincipalEmployeeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_PrincipalUserId_ValidFrom_ValidUntilExclusive",
                table: "Delegations",
                columns: new[] { "PrincipalUserId", "ValidFrom", "ValidUntilExclusive" });
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Delegations_Immutable] ON [dbo].[Delegations]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51030, ''Delegation records are immutable.'', 1; END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_DelegationRevocations_Immutable] ON [dbo].[DelegationRevocations]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51031, ''Delegation revocations are immutable.'', 1; END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelegationRevocations");

            migrationBuilder.DropTable(
                name: "Delegations");
        }
    }
}
