using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PeriodDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PeriodDiagnostics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputRevision = table.Column<long>(type: "bigint", nullable: true),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodDiagnostics", x => x.Id);
                    table.CheckConstraint("CK_PeriodDiagnostics_Json", "ISJSON([EvidenceJson]) = 1");
                    table.CheckConstraint("CK_PeriodDiagnostics_Revision", "[InputRevision] IS NULL OR [InputRevision] >= 0");
                    table.CheckConstraint("CK_PeriodDiagnostics_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_PeriodDiagnostics_PeriodDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "PeriodDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PeriodDiagnostics_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PeriodDiagnostics_CreatedByUserId",
                table: "PeriodDiagnostics",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodDiagnostics_DraftId_CreatedAtUtc",
                table: "PeriodDiagnostics",
                columns: new[] { "DraftId", "CreatedAtUtc" });
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_PeriodDiagnostics_Immutable] ON [dbo].[PeriodDiagnostics]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51014, ''Period diagnostics are immutable.'', 1; END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PeriodDiagnostics");
        }
    }
}
