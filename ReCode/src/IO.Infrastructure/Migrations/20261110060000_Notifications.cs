using System;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <summary>
    /// In-app notifications: one row per decision that a requester should see.
    /// The payload (recipient, category, text, creation time) is immutable; only
    /// the read timestamp may change once.
    /// </summary>
    [DbContext(typeof(IoDbContext))]
    [Migration("20261110060000_Notifications")]
    public sealed class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ReadAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_ReadAtUtc",
                table: "Notifications",
                columns: new[] { "UserId", "ReadAtUtc" });

            migrationBuilder.Sql("""
                EXEC(N'CREATE OR ALTER TRIGGER [dbo].[TR_Notifications_ImmutablePayload] ON [dbo].[Notifications]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
                    THROW 51010, ''Notifications cannot be deleted.'', 1;
                IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON d.Id = i.Id WHERE
                    UPDATE(UserId) OR UPDATE(Category) OR UPDATE(Text) OR UPDATE(CreatedAtUtc))
                    THROW 51010, ''Notification payload is immutable.'', 1;
                END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
