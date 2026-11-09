using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Calculation;

/// <summary>Checks deployed trigger metadata, not its security or every possible write path.</summary>
public static class CalculationMonitorHealth
{
    public const string Present = "present";
    public const string Unverified = "unverified";

    public static async Task<string> InspectAsync(IoDbContext db, CancellationToken ct = default)
    {
        // An app account without permission to inspect trigger definitions must
        // receive "unverified", never a false assertion that monitoring works.
        const string query = """
            SELECT COUNT(*) AS [Value]
            FROM (VALUES
              (N'Companies',N'TR_Revision_Companies'), (N'Sites',N'TR_Revision_Sites'),
              (N'Employees',N'TR_Revision_Employees'), (N'Calendars',N'TR_Revision_Calendars'),
              (N'CalendarDays',N'TR_Revision_CalendarDays'), (N'ShiftDefinitions',N'TR_Revision_ShiftDefinitions'),
              (N'WorkScheduleAssignments',N'TR_Revision_WorkScheduleAssignments'), (N'EventSources',N'TR_Revision_EventSources'),
              (N'RawAttendanceEvents',N'TR_Revision_RawAttendanceEvents'), (N'RawEventConflicts',N'TR_Revision_RawEventConflicts'),
              (N'SourceEmployeeMappings',N'TR_Revision_SourceEmployeeMappings'), (N'ObservedRuleVersions',N'TR_Revision_ObservedRuleVersions'),
              (N'CorrectionPolicies',N'TR_Revision_CorrectionPolicies'), (N'CorrectionRequests',N'TR_Revision_CorrectionRequests'),
              (N'CorrectionDecisions',N'TR_Revision_CorrectionDecisions'),
              (N'CalculationInputRevision',N'TR_CalculationInputRevision_Monotonic')
            ) AS required([TableName], [TriggerName])
            WHERE EXISTS (
              SELECT 1 FROM sys.triggers AS tr
              JOIN sys.tables AS tb ON tb.object_id = tr.parent_id
              JOIN sys.schemas AS sc ON sc.schema_id = tb.schema_id
              WHERE sc.name = N'dbo' AND tb.name = required.[TableName] AND tr.name = required.[TriggerName]
                AND tr.is_disabled = 0
                AND CHARINDEX(N'AFTER INSERT, UPDATE, DELETE', OBJECT_DEFINITION(tr.object_id)) > 0
                AND ((required.[TableName] = N'CalculationInputRevision'
                      AND CHARINDEX(N'i.Revision <> d.Revision + CONVERT(bigint, 1)', OBJECT_DEFINITION(tr.object_id)) > 0
                      AND CHARINDEX(N'THROW 51012', OBJECT_DEFINITION(tr.object_id)) > 0)
                  OR (required.[TableName] <> N'CalculationInputRevision'
                      AND CHARINDEX(N'UPDATE [dbo].[CalculationInputRevision] SET [Revision] = [Revision] + CONVERT(bigint, 1)', OBJECT_DEFINITION(tr.object_id)) > 0
                      AND CHARINDEX(N'THROW 51013', OBJECT_DEFINITION(tr.object_id)) > 0))
            )
            """;
        try
        {
            var found = await db.Database.SqlQueryRaw<int>(query).SingleAsync(ct);
            return found == 16 ? Present : Unverified;
        }
        catch (SqlException e) when (e.Number is 229 or 297)
        {
            return Unverified;
        }
    }
}
