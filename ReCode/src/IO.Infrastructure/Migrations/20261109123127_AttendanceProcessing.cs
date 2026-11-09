using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RawEventConflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalEventId = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    SourceEmployeeIdentifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceTimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReportedEventType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SourcePayload = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawEventConflicts", x => x.Id);
                    table.CheckConstraint("CK_RawEventConflicts_OccurredUtc", "[OccurredAtUtc] IS NULL OR DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
                    table.CheckConstraint("CK_RawEventConflicts_ReceivedUtc", "DATEPART(TZOFFSET, [ReceivedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_RawEventConflicts_EventSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "EventSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawEventConflicts_RawAttendanceEvents_OriginalEventId",
                        column: x => x.OriginalEventId,
                        principalTable: "RawAttendanceEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceEmployeeMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourcePersonId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidUntilUtcExclusive = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    SourcePersonIdByteLength = table.Column<int>(type: "int", nullable: false, computedColumnSql: "DATALENGTH([SourcePersonId])", stored: true),
                    SourcePersonIdBytes = table.Column<byte[]>(type: "varbinary(200)", nullable: true, computedColumnSql: "CONVERT(varbinary(200), [SourcePersonId])", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceEmployeeMappings", x => x.Id);
                    table.CheckConstraint("CK_SourceEmployeeMappings_EndUtc", "[ValidUntilUtcExclusive] IS NULL OR DATEPART(TZOFFSET, [ValidUntilUtcExclusive]) = 0");
                    table.CheckConstraint("CK_SourceEmployeeMappings_Identifier", "DATALENGTH([SourcePersonId]) > 0");
                    table.CheckConstraint("CK_SourceEmployeeMappings_Interval", "[ValidUntilUtcExclusive] IS NULL OR [ValidUntilUtcExclusive] > [ValidFromUtc]");
                    table.CheckConstraint("CK_SourceEmployeeMappings_StartUtc", "DATEPART(TZOFFSET, [ValidFromUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_SourceEmployeeMappings_Employees_EmployeeId_CompanyId",
                        columns: x => new { x.EmployeeId, x.CompanyId },
                        principalTable: "Employees",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceEmployeeMappings_EventSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "EventSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawEventConflicts_OriginalEventId",
                table: "RawEventConflicts",
                column: "OriginalEventId");

            migrationBuilder.CreateIndex(
                name: "IX_RawEventConflicts_SourceId_ReceivedAtUtc",
                table: "RawEventConflicts",
                columns: new[] { "SourceId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceEmployeeMappings_EmployeeId_CompanyId",
                table: "SourceEmployeeMappings",
                columns: new[] { "EmployeeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceEmployeeMappings_SourceId_SourcePersonIdBytes_SourcePersonIdByteLength_ValidFromUtc",
                table: "SourceEmployeeMappings",
                columns: new[] { "SourceId", "SourcePersonIdBytes", "SourcePersonIdByteLength", "ValidFromUtc" });
            // EF stores trigger metadata only; enforce append-only conflicts in SQL.
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_RawEventConflicts_Immutable]
                ON [dbo].[RawEventConflicts]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51003, ''Raw conflict receipts are immutable.'', 1;
                END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RawEventConflicts");

            migrationBuilder.DropTable(
                name: "SourceEmployeeMappings");
        }
    }
}
