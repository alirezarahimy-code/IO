using System.Text.Json;
using Microsoft.Data.SqlClient;
using IO.Domain.Attendance;
using IO.Infrastructure.Attendance;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class AttendanceIngestionTests
{
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        Guid admin, limited;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var role = await db.Roles.SingleAsync(x => x.Name == "Initial administrator");
            admin = await db.UserRoles.Where(x => x.RoleId == role.Id).Select(x => x.UserId).SingleAsync();
            limited = await db.Users.Where(x => x.UserName == "web-manager").Select(x => x.Id).SingleAsync();
            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == AttendanceIngestion.Manage);
            if (permission is null) { permission = new PermissionRecord { Id = Guid.NewGuid(), Code = AttendanceIngestion.Manage }; db.Permissions.Add(permission); }
            if (!await db.RolePermissions.AnyAsync(x => x.RoleId == role.Id && x.PermissionId == permission.Id))
                db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = permission.Id });
            await db.SaveChangesAsync();
        }
        async Task<T> Use<T>(Func<AttendanceIngestion, Task<T>> f)
        {
            await using var scope = provider.CreateAsyncScope();
            return await f(scope.ServiceProvider.GetRequiredService<AttendanceIngestion>());
        }
        var source = await Use(s => s.CreateSourceAsync(admin, null, "synthetic ingestion"));
        var time = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        Task<AttendanceIngestion.Receipt> Append(string? id, byte[] payload) =>
            Use(s => s.AppendAsync(admin, source, id, "test-person", time, "Asia/Tehran", "in", payload));
        bool denied = false;
        try { await Use(s => s.AppendAsync(limited, source, "x", null, null, null, null, [])); }
        catch (UnauthorizedAccessException) { denied = true; }
        check(denied, "raw ingestion requires explicit permission");
        var concurrent = await Task.WhenAll(Append("concurrent", [1, 2]), Append("concurrent", [1, 2]));
        check(concurrent.Count(x => x.Outcome == RawEventAppendResult.Appended) == 1 &&
            concurrent.Count(x => x.Outcome == RawEventAppendResult.AlreadyRecorded) == 1 && concurrent[0].EventId == concurrent[1].EventId,
            "concurrent identical raw event retries produce one original");
        var conflict = await Append("concurrent", [1, 3]);
        check(conflict.Outcome == RawEventAppendResult.ConflictingExternalIdentity && conflict.EventId == concurrent[0].EventId,
            "same external identity with different payload reports conflict");
        check(conflict.ConflictId is not null, "conflicting receipt has a separate review identity");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var preserved = await db.RawEventConflicts.SingleAsync(x => x.Id == conflict.ConflictId);
            var original = await db.RawAttendanceEvents.SingleAsync(x => x.Id == conflict.EventId);
            check(preserved.SourcePayload.SequenceEqual(new byte[] { 1, 3 }) && original.SourcePayload.SequenceEqual(new byte[] { 1, 2 }) &&
                  preserved.OriginalEventId == original.Id, "conflict payload preserved separately from immutable original");
            var updateBlocked = false; var deleteBlocked = false;
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.RawEventConflicts SET ReportedEventType = {'x'.ToString()} WHERE Id = {preserved.Id}"); }
            catch (SqlException e) when (e.Number == 51003) { updateBlocked = true; }
            try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.RawEventConflicts WHERE Id = {preserved.Id}"); }
            catch (SqlException e) when (e.Number == 51003) { deleteBlocked = true; }
            check(updateBlocked && deleteBlocked, "SQL blocks updates and deletes of conflict receipts");
        }
        var noId1 = await Append(null, [7]); var noId2 = await Append(null, [7]);
        check(noId1.Outcome == RawEventAppendResult.IdentityRequiresReview && noId2.Outcome == RawEventAppendResult.IdentityRequiresReview && noId1.EventId != noId2.EventId,
            "unidentified events are retained independently for review");
        var upper = await Append("Exact", [1]); var lower = await Append("exact", [1]);
        var trailing = await Append("Exact ", [1]); var nul = await Append("Exact\0", [1]);
        check(new[] { upper, lower, trailing, nul }.All(x => x.Outcome == RawEventAppendResult.Appended) &&
            (await Append("Exact\0", [1])).Outcome == RawEventAppendResult.AlreadyRecorded,
            "external identity uses exact UTF-16 including case, spaces and trailing NUL");
        Guid site, employee, foreignEmployee;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var pair = await (from e in db.Employees join siteRecord in db.Sites on e.CompanyId equals siteRecord.CompanyId
                select new { Employee = e.Id, Site = siteRecord.Id, e.CompanyId }).FirstAsync();
            site = pair.Site; employee = pair.Employee;
            foreignEmployee = await db.Employees.Where(x => x.CompanyId != pair.CompanyId).Select(x => x.Id).FirstAsync();
        }
        var mappedSource = await Use(s => s.CreateSourceAsync(admin, site, "mapping synthetic source"));
        async Task<bool> Reject<T>(Func<Task> action) where T : Exception
        { try { await action(); return false; } catch (T) { return true; } }
        check(await Reject<UnauthorizedAccessException>(() => Use(s => s.AddMappingAsync(limited, mappedSource, "P1", employee, time, null))) &&
              await Reject<ArgumentException>(() => Use(s => s.AddMappingAsync(admin, mappedSource, "P1", foreignEmployee, time, null))) &&
              await Reject<ArgumentException>(() => Use(s => s.AddMappingAsync(admin, source, "P1", employee, time, null))) &&
              await Reject<ArgumentException>(() => Use(s => s.AddMappingAsync(admin, mappedSource, "P1", employee, time, time))),
              "mapping requires permission, same-company site and valid interval");
        async Task<Guid?> TryMapping()
        {
            try { return await Use(s => s.AddMappingAsync(admin, mappedSource, "P1", employee, time, time.AddHours(1))); }
            catch (DomainConflictException) { return null; }
        }
        var attempts = await Task.WhenAll(TryMapping(), TryMapping());
        check(attempts.Count(x => x.HasValue) == 1, "concurrent overlapping source mappings serialize");
        var openMapping = await Use(s => s.AddMappingAsync(admin, mappedSource, "P1", employee, time.AddHours(1), null));
        var before = await Use(s => s.AppendAsync(admin, mappedSource, "before", "P1", time.AddSeconds(-1), null, "in", []));
        var atStart = await Use(s => s.AppendAsync(admin, mappedSource, "start", "P1", time, null, "in", []));
        var boundary = await Use(s => s.AppendAsync(admin, mappedSource, "boundary", "P1", time.AddHours(1), null, "out", []));
        var differentCase = await Use(s => s.AppendAsync(admin, mappedSource, "case", "p1", time, null, "in", []));
        var missingTime = await Use(s => s.AppendAsync(admin, mappedSource, "missing", "P1", null, null, "in", []));
        using var resolution = JsonSerializer.SerializeToDocument(await Use(s => s.ListResolutionAsync(admin, mappedSource, 0)));
        var items = resolution.RootElement.GetProperty("items").EnumerateArray().ToDictionary(x => x.GetProperty("Id").GetGuid());
        check(items[before.EventId].GetProperty("Status").GetString() == "requires-review" &&
              items[atStart.EventId].GetProperty("EmployeeId").GetGuid() == employee &&
              items[boundary.EventId].GetProperty("EmployeeId").GetGuid() == employee &&
              items[differentCase.EventId].GetProperty("Status").GetString() == "requires-review" &&
              items[missingTime.EventId].GetProperty("Status").GetString() == "requires-review",
              "mapping resolves exact identifiers and half-open UTC boundaries without guessing");
        var mappings = JsonSerializer.SerializeToElement(await Use(s => s.ListMappingsAsync(admin, mappedSource, 0)));
        check(mappings.GetProperty("items").GetArrayLength() == 2 &&
              await Reject<UnauthorizedAccessException>(() => Use(s => s.ListMappingsAsync(limited, mappedSource, 0))) &&
              await Reject<ArgumentException>(() => Use(s => s.ListMappingsAsync(admin, mappedSource, -1))),
              "source mapping history is paginated and restricted to ingestion operators");
        check(await Reject<UnauthorizedAccessException>(() => Use(s => s.ListResolutionAsync(limited, mappedSource, 0))) &&
              await Reject<UnauthorizedAccessException>(() => Use(s => s.GetConflictAsync(limited, conflict.ConflictId!.Value))) &&
              (await Use(s => s.GetConflictAsync(admin, conflict.ConflictId!.Value))) is not null,
              "mapping review and conflict payload require explicit permission");
        var newEmployee = Guid.NewGuid(); byte[] version;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var company = await db.Employees.Where(x => x.Id == employee).Select(x => x.CompanyId).SingleAsync();
            db.Employees.Add(new() { Id = newEmployee, CompanyId = company, PersonnelNumber = "MAPPING-NEW", DisplayName = "New synthetic mapped employee" });
            await db.SaveChangesAsync();
            version = (await db.SourceEmployeeMappings.SingleAsync(x => x.Id == openMapping)).Version;
        }
        var cutoff = time.AddHours(2);
        check(await Reject<UnauthorizedAccessException>(() => Use(s => s.ReplaceMappingAsync(limited, mappedSource, "P1", openMapping, newEmployee, cutoff, version, "denied"))) &&
              await Reject<ArgumentException>(() => Use(s => s.ReplaceMappingAsync(admin, mappedSource, "p1", openMapping, newEmployee, cutoff, version, "wrong case"))) &&
              await Reject<ArgumentException>(() => Use(s => s.ReplaceMappingAsync(admin, mappedSource, "P1", openMapping, foreignEmployee, cutoff, version, "wrong company"))) &&
              await Reject<ArgumentException>(() => Use(s => s.ReplaceMappingAsync(admin, mappedSource, "P1", openMapping, newEmployee, time, version, "bad boundary"))) &&
              await Reject<DomainConflictException>(() => Use(s => s.ReplaceMappingAsync(admin, mappedSource, "P1", openMapping, newEmployee, cutoff, new byte[8], "stale"))),
              "mapping replacement validates permission, exact identity, company, boundary and rowversion");
        async Task<AttendanceIngestion.MappingReplacement?> TryReplacement()
        {
            try { return await Use(s => s.ReplaceMappingAsync(admin, mappedSource, "P1", openMapping, newEmployee, cutoff, version, "synthetic transfer")); }
            catch (DomainConflictException) { return null; }
        }
        var replacements = await Task.WhenAll(TryReplacement(), TryReplacement());
        var changed = replacements.Single(x => x is not null)!;
        check(replacements.Count(x => x is not null) == 1, "competing mapping replacements allow exactly one winner");
        var atCutoff = await Use(s => s.AppendAsync(admin, mappedSource, "new-boundary", "P1", cutoff, null, "in", []));
        using var afterReplacement = JsonSerializer.SerializeToDocument(await Use(s => s.ListResolutionAsync(admin, mappedSource, 0)));
        var resolved = afterReplacement.RootElement.GetProperty("items").EnumerateArray().ToDictionary(x => x.GetProperty("Id").GetGuid());
        check(resolved[boundary.EventId].GetProperty("EmployeeId").GetGuid() == employee &&
              resolved[atCutoff.EventId].GetProperty("EmployeeId").GetGuid() == newEmployee,
              "mapping replacement preserves previous interval and uses new employee at the half-open boundary");
        var mappingChanges = JsonSerializer.SerializeToElement(await Use(s => s.ListMappingChangesAsync(admin, mappedSource, 0)));
        check(mappingChanges.GetProperty("items").GetArrayLength() == 1 &&
              await Reject<UnauthorizedAccessException>(() => Use(s => s.ListMappingChangesAsync(limited, mappedSource, 0))),
              "mapping change history is restricted and includes the recorded replacement");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var blocked = false;
            try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.SourceMappingChanges WHERE Id = {changed.ChangeId}"); }
            catch (SqlException e) when (e.Number == 51011) { blocked = true; }
            check(blocked && await db.SourceEmployeeMappings.AnyAsync(x => x.Id == openMapping && x.ValidUntilUtcExclusive == cutoff) &&
                await db.SourceEmployeeMappings.AnyAsync(x => x.Id == changed.NewMappingId && x.ValidFromUtc == cutoff && x.EmployeeId == newEmployee) &&
                await scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().SecurityAudit.AnyAsync(x => x.Action == "attendance.mapping.replaced:" + changed.ChangeId.ToString("N")),
                "mapping replacement is transactional, audited and its history is immutable");
        }
        var conflictId = conflict.ConflictId!.Value;
        check(await Reject<UnauthorizedAccessException>(() => Use(s => s.AddConflictNoteAsync(limited, conflictId, "denied"))) &&
              await Reject<ArgumentException>(() => Use(s => s.AddConflictNoteAsync(admin, conflictId, " "))) &&
              await Reject<ArgumentException>(() => Use(s => s.AddConflictNoteAsync(admin, Guid.NewGuid(), "unknown"))),
              "conflict notes require permission, valid text and an existing conflict");
        var noteId = await Use(s => s.AddConflictNoteAsync(admin, conflictId, "synthetic review note"));
        var notes = JsonSerializer.SerializeToElement(await Use(s => s.ListConflictNotesAsync(admin, conflictId, 0)));
        check(!notes.GetProperty("resolved").GetBoolean() && notes.GetProperty("items").GetArrayLength() == 1 &&
              await Reject<UnauthorizedAccessException>(() => Use(s => s.ListConflictNotesAsync(limited, conflictId, 0))),
              "conflict notes remain restricted and do not mark collision resolved");
        var conflictList = JsonSerializer.SerializeToElement(await Use(s => s.ListConflictsAsync(admin, source, 0)));
        var listed = conflictList.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("Id").GetGuid() == conflictId);
        check(listed.GetProperty("NoteCount").GetInt32() == 1 && !listed.GetProperty("Resolved").GetBoolean(),
              "collision list shows note count without presenting the conflict as resolved");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var blocked = false;
            try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.RawConflictNotes WHERE Id = {noteId}"); }
            catch (SqlException e) when (e.Number == 51010) { blocked = true; }
            check(blocked && await db.RawEventConflicts.AnyAsync(x => x.Id == conflictId) &&
                  await scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().SecurityAudit.AnyAsync(x => x.Action == "attendance.conflict.noted:" + noteId.ToString("N")),
                  "SQL protects conflict notes and their audit without modifying collision evidence");
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>();
            check(await db.RawAttendanceEvents.CountAsync(x => x.SourceId == source) == 7 &&
                await audit.SecurityAudit.AnyAsync(x => x.ActorUserId == admin && x.Action == "attendance.received:" + noId1.EventId.ToString("N")),
                "raw records and transaction audit persist without duplicate or overwrite");
        }
    }
}
