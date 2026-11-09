using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConflictNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RawConflictNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConflictId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawConflictNotes", x => x.Id);
                    table.CheckConstraint("CK_RawConflictNotes_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_RawConflictNotes_RawEventConflicts_ConflictId",
                        column: x => x.ConflictId,
                        principalTable: "RawEventConflicts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawConflictNotes_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawConflictNotes_ConflictId_CreatedAtUtc",
                table: "RawConflictNotes",
                columns: new[] { "ConflictId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RawConflictNotes_CreatedByUserId",
                table: "RawConflictNotes",
                column: "CreatedByUserId");
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_RawConflictNotes_Immutable] ON [dbo].[RawConflictNotes]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51010, ''Conflict notes are immutable.'', 1; END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RawConflictNotes");
        }
    }
}
