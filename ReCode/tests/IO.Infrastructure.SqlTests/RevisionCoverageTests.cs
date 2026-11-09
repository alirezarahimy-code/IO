using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class RevisionCoverageTests
{
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
        var initial = await db.CalculationInputRevisions.AsNoTracking().Where(x => x.Id == 1).Select(x => x.Revision).SingleAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        // All writes use disposable synthetic fixtures and roll back. Every
        // statement must advance the coarse global epoch, even a no-op update.
        var newRequest = Guid.NewGuid();
        var statements = new (string Name, string Sql)[]
        {
            ("Companies", "UPDATE dbo.Companies SET Name = Name WHERE Id = (SELECT TOP (1) Id FROM dbo.Companies)"),
            ("Sites", "UPDATE dbo.Sites SET TimeZoneId = TimeZoneId WHERE Id = (SELECT TOP (1) Id FROM dbo.Sites)"),
            ("Employees", "UPDATE dbo.Employees SET DisplayName = DisplayName WHERE Id = (SELECT TOP (1) Id FROM dbo.Employees)"),
            ("Calendars", "UPDATE dbo.Calendars SET Name = Name WHERE Id = (SELECT TOP (1) Id FROM dbo.Calendars)"),
            ("CalendarDays", "UPDATE dbo.CalendarDays SET Kind = Kind WHERE Id = (SELECT TOP (1) Id FROM dbo.CalendarDays)"),
            ("ShiftDefinitions", "UPDATE dbo.ShiftDefinitions SET StartsAt = StartsAt WHERE Id = (SELECT TOP (1) Id FROM dbo.ShiftDefinitions)"),
            ("WorkScheduleAssignments", "UPDATE dbo.WorkScheduleAssignments SET ValidFrom = ValidFrom WHERE Id = (SELECT TOP (1) Id FROM dbo.WorkScheduleAssignments)"),
            ("EventSources", "UPDATE dbo.EventSources SET Name = Name WHERE Id = (SELECT TOP (1) Id FROM dbo.EventSources)"),
            ("SourceEmployeeMappings", "UPDATE dbo.SourceEmployeeMappings SET ValidFromUtc = ValidFromUtc WHERE Id = (SELECT TOP (1) Id FROM dbo.SourceEmployeeMappings)"),
            ("RawAttendanceEvents", "INSERT INTO dbo.RawAttendanceEvents (Id, SourceId, ExternalEventId, SourceEmployeeIdentifier, OccurredAtUtc, ReceivedAtUtc, SourceTimeZoneId, ReportedEventType, SourcePayload) SELECT TOP (1) NEWID(), SourceId, NULL, SourceEmployeeIdentifier, OccurredAtUtc, ReceivedAtUtc, SourceTimeZoneId, ReportedEventType, SourcePayload FROM dbo.RawAttendanceEvents"),
            ("RawEventConflicts", "INSERT INTO dbo.RawEventConflicts (Id, OriginalEventId, SourceId, ExternalEventId, SourceEmployeeIdentifier, OccurredAtUtc, ReceivedAtUtc, SourceTimeZoneId, ReportedEventType, SourcePayload) SELECT TOP (1) NEWID(), OriginalEventId, SourceId, ExternalEventId, SourceEmployeeIdentifier, OccurredAtUtc, ReceivedAtUtc, SourceTimeZoneId, ReportedEventType, SourcePayload FROM dbo.RawEventConflicts"),
            ("ObservedRuleVersions", "INSERT INTO dbo.ObservedRuleVersions (Id, CompanyId, Name, Algorithm, BeforeMinutes, AfterMinutes, CreatedAtUtc, CreatedByUserId) SELECT TOP (1) NEWID(), CompanyId, Name, Algorithm, BeforeMinutes, AfterMinutes, CreatedAtUtc, CreatedByUserId FROM dbo.ObservedRuleVersions"),
            ("CorrectionPolicies", "INSERT INTO dbo.CorrectionPolicies (Id, CompanyId, Name, ApproverUserId, CreatedByUserId, CreatedAtUtc) SELECT TOP (1) NEWID(), CompanyId, Name, ApproverUserId, CreatedByUserId, CreatedAtUtc FROM dbo.CorrectionPolicies"),
            ("CorrectionRequests", $"INSERT INTO dbo.CorrectionRequests (Id, CompanyId, EmployeeId, RequesterUserId, PolicyId, WorkDate, ProposedAtUtc, PunchKind, Reason, State, CreatedAtUtc) SELECT TOP (1) '{newRequest:D}', CompanyId, EmployeeId, RequesterUserId, PolicyId, WorkDate, ProposedAtUtc, PunchKind, Reason, 0, CreatedAtUtc FROM dbo.CorrectionRequests"),
            ("CorrectionDecisions", $"INSERT INTO dbo.CorrectionDecisions (Id, RequestId, ActorUserId, Decision, Reason, AtUtc) SELECT TOP (1) NEWID(), '{newRequest:D}', ActorUserId, Decision, Reason, AtUtc FROM dbo.CorrectionDecisions")
        };
        var current = initial;
        var failures = new List<string>();
        foreach (var (name, sql) in statements)
        {
            await db.Database.ExecuteSqlRawAsync(sql);
            var next = await db.CalculationInputRevisions.AsNoTracking().Where(x => x.Id == 1).Select(x => x.Revision).SingleAsync();
            if (next != current + 1) failures.Add(name);
            current = next;
        }
        await tx.RollbackAsync();
        var afterRollback = await db.CalculationInputRevisions.AsNoTracking().Where(x => x.Id == 1).Select(x => x.Revision).SingleAsync();
        check(statements.Length == 15 && failures.Count == 0 && afterRollback == initial,
            "all monitored input tables advance the transactional revision; rollback restores it: " + string.Join(",", failures));
    }
}
