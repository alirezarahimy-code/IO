using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ObservedCalculation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ObservedRuleVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Algorithm = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BeforeMinutes = table.Column<int>(type: "int", nullable: false),
                    AfterMinutes = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObservedRuleVersions", x => x.Id);
                    table.UniqueConstraint("AK_ObservedRuleVersions_Id_CompanyId", x => new { x.Id, x.CompanyId });
                    table.CheckConstraint("CK_ObservedRules_Algorithm", "[Algorithm] = N'paired-v1'");
                    table.CheckConstraint("CK_ObservedRules_CreatedUtc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                    table.CheckConstraint("CK_ObservedRules_Margins", "[BeforeMinutes] BETWEEN 0 AND 720 AND [AfterMinutes] BETWEEN 0 AND 720");
                    table.ForeignKey(
                        name: "FK_ObservedRuleVersions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ObservedRuleVersions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyCalculationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleAssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    WindowStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WindowEndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InputSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnomaliesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ObservedTicks = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyCalculationRuns", x => x.Id);
                    table.CheckConstraint("CK_CalculationRuns_Ticks", "[ObservedTicks] IS NULL OR [ObservedTicks] >= 0");
                    table.CheckConstraint("CK_CalculationRuns_Utc", "DATEPART(TZOFFSET, [WindowStartUtc]) = 0 AND DATEPART(TZOFFSET, [WindowEndUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                    table.CheckConstraint("CK_CalculationRuns_Window", "[WindowEndUtc] > [WindowStartUtc]");
                    table.ForeignKey(
                        name: "FK_DailyCalculationRuns_Employees_EmployeeId_CompanyId",
                        columns: x => new { x.EmployeeId, x.CompanyId },
                        principalTable: "Employees",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyCalculationRuns_EventSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "EventSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyCalculationRuns_ObservedRuleVersions_RuleVersionId_CompanyId",
                        columns: x => new { x.RuleVersionId, x.CompanyId },
                        principalTable: "ObservedRuleVersions",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyCalculationRuns_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyCalculationRuns_WorkScheduleAssignments_ScheduleAssignmentId",
                        column: x => x.ScheduleAssignmentId,
                        principalTable: "WorkScheduleAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyCalculationRuns_CreatedByUserId",
                table: "DailyCalculationRuns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCalculationRuns_EmployeeId_CompanyId",
                table: "DailyCalculationRuns",
                columns: new[] { "EmployeeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyCalculationRuns_EmployeeId_WorkDate_CreatedAtUtc",
                table: "DailyCalculationRuns",
                columns: new[] { "EmployeeId", "WorkDate", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyCalculationRuns_RuleVersionId_CompanyId",
                table: "DailyCalculationRuns",
                columns: new[] { "RuleVersionId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyCalculationRuns_ScheduleAssignmentId",
                table: "DailyCalculationRuns",
                column: "ScheduleAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCalculationRuns_SourceId",
                table: "DailyCalculationRuns",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ObservedRuleVersions_CompanyId",
                table: "ObservedRuleVersions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ObservedRuleVersions_CreatedByUserId",
                table: "ObservedRuleVersions",
                column: "CreatedByUserId");
            // The immutable policy and every execution snapshot are append-only.
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_ObservedRuleVersions_Immutable]
                ON [dbo].[ObservedRuleVersions] AFTER UPDATE, DELETE AS
                BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                    THROW 51004, ''Observed rule versions are immutable.'', 1; END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_DailyCalculationRuns_Immutable]
                ON [dbo].[DailyCalculationRuns] AFTER UPDATE, DELETE AS
                BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                    THROW 51005, ''Calculation runs are immutable.'', 1; END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyCalculationRuns");

            migrationBuilder.DropTable(
                name: "ObservedRuleVersions");
        }
    }
}
