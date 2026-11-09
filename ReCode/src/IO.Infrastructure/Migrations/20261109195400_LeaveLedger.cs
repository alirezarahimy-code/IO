using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeaveLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaveTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    MinutesPerDay = table.Column<int>(type: "int", nullable: false),
                    CarryOverAllowed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveTypes", x => x.Id);
                    table.UniqueConstraint("AK_LeaveTypes_Id_CompanyId", x => new { x.Id, x.CompanyId });
                    table.CheckConstraint("CK_LeaveTypes_MinutesPerDay", "[MinutesPerDay] BETWEEN 1 AND 1440");
                    table.CheckConstraint("CK_LeaveTypes_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_LeaveTypes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveTypes_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersianYear = table.Column<int>(type: "int", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveLedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_LeaveLedgerEntries_Minutes", "[Minutes] BETWEEN -527040 AND 527040 AND (([Kind] = 0 AND [Minutes] >= 0) OR ([Kind] = 1 AND [Minutes] > 0) OR ([Kind] = 2 AND [Minutes] < 0) OR ([Kind] = 3 AND [Minutes] <> 0))");
                    table.CheckConstraint("CK_LeaveLedgerEntries_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                    table.CheckConstraint("CK_LeaveLedgerEntries_Year", "[PersianYear] BETWEEN 1300 AND 1500");
                    table.ForeignKey(
                        name: "FK_LeaveLedgerEntries_Employees_EmployeeId_CompanyId",
                        columns: x => new { x.EmployeeId, x.CompanyId },
                        principalTable: "Employees",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveLedgerEntries_LeaveTypes_LeaveTypeId_CompanyId",
                        columns: x => new { x.LeaveTypeId, x.CompanyId },
                        principalTable: "LeaveTypes",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveLedgerEntries_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveLedgerEntries_CreatedByUserId",
                table: "LeaveLedgerEntries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveLedgerEntries_EmployeeId_CompanyId",
                table: "LeaveLedgerEntries",
                columns: new[] { "EmployeeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveLedgerEntries_EmployeeId_LeaveTypeId_PersianYear",
                table: "LeaveLedgerEntries",
                columns: new[] { "EmployeeId", "LeaveTypeId", "PersianYear" },
                unique: true,
                filter: "[Kind] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveLedgerEntries_EmployeeId_LeaveTypeId_PersianYear_EffectiveDate",
                table: "LeaveLedgerEntries",
                columns: new[] { "EmployeeId", "LeaveTypeId", "PersianYear", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveLedgerEntries_LeaveTypeId_CompanyId",
                table: "LeaveLedgerEntries",
                columns: new[] { "LeaveTypeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_CompanyId_Symbol",
                table: "LeaveTypes",
                columns: new[] { "CompanyId", "Symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_CreatedByUserId",
                table: "LeaveTypes",
                column: "CreatedByUserId");
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_LeaveTypes_Immutable] ON [dbo].[LeaveTypes]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51020, ''Leave types are immutable.'', 1; END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_LeaveLedgerEntries_Immutable] ON [dbo].[LeaveLedgerEntries]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51021, ''Leave ledger entries are immutable.'', 1; END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaveLedgerEntries");

            migrationBuilder.DropTable(
                name: "LeaveTypes");
        }
    }
}
