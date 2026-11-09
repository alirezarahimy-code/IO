using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CalculationRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "InputRevision",
                table: "DailyCalculationRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CalculationInputRevision",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalculationInputRevision", x => x.Id);
                    table.CheckConstraint("CK_CalculationInputRevision_Singleton", "[Id] = 1 AND [Revision] >= 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CalculationRuns_Revision",
                table: "DailyCalculationRuns",
                sql: "[InputRevision] IS NULL OR [InputRevision] >= 0");
            // Existing snapshots predate revision tracking and retain NULL baselines.
            migrationBuilder.Sql("INSERT INTO [dbo].[CalculationInputRevision] ([Id], [Revision]) VALUES (1, 0);");
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_CalculationInputRevision_Monotonic] ON [dbo].[CalculationInputRevision]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted i FULL JOIN deleted d ON i.Id = d.Id
                    WHERE i.Id IS NULL OR d.Id IS NULL OR i.Revision <> d.Revision + CONVERT(bigint, 1))
                    THROW 51012, ''Input revision can only advance by one.'', 1;
                END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_Companies] ON [dbo].[Companies]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_Sites] ON [dbo].[Sites]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_Employees] ON [dbo].[Employees]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_Calendars] ON [dbo].[Calendars]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_CalendarDays] ON [dbo].[CalendarDays]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_ShiftDefinitions] ON [dbo].[ShiftDefinitions]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_WorkScheduleAssignments] ON [dbo].[WorkScheduleAssignments]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_EventSources] ON [dbo].[EventSources]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_RawAttendanceEvents] ON [dbo].[RawAttendanceEvents]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_RawEventConflicts] ON [dbo].[RawEventConflicts]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_SourceEmployeeMappings] ON [dbo].[SourceEmployeeMappings]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_ObservedRuleVersions] ON [dbo].[ObservedRuleVersions]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_CorrectionPolicies] ON [dbo].[CorrectionPolicies]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_CorrectionRequests] ON [dbo].[CorrectionRequests]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_Revision_CorrectionDecisions] ON [dbo].[CorrectionDecisions]
                AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1) WHERE [Id] = 1;
                    IF @@ROWCOUNT <> 1 THROW 51013, ''Calculation input revision unavailable.'', 1;
                END END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_Companies];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_Sites];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_Employees];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_Calendars];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_CalendarDays];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_ShiftDefinitions];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_WorkScheduleAssignments];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_EventSources];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_RawAttendanceEvents];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_RawEventConflicts];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_SourceEmployeeMappings];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_ObservedRuleVersions];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_CorrectionPolicies];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_CorrectionRequests];");
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Revision_CorrectionDecisions];");
            migrationBuilder.DropTable(
                name: "CalculationInputRevision");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CalculationRuns_Revision",
                table: "DailyCalculationRuns");

            migrationBuilder.DropColumn(
                name: "InputRevision",
                table: "DailyCalculationRuns");
        }
    }
}
