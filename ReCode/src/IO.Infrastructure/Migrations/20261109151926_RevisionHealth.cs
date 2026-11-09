using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RevisionHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InputMonitorState",
                table: "DailyCalculationRuns",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CalculationRuns_MonitorState",
                table: "DailyCalculationRuns",
                sql: "[InputMonitorState] IS NULL OR [InputMonitorState] IN (N'present', N'unverified')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CalculationRuns_MonitorState",
                table: "DailyCalculationRuns");

            migrationBuilder.DropColumn(
                name: "InputMonitorState",
                table: "DailyCalculationRuns");
        }
    }
}
