using System.Text.Json;
using IO.Infrastructure.Attendance;
using IO.Infrastructure.Calculation;
using IO.Infrastructure.Security;
using IO.Infrastructure.Periods;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class ObservedCalculationTests
{
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        Guid admin, limited, employee = Guid.NewGuid(), company = Guid.NewGuid(), source = Guid.NewGuid();
        var date = new DateOnly(2027, 5, 5); var midnight = new DateTimeOffset(2027, 5, 5, 0, 0, 0, TimeSpan.Zero);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var role = await db.Roles.SingleAsync(x => x.Name == "Initial administrator");
            admin = await db.UserRoles.Where(x => x.RoleId == role.Id).Select(x => x.UserId).SingleAsync();
            limited = await db.Users.Where(x => x.UserName == "web-manager").Select(x => x.Id).SingleAsync();
            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == ObservedCalculation.Manage);
            if (permission is null) { permission = new() { Id = Guid.NewGuid(), Code = ObservedCalculation.Manage }; db.Permissions.Add(permission); }
            if (!await db.RolePermissions.AnyAsync(x => x.RoleId == role.Id && x.PermissionId == permission.Id))
                db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = permission.Id });
            var org = Guid.NewGuid(); var site = Guid.NewGuid(); var calendar = Guid.NewGuid(); var shift = Guid.NewGuid();
            db.Organizations.Add(new() { Id = org, Name = "calculation synthetic org" });
            db.Companies.Add(new() { Id = company, OrganizationId = org, Name = "calculation synthetic company" });
            db.Sites.Add(new() { Id = site, CompanyId = company, Name = "calculation synthetic site", TimeZoneId = "Etc/UTC" });
            db.Employees.Add(new() { Id = employee, CompanyId = company, PersonnelNumber = "CALC-1", DisplayName = "Synthetic calculation employee" });
            db.EventSources.Add(new() { Id = source, SiteId = site, Name = "calculation synthetic source" });
            db.Calendars.Add(new() { Id = calendar, CompanyId = company, Name = "calculation synthetic calendar" });
            db.CalendarDays.Add(new() { Id = Guid.NewGuid(), CalendarId = calendar, Date = date, Kind = 0, RecordedAtUtc = DateTimeOffset.UtcNow });
            db.ShiftDefinitions.Add(new() { Id = shift, CompanyId = company, SiteId = site, Name = "synthetic fixed shift", StartsAt = new(8, 0), EndsAt = new(16, 0), EndDayOffset = 0 });
            db.WorkScheduleAssignments.Add(new() { Id = Guid.NewGuid(), CompanyId = company, EmployeeId = employee, CalendarId = calendar, ShiftId = shift, ValidFrom = date, ValidUntilExclusive = date.AddDays(1) });
            await db.SaveChangesAsync();
        }
        async Task<T> Calc<T>(Func<ObservedCalculation, Task<T>> f) { await using var scope = provider.CreateAsyncScope(); return await f(scope.ServiceProvider.GetRequiredService<ObservedCalculation>()); }
        async Task<T> Raw<T>(Func<AttendanceIngestion, Task<T>> f) { await using var scope = provider.CreateAsyncScope(); return await f(scope.ServiceProvider.GetRequiredService<AttendanceIngestion>()); }
        async Task<bool> Reject<T>(Func<Task> f) where T : Exception { try { await f(); return false; } catch (T) { return true; } }
        check(await Reject<UnauthorizedAccessException>(() => Calc(s => s.CreateRuleAsync(limited, company, "denied", 0, 0))) &&
              await Reject<ArgumentException>(() => Calc(s => s.CreateRuleAsync(admin, company, "invalid", 721, 0))),
            "calculation policy requires permission and bounded explicit margins");
        var rule = await Calc(s => s.CreateRuleAsync(admin, company, "test paired presence", 10, 0));
        var sourceMapping = await Raw(s => s.AddMappingAsync(admin, source, "person-1", employee, midnight, null));
        var entry = await Raw(s => s.AppendAsync(admin, source, "in-1", "person-1", midnight.AddHours(7).AddMinutes(55), null, "in", [1]));
        var exit = await Raw(s => s.AppendAsync(admin, source, "out-1", "person-1", midnight.AddHours(16), null, "out", [2]));
        var first = await Calc(s => s.CalculateAsync(admin, employee, date, source, rule));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var run = await db.DailyCalculationRuns.SingleAsync(x => x.Id == first);
            using var snapshot = JsonDocument.Parse(run.InputSnapshotJson);
            check(run.ObservedTicks == TimeSpan.FromMinutes(485).Ticks && run.AnomaliesJson == "[]" &&
                  run.WindowEndUtc == midnight.AddHours(16).AddTicks(1) && snapshot.RootElement.GetProperty("Events").GetArrayLength() == 2 &&
                  await scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().SecurityAudit.AnyAsync(x => x.Action == "calculation.run.created:" + first.ToString("N")),
                "calculation resolves schedule, exact boundary, mapped punches and audit");
        }
        var baselineStatus = JsonSerializer.SerializeToElement(await Calc(s => s.AssessDatabaseChangesAsync(admin, first)));
        check(baselineStatus.GetProperty("databaseChange").GetString() == "no-monitored-database-change-detected" &&
              baselineStatus.GetProperty("freshness").GetString() == "unknown" && !baselineStatus.GetProperty("approvable").GetBoolean() &&
              baselineStatus.GetProperty("InputRevision").GetInt64() == baselineStatus.GetProperty("CurrentRevision").GetInt64(),
              "revision baseline detects no monitored database write but never declares freshness");
        await Raw(s => s.AppendAsync(admin, source, "in-1", "person-1", midnight.AddHours(7).AddMinutes(55), null, "in", [1]));
        var replayStatus = JsonSerializer.SerializeToElement(await Calc(s => s.AssessDatabaseChangesAsync(admin, first)));
        check(replayStatus.GetProperty("databaseChange").GetString() == "no-monitored-database-change-detected" &&
              await Reject<UnauthorizedAccessException>(() => Calc(s => s.AssessDatabaseChangesAsync(limited, first))) &&
              await Reject<ArgumentException>(() => Calc(s => s.AssessDatabaseChangesAsync(admin, Guid.NewGuid()))),
              "identical raw retry does not change revision and assessment requires permission");
        // Metadata is checked both at capture and assessment. Restoring a trigger
        // does not reconstruct writes that happened while it was disabled.
        Guid capturedUnverified;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            await db.Database.ExecuteSqlRawAsync("DISABLE TRIGGER dbo.TR_Revision_Employees ON dbo.Employees");
            try
            {
                var status = JsonSerializer.SerializeToElement(await Calc(s => s.AssessDatabaseChangesAsync(admin, first)));
                capturedUnverified = await Calc(s => s.CalculateAsync(admin, employee, date, source, rule));
                check(status.GetProperty("databaseChange").GetString() == "monitor-unverified" &&
                      status.GetProperty("currentMonitor").GetString() == "unverified" &&
                      !status.GetProperty("approvable").GetBoolean(),
                    "disabled input trigger makes the current monitor unverified");
            }
            finally { await db.Database.ExecuteSqlRawAsync("ENABLE TRIGGER dbo.TR_Revision_Employees ON dbo.Employees"); }
        }
        var unverifiedCapture = JsonSerializer.SerializeToElement(await Calc(s => s.AssessDatabaseChangesAsync(admin, capturedUnverified)));
        check(unverifiedCapture.GetProperty("databaseChange").GetString() == "unverified-at-capture" &&
              unverifiedCapture.GetProperty("currentMonitor").GetString() == "present" &&
              !unverifiedCapture.GetProperty("approvable").GetBoolean(),
              "restoring a trigger does not validate a run captured while monitoring was unverified");
        var legacyId = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var original = await db.DailyCalculationRuns.AsNoTracking().SingleAsync(x => x.Id == first);
            db.DailyCalculationRuns.Add(new() { Id = legacyId, CompanyId = original.CompanyId, EmployeeId = original.EmployeeId,
                RuleVersionId = original.RuleVersionId, SourceId = original.SourceId, ScheduleAssignmentId = original.ScheduleAssignmentId,
                WorkDate = original.WorkDate, WindowStartUtc = original.WindowStartUtc, WindowEndUtc = original.WindowEndUtc,
                InputSnapshotJson = original.InputSnapshotJson, AnomaliesJson = original.AnomaliesJson, ObservedTicks = original.ObservedTicks,
                InputRevision = null, CreatedAtUtc = original.CreatedAtUtc.AddTicks(-1), CreatedByUserId = original.CreatedByUserId });
            await db.SaveChangesAsync();
            var noDelete = false; var noReset = false;
            try { await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.CalculationInputRevision WHERE Id = 1"); }
            catch (SqlException e) when (e.Number == 51012) { noDelete = true; }
            try { await db.Database.ExecuteSqlRawAsync("UPDATE dbo.CalculationInputRevision SET Revision = 0 WHERE Id = 1"); }
            catch (SqlException e) when (e.Number == 51012) { noReset = true; }
            await db.Database.OpenConnectionAsync();
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sys.triggers WHERE name LIKE N'TR[_]Revision[_]%' AND is_disabled = 0";
            var triggerCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            check(noDelete && noReset && triggerCount == 15 && CalculationInputRevisionStatus.Evaluate(3, null, "present", "present") == "unavailable" &&
                CalculationInputRevisionStatus.Evaluate(3, 2, "present", "present") == "inconsistent", "revision singleton is monotonic and monitored table triggers are enabled");
        }
        var legacyStatus = JsonSerializer.SerializeToElement(await Calc(s => s.AssessDatabaseChangesAsync(admin, legacyId)));
        check(legacyStatus.GetProperty("databaseChange").GetString() == "untracked" && !legacyStatus.GetProperty("approvable").GetBoolean(),
            "historical runs without a revision baseline remain untracked");
        // Hold a real uncommitted input write and revision update. The calculation
        // must wait, then capture both the committed event and the matching epoch.
        var racingEvent = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var writerDb = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            await using var writer = await writerDb.Database.BeginTransactionAsync();
            writerDb.RawAttendanceEvents.Add(new() { Id = racingEvent, SourceId = source, ExternalEventId = "race-uncommitted",
                SourceEmployeeIdentifier = "person-1", OccurredAtUtc = midnight.AddHours(9), ReceivedAtUtc = DateTimeOffset.UtcNow,
                ReportedEventType = "in", SourcePayload = [5] });
            await writerDb.SaveChangesAsync();
            var racingRun = Calc(s => s.CalculateAsync(admin, employee, date, source, rule));
            var waiting = await Task.WhenAny(racingRun, Task.Delay(300)) != racingRun;
            await writer.CommitAsync();
            var racingRunId = await racingRun;
            var status = JsonSerializer.SerializeToElement(await Calc(s => s.AssessDatabaseChangesAsync(admin, racingRunId)));
            await using var reader = provider.CreateAsyncScope();
            var run = await reader.ServiceProvider.GetRequiredService<IoDbContext>().DailyCalculationRuns.SingleAsync(x => x.Id == racingRunId);
            using var snap = JsonDocument.Parse(run.InputSnapshotJson);
            check(waiting && status.GetProperty("databaseChange").GetString() == "no-monitored-database-change-detected" &&
                  snap.RootElement.GetProperty("Events").EnumerateArray().Any(x => x.GetProperty("Id").GetGuid() == racingEvent),
                  "calculation waits for uncommitted input and captures the committed event with its revision");
        }
        await Raw(s => s.AppendAsync(admin, source, "extra", "person-1", midnight.AddHours(12), null, "in", [3]));
        var changedStatus = JsonSerializer.SerializeToElement(await Calc(s => s.AssessDatabaseChangesAsync(admin, first)));
        check(changedStatus.GetProperty("databaseChange").GetString() == "monitored-database-changed" &&
              changedStatus.GetProperty("CurrentRevision").GetInt64() > changedStatus.GetProperty("InputRevision").GetInt64(),
              "new raw event invalidates the previous database revision baseline");
        var second = await Calc(s => s.CalculateAsync(admin, employee, date, source, rule));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var old = await db.DailyCalculationRuns.SingleAsync(x => x.Id == first);
            var next = await db.DailyCalculationRuns.SingleAsync(x => x.Id == second);
            check(old.ObservedTicks == TimeSpan.FromMinutes(485).Ticks && next.ObservedTicks is null && next.AnomaliesJson.Contains("invalid-punch-order", StringComparison.Ordinal) &&
                  await db.RawAttendanceEvents.CountAsync(x => x.Id == entry.EventId || x.Id == exit.EventId) == 2,
                "recalculation preserves previous run and raw input while incomplete pairing fails closed");
            var runBlocked = false; var ruleBlocked = false;
            try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.DailyCalculationRuns WHERE Id = {first}"); }
            catch (SqlException e) when (e.Number == 51005) { runBlocked = true; }
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.ObservedRuleVersions SET Name = {'x'.ToString()} WHERE Id = {rule}"); }
            catch (SqlException e) when (e.Number == 51004) { ruleBlocked = true; }
            check(runBlocked && ruleBlocked, "SQL blocks mutation of calculation runs and rule versions");
        }
        var compared = JsonSerializer.SerializeToElement(await Calc(s => s.CompareAsync(admin, first, second)));
        var same = JsonSerializer.SerializeToElement(await Calc(s => s.CompareAsync(admin, first, first)));
        check(compared.GetProperty("snapshotDifferent").GetBoolean() && compared.GetProperty("resultDifferent").GetBoolean() &&
              !same.GetProperty("snapshotDifferent").GetBoolean() && !same.GetProperty("approvable").GetBoolean() &&
              same.GetProperty("freshness").GetString() == "unknown", "historical run comparison detects differences without asserting freshness");
        check(await Reject<UnauthorizedAccessException>(() => Calc(s => s.CompareAsync(limited, first, second))) &&
              await Reject<ArgumentException>(() => Calc(s => s.CompareAsync(admin, first, Guid.NewGuid()))),
              "historical run comparison requires permission and valid identities");
        var unknown = await Raw(s => s.AppendAsync(admin, source, "unknown", "unmapped", midnight.AddHours(10), null, "in", [4]));
        var third = await Calc(s => s.CalculateAsync(admin, employee, date, source, rule));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var run = await db.DailyCalculationRuns.SingleAsync(x => x.Id == third);
            check(run.ObservedTicks is null && run.AnomaliesJson.Contains("unresolved-source-events", StringComparison.Ordinal) &&
                  run.InputSnapshotJson.Contains(unknown.EventId.ToString(), StringComparison.OrdinalIgnoreCase),
                "unmapped source event prevents numeric result and remains in snapshot");
        }
        var collision = await Raw(s => s.AppendAsync(admin, source, "in-1", "person-1", midnight.AddHours(7).AddMinutes(55), null, "in", [99]));
        var beforeNote = await Calc(s => s.CalculateAsync(admin, employee, date, source, rule));
        await Raw(s => s.AddConflictNoteAsync(admin, collision.ConflictId!.Value, "reviewed but not resolved"));
        var noteStatus = JsonSerializer.SerializeToElement(await Calc(s => s.AssessDatabaseChangesAsync(admin, beforeNote)));
        check(noteStatus.GetProperty("databaseChange").GetString() == "no-monitored-database-change-detected",
            "review notes alone do not change the monitored calculation input revision");
        var afterNote = await Calc(s => s.CalculateAsync(admin, employee, date, source, rule));
        await using (var scope = provider.CreateAsyncScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<IoDbContext>().DailyCalculationRuns.SingleAsync(x => x.Id == afterNote);
            check(run.ObservedTicks is null && run.AnomaliesJson.Contains("source-has-conflicts", StringComparison.Ordinal),
                "conflict note does not suppress calculation anomaly");
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var site = Guid.NewGuid(); var shift = Guid.NewGuid();
            var calendar = (await db.WorkScheduleAssignments.SingleAsync(x => x.EmployeeId == employee)).CalendarId;
            db.Sites.Add(new() { Id = site, CompanyId = company, Name = "adjacent other site", TimeZoneId = "Etc/UTC" });
            db.ShiftDefinitions.Add(new() { Id = shift, CompanyId = company, SiteId = site, Name = "adjacent overnight", StartsAt = new(23, 0), EndsAt = new(8, 0), EndDayOffset = 1 });
            db.WorkScheduleAssignments.Add(new() { Id = Guid.NewGuid(), CompanyId = company, EmployeeId = employee,
                CalendarId = calendar, ShiftId = shift, ValidFrom = date.AddDays(-1), ValidUntilExclusive = date });
            await db.SaveChangesAsync();
        }
        var overlapping = await Calc(s => s.CalculateAsync(admin, employee, date, source, rule));
        await using (var scope = provider.CreateAsyncScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<IoDbContext>().DailyCalculationRuns.SingleAsync(x => x.Id == overlapping);
            check(run.ObservedTicks is null && run.AnomaliesJson.Contains("overlapping-work-windows", StringComparison.Ordinal),
                "overlapping adjacent shifts at different sites block a numerical result");
        }
        check(await Reject<UnauthorizedAccessException>(() => Calc(s => s.CalculateAsync(limited, employee, date, source, rule))) &&
              await Reject<UnauthorizedAccessException>(() => Calc(s => s.ListRunsAsync(limited, employee, 0))) &&
              await Reject<ArgumentException>(() => Calc(s => s.CalculateAsync(admin, employee, date, Guid.NewGuid(), rule))),
              "calculation execution and results enforce permission and source identity");
        var concurrent = await Task.WhenAll(Calc(s => s.CalculateAsync(admin, employee, date, source, rule)), Calc(s => s.CalculateAsync(admin, employee, date, source, rule)));
        check(concurrent.Distinct().Count() == 2, "concurrent recomputations preserve distinct run history");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var role = await db.Roles.SingleAsync(x => x.Name == "Initial administrator");
            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == PeriodPreflight.Read);
            if (permission is null) { permission = new() { Id = Guid.NewGuid(), Code = PeriodPreflight.Read }; db.Permissions.Add(permission); }
            if (!await db.RolePermissions.AnyAsync(x => x.RoleId == role.Id && x.PermissionId == permission.Id))
                db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = permission.Id });
            await db.SaveChangesAsync();
        }
        async Task<T> Period<T>(Func<PeriodPreflight, Task<T>> f) { await using var scope = provider.CreateAsyncScope(); return await f(scope.ServiceProvider.GetRequiredService<PeriodPreflight>()); }
        var report = JsonSerializer.SerializeToElement(await Period(s => s.InspectAsync(admin, employee, date, date.AddDays(2))));
        var days = report.GetProperty("days");
        check(!report.GetProperty("approvable").GetBoolean() && report.GetProperty("freshness").GetString() == "unknown" &&
              days.GetArrayLength() == 2 && days[0].GetProperty("latestRun").ValueKind == JsonValueKind.Object &&
              days[1].GetProperty("latestRun").ValueKind == JsonValueKind.Null,
              "period preflight displays historical coverage without claiming freshness or approval");
        var replacementEmployee = Guid.NewGuid(); byte[] mappingVersion;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            db.Employees.Add(new() { Id = replacementEmployee, CompanyId = company, PersonnelNumber = "CALC-REMAP", DisplayName = "Synthetic remapped employee" });
            await db.SaveChangesAsync();
            mappingVersion = (await db.SourceEmployeeMappings.SingleAsync(x => x.Id == sourceMapping)).Version;
        }
        var beforeRemap = await Calc(s => s.CalculateAsync(admin, employee, date, source, rule));
        await Raw(s => s.ReplaceMappingAsync(admin, source, "person-1", sourceMapping, replacementEmployee, midnight.AddHours(12), mappingVersion, "historical correction"));
        var remapStatus = JsonSerializer.SerializeToElement(await Calc(s => s.AssessDatabaseChangesAsync(admin, beforeRemap)));
        check(remapStatus.GetProperty("databaseChange").GetString() == "monitored-database-changed",
            "mapping replacement advances the monitored database input revision");
        var afterRemap = await Calc(s => s.CalculateAsync(admin, employee, date, source, rule));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var priorRun = await db.DailyCalculationRuns.SingleAsync(x => x.Id == first);
            var newRun = await db.DailyCalculationRuns.SingleAsync(x => x.Id == afterRemap);
            using var newSnapshot = JsonDocument.Parse(newRun.InputSnapshotJson);
            var exitEvent = newSnapshot.RootElement.GetProperty("Events").EnumerateArray().Single(x => x.GetProperty("Id").GetGuid() == exit.EventId);
            check(priorRun.ObservedTicks == TimeSpan.FromMinutes(485).Ticks && newRun.ObservedTicks is null &&
                  exitEvent.GetProperty("MappedEmployeeId").GetGuid() == replacementEmployee &&
                  await db.RawAttendanceEvents.AnyAsync(x => x.Id == exit.EventId),
                  "mapping replacement affects recomputation while preserving prior snapshot and raw evidence");
        }
        check(await Reject<UnauthorizedAccessException>(() => Period(s => s.InspectAsync(limited, employee, date, date.AddDays(1)))) &&
              await Reject<ArgumentException>(() => Period(s => s.InspectAsync(admin, employee, date, date.AddDays(63)))) &&
              await Reject<ArgumentException>(() => Period(s => s.InspectAsync(admin, employee, date, date))),
              "period preflight requires explicit permission and a bounded nonempty interval");
        var reportActor=Guid.NewGuid(); var otherReportActor=Guid.NewGuid(); var unlinkedActor=Guid.NewGuid(); var reportRole=Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==PersonalReports.ReadSelf);
            if(p is null){p=new PermissionRecord{Id=Guid.NewGuid(),Code=PersonalReports.ReadSelf};db.Permissions.Add(p);}
            db.Roles.Add(new RoleRecord{Id=reportRole,Name="synthetic personal report role"});
            db.Users.AddRange(new UserRecord{Id=reportActor,UserName="synthetic report actor",EmployeeId=employee,IsActive=true},
                new UserRecord{Id=otherReportActor,UserName="synthetic other report actor",EmployeeId=replacementEmployee,IsActive=true},
                new UserRecord{Id=unlinkedActor,UserName="synthetic unlinked report actor",IsActive=true});
            db.RolePermissions.Add(new RolePermissionRecord{RoleId=reportRole,PermissionId=p.Id});
            db.UserRoles.AddRange(new UserRoleRecord{UserId=reportActor,RoleId=reportRole},new UserRoleRecord{UserId=otherReportActor,RoleId=reportRole},new UserRoleRecord{UserId=unlinkedActor,RoleId=reportRole});
            await db.SaveChangesAsync();
        }
        async Task<PersonalReports.Report> OwnReport(Guid who, DateOnly start, DateOnly until)
        { await using var scope=provider.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<PersonalReports>().InspectAsync(who,start,until); }
        var personalReport=await OwnReport(reportActor,date,date.AddDays(2));
        var otherReport=await OwnReport(otherReportActor,date,date.AddDays(1));
        check(personalReport.Days.Count==2 && personalReport.Days[0].LatestRun?.Id==afterRemap && personalReport.Days[1].LatestRun is null &&
              !personalReport.Approvable && personalReport.Freshness=="unknown" && otherReport.Days[0].LatestRun is null,
              "personal report shows only linked employee latest historical runs and empty days");
        check(await Reject<UnauthorizedAccessException>(()=>OwnReport(limited,date,date.AddDays(1))) &&
              await Reject<UnauthorizedAccessException>(()=>OwnReport(unlinkedActor,date,date.AddDays(1))) &&
              await Reject<ArgumentException>(()=>OwnReport(reportActor,date,date.AddDays(63))) &&
              await Reject<ArgumentException>(()=>OwnReport(reportActor,date,date)),
              "personal report requires explicit permission, linked employee and bounded interval");

    }
}
