using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SchedulingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Sites_Id_CompanyId",
                table: "Sites",
                columns: new[] { "Id", "CompanyId" });

            migrationBuilder.CreateTable(
                name: "Calendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calendars", x => x.Id);
                    table.UniqueConstraint("AK_Calendars_Id_CompanyId", x => new { x.Id, x.CompanyId });
                    table.ForeignKey(
                        name: "FK_Calendars_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartsAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndsAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndDayOffset = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftDefinitions", x => x.Id);
                    table.UniqueConstraint("AK_ShiftDefinitions_Id_CompanyId", x => new { x.Id, x.CompanyId });
                    table.CheckConstraint("CK_ShiftDefinitions_Interval", "([EndDayOffset] = 0 AND [EndsAt] > [StartsAt]) OR ([EndDayOffset] = 1 AND [EndsAt] <= [StartsAt])");
                    table.ForeignKey(
                        name: "FK_ShiftDefinitions_Sites_SiteId_CompanyId",
                        columns: x => new { x.SiteId, x.CompanyId },
                        principalTable: "Sites",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CalendarDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarDays", x => x.Id);
                    table.CheckConstraint("CK_CalendarDays_Kind", "[Kind] IN (0, 1, 2)");
                    table.CheckConstraint("CK_CalendarDays_RecordedUtc", "DATEPART(TZOFFSET, [RecordedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_CalendarDays_Calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "Calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkScheduleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntilExclusive = table.Column<DateOnly>(type: "date", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkScheduleAssignments", x => x.Id);
                    table.CheckConstraint("CK_WorkScheduleAssignments_Interval", "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]");
                    table.ForeignKey(
                        name: "FK_WorkScheduleAssignments_Calendars_CalendarId_CompanyId",
                        columns: x => new { x.CalendarId, x.CompanyId },
                        principalTable: "Calendars",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkScheduleAssignments_Employees_EmployeeId_CompanyId",
                        columns: x => new { x.EmployeeId, x.CompanyId },
                        principalTable: "Employees",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkScheduleAssignments_ShiftDefinitions_ShiftId_CompanyId",
                        columns: x => new { x.ShiftId, x.CompanyId },
                        principalTable: "ShiftDefinitions",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarDays_CalendarId_Date",
                table: "CalendarDays",
                columns: new[] { "CalendarId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Calendars_CompanyId",
                table: "Calendars",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftDefinitions_SiteId_CompanyId",
                table: "ShiftDefinitions",
                columns: new[] { "SiteId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkScheduleAssignments_CalendarId_CompanyId",
                table: "WorkScheduleAssignments",
                columns: new[] { "CalendarId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkScheduleAssignments_EmployeeId_CompanyId",
                table: "WorkScheduleAssignments",
                columns: new[] { "EmployeeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkScheduleAssignments_EmployeeId_ValidFrom",
                table: "WorkScheduleAssignments",
                columns: new[] { "EmployeeId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkScheduleAssignments_ShiftId_CompanyId",
                table: "WorkScheduleAssignments",
                columns: new[] { "ShiftId", "CompanyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarDays");

            migrationBuilder.DropTable(
                name: "WorkScheduleAssignments");

            migrationBuilder.DropTable(
                name: "Calendars");

            migrationBuilder.DropTable(
                name: "ShiftDefinitions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Sites_Id_CompanyId",
                table: "Sites");
        }
    }
}
