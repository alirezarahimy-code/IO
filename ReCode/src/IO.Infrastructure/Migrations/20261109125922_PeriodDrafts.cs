using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PeriodDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PeriodDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    EndExclusive = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodDrafts", x => x.Id);
                    table.CheckConstraint("CK_PeriodDrafts_Interval", "[EndExclusive] > [Start] AND DATEDIFF(day, [Start], [EndExclusive]) <= 62");
                    table.CheckConstraint("CK_PeriodDrafts_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_PeriodDrafts_Employees_EmployeeId_CompanyId",
                        columns: x => new { x.EmployeeId, x.CompanyId },
                        principalTable: "Employees",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PeriodDrafts_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PeriodDrafts_CreatedByUserId",
                table: "PeriodDrafts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodDrafts_EmployeeId_CompanyId",
                table: "PeriodDrafts",
                columns: new[] { "EmployeeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PeriodDrafts_EmployeeId_Start_EndExclusive",
                table: "PeriodDrafts",
                columns: new[] { "EmployeeId", "Start", "EndExclusive" },
                unique: true);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_PeriodDrafts_Immutable] ON [dbo].[PeriodDrafts]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51009, ''Period drafts are immutable.'', 1; END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PeriodDrafts");
        }
    }
}
