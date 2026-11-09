using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorrectionRequestLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyRequestLimit",
                table: "CorrectionSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MonthlyRequestLimit",
                table: "CorrectionSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CorrectionSettings_Limits",
                table: "CorrectionSettings",
                sql: "[DailyRequestLimit] BETWEEN 0 AND 1000 AND [MonthlyRequestLimit] BETWEEN 0 AND 1000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CorrectionSettings_Limits",
                table: "CorrectionSettings");

            migrationBuilder.DropColumn(
                name: "DailyRequestLimit",
                table: "CorrectionSettings");

            migrationBuilder.DropColumn(
                name: "MonthlyRequestLimit",
                table: "CorrectionSettings");
        }
    }
}
