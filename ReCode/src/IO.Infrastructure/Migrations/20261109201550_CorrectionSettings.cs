using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorrectionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CorrectionSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    MaxRequestAgeDays = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectionSettings", x => x.Id);
                    table.CheckConstraint("CK_CorrectionSettings_Age", "[MaxRequestAgeDays] BETWEEN 0 AND 3650");
                    table.CheckConstraint("CK_CorrectionSettings_Revision", "[Revision] > 0");
                    table.CheckConstraint("CK_CorrectionSettings_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_CorrectionSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrectionSettings_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionSettings_CompanyId_Revision",
                table: "CorrectionSettings",
                columns: new[] { "CompanyId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrectionSettings_CreatedByUserId",
                table: "CorrectionSettings",
                column: "CreatedByUserId");
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_CorrectionSettings_Immutable] ON [dbo].[CorrectionSettings]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51040, ''Correction settings history is immutable.'', 1; END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrectionSettings");
        }
    }
}
