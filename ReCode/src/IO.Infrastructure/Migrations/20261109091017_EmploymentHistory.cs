using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmploymentHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeSiteAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntilExclusive = table.Column<DateOnly>(type: "date", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSiteAssignments", x => x.Id);
                    table.CheckConstraint("CK_EmployeeSiteAssignments_Interval", "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]");
                    table.ForeignKey(
                        name: "FK_EmployeeSiteAssignments_Employees_EmployeeId_CompanyId",
                        columns: x => new { x.EmployeeId, x.CompanyId },
                        principalTable: "Employees",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeSiteAssignments_Sites_SiteId_CompanyId",
                        columns: x => new { x.SiteId, x.CompanyId },
                        principalTable: "Sites",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmploymentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatusCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntilExclusive = table.Column<DateOnly>(type: "date", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmploymentProfiles", x => x.Id);
                    table.CheckConstraint("CK_EmploymentProfiles_Interval", "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]");
                    table.ForeignKey(
                        name: "FK_EmploymentProfiles_Employees_EmployeeId_CompanyId",
                        columns: x => new { x.EmployeeId, x.CompanyId },
                        principalTable: "Employees",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSiteAssignments_EmployeeId_CompanyId",
                table: "EmployeeSiteAssignments",
                columns: new[] { "EmployeeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSiteAssignments_EmployeeId_ValidFrom",
                table: "EmployeeSiteAssignments",
                columns: new[] { "EmployeeId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSiteAssignments_SiteId_CompanyId",
                table: "EmployeeSiteAssignments",
                columns: new[] { "SiteId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentProfiles_EmployeeId_CompanyId",
                table: "EmploymentProfiles",
                columns: new[] { "EmployeeId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentProfiles_EmployeeId_ValidFrom",
                table: "EmploymentProfiles",
                columns: new[] { "EmployeeId", "ValidFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeSiteAssignments");

            migrationBuilder.DropTable(
                name: "EmploymentProfiles");
        }
    }
}
