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
            var permission = new PermissionRecord { Id = Guid.NewGuid(), Code = AttendanceIngestion.Manage };
            db.Permissions.Add(permission); db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = permission.Id });
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
        var noId1 = await Append(null, [7]); var noId2 = await Append(null, [7]);
        check(noId1.Outcome == RawEventAppendResult.IdentityRequiresReview && noId2.Outcome == RawEventAppendResult.IdentityRequiresReview && noId1.EventId != noId2.EventId,
            "unidentified events are retained independently for review");
        var upper = await Append("Exact", [1]); var lower = await Append("exact", [1]);
        var trailing = await Append("Exact ", [1]); var nul = await Append("Exact\0", [1]);
        check(new[] { upper, lower, trailing, nul }.All(x => x.Outcome == RawEventAppendResult.Appended) &&
            (await Append("Exact\0", [1])).Outcome == RawEventAppendResult.AlreadyRecorded,
            "external identity uses exact UTF-16 including case, spaces and trailing NUL");
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
