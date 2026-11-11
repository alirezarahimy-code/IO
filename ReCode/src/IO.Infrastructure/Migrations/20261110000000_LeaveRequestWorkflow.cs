using System;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <summary>
    /// Leave request workflow: daily and hourly leave requests with one immutable
    /// decision (or requester cancellation) per request, mirroring the correction
    /// request state machine. Approval itself is posted by the service as a usage
    /// movement into the existing leave ledger, never by this schema.
    /// </summary>
    [DbContext(typeof(IoDbContext))]
    [Migration("20261110000000_LeaveRequestWorkflow")]
    public sealed class LeaveRequestWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FromAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UntilAtUtcExclusive = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequests", x => x.Id);
                    table.CheckConstraint("CK_LeaveRequests_Kind", "[Kind] IN (0,1)");
                    table.CheckConstraint("CK_LeaveRequests_Category", "[Category] IN (0,1,2)");
                    table.CheckConstraint("CK_LeaveRequests_OvertimeHourly", "[Category] <> 1 OR [Kind] = 1");
                    table.CheckConstraint("CK_LeaveRequests_Minutes", "[Minutes] BETWEEN 1 AND 1440");
                    table.CheckConstraint("CK_LeaveRequests_State", "[State] IN (0,1,2,3)");
                    table.CheckConstraint("CK_LeaveRequests_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [FromAtUtc]) = 0 AND DATEPART(TZOFFSET, [UntilAtUtcExclusive]) = 0");
                    table.ForeignKey(
                        name: "FK_LeaveRequests_Employees_EmployeeId_CompanyId",
                        columns: x => new { x.EmployeeId, x.CompanyId },
                        principalTable: "Employees",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeaveTypes_LeaveTypeId_CompanyId",
                        columns: x => new { x.LeaveTypeId, x.CompanyId },
                        principalTable: "LeaveTypes",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_Users_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveDecisions", x => x.Id);
                    table.CheckConstraint("CK_LeaveDecisions_Decision", "[Decision] IN (1,2,3)");
                    table.CheckConstraint("CK_LeaveDecisions_Utc", "DATEPART(TZOFFSET, [AtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_LeaveDecisions_LeaveRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "LeaveRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveDecisions_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_OperationId",
                table: "LeaveRequests",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_RequesterUserId",
                table: "LeaveRequests",
                column: "RequesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_EmployeeId_CompanyId",
                table: "LeaveRequests",
                columns: new[] { "EmployeeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_LeaveTypeId_CompanyId",
                table: "LeaveRequests",
                columns: new[] { "LeaveTypeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_EmployeeId_WorkDate_State",
                table: "LeaveRequests",
                columns: new[] { "EmployeeId", "WorkDate", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveDecisions_ActorUserId",
                table: "LeaveDecisions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveDecisions_RequestId",
                table: "LeaveDecisions",
                column: "RequestId",
                unique: true);

            migrationBuilder.Sql("""
                EXEC(N'CREATE OR ALTER TRIGGER [dbo].[TR_LeaveRequests_ImmutablePayload] ON [dbo].[LeaveRequests]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
                    THROW 51009, ''Leave requests cannot be deleted.'', 1;
                IF NOT EXISTS (SELECT 1 FROM deleted) AND EXISTS (SELECT 1 FROM inserted WHERE State <> 0)
                    THROW 51009, ''Leave requests must start pending.'', 1;
                IF EXISTS (SELECT 1 FROM deleted) AND (UPDATE(Id) OR UPDATE(CompanyId) OR UPDATE(EmployeeId) OR
                    UPDATE(RequesterUserId) OR UPDATE(LeaveTypeId) OR UPDATE(OperationId) OR UPDATE(Category) OR UPDATE(Kind) OR UPDATE(WorkDate) OR
                    UPDATE(FromAtUtc) OR UPDATE(UntilAtUtcExclusive) OR UPDATE(Minutes) OR UPDATE(Reason) OR UPDATE(CreatedAtUtc))
                    THROW 51009, ''Leave request payload is immutable.'', 1;
                IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON d.Id = i.Id WHERE d.State <> 0 OR i.State NOT IN (0,1,2,3))
                    THROW 51009, ''Invalid leave request transition.'', 1;
                END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE OR ALTER TRIGGER [dbo].[TR_LeaveDecisions_Immutable] ON [dbo].[LeaveDecisions]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51009, ''Leave decisions are immutable.'', 1; END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaveDecisions");

            migrationBuilder.DropTable(
                name: "LeaveRequests");
        }
    }
}
