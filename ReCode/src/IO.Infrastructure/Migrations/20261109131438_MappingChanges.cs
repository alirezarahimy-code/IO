using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MappingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceMappingChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousMappingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NewMappingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceMappingChanges", x => x.Id);
                    table.CheckConstraint("CK_SourceMappingChanges_Distinct", "[PreviousMappingId] <> [NewMappingId]");
                    table.CheckConstraint("CK_SourceMappingChanges_Utc", "DATEPART(TZOFFSET, [EffectiveAtUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_SourceMappingChanges_SourceEmployeeMappings_NewMappingId",
                        column: x => x.NewMappingId,
                        principalTable: "SourceEmployeeMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceMappingChanges_SourceEmployeeMappings_PreviousMappingId",
                        column: x => x.PreviousMappingId,
                        principalTable: "SourceEmployeeMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceMappingChanges_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceMappingChanges_ActorUserId",
                table: "SourceMappingChanges",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceMappingChanges_NewMappingId",
                table: "SourceMappingChanges",
                column: "NewMappingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceMappingChanges_PreviousMappingId",
                table: "SourceMappingChanges",
                column: "PreviousMappingId",
                unique: true);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_SourceMappingChanges_Immutable] ON [dbo].[SourceMappingChanges]
                AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted)
                THROW 51011, ''Mapping change history is immutable.'', 1; END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceMappingChanges");
        }
    }
}
