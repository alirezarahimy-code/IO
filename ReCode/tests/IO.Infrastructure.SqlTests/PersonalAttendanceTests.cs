using IO.Infrastructure.Attendance;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class PersonalAttendanceTests
{
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        var actor = Guid.NewGuid(); var otherActor = Guid.NewGuid(); var deniedActor = Guid.NewGuid();
        var employee = Guid.NewGuid(); var otherEmployee = Guid.NewGuid(); var role = Guid.NewGuid(); var permission = Guid.NewGuid();
        var source = Guid.NewGuid(); var match = Guid.NewGuid(); var boundary = Guid.NewGuid();
        var start = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero); var end = start.AddDays(1);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var site = await db.Sites.AsNoTracking().FirstAsync();
            db.Employees.AddRange(new EmployeeRecord { Id=employee, CompanyId=site.CompanyId, PersonnelNumber="PERSONAL-TEST-1", DisplayName="Personal test" },
                new EmployeeRecord { Id=otherEmployee, CompanyId=site.CompanyId, PersonnelNumber="PERSONAL-TEST-2", DisplayName="Other test" });
            db.Users.AddRange(new UserRecord { Id=actor, EmployeeId=employee, UserName="personal-test-self", IsActive=true },
                new UserRecord { Id=otherActor, EmployeeId=otherEmployee, UserName="personal-test-other", IsActive=true },
                new UserRecord { Id=deniedActor, EmployeeId=employee, UserName="personal-test-denied", IsActive=true });
            db.Roles.Add(new RoleRecord { Id=role, Name="personal-test-role" });
            // A permission may have been provisioned by the browser fixture.
            var existing = await db.Permissions.SingleOrDefaultAsync(p => p.Code == PersonalAttendance.ReadSelf);
            if (existing is null) db.Permissions.Add(new PermissionRecord { Id=permission, Code=PersonalAttendance.ReadSelf });
            else permission=existing.Id;
            db.RolePermissions.Add(new RolePermissionRecord { RoleId=role, PermissionId=permission });
            db.UserRoles.AddRange(new UserRoleRecord { UserId=actor, RoleId=role }, new UserRoleRecord { UserId=otherActor, RoleId=role });
            db.EventSources.Add(new EventSourceRecord { Id=source, SiteId=site.Id, Name="Personal source" });
            await db.SaveChangesAsync();
            db.SourceEmployeeMappings.AddRange(
                new SourceEmployeeMappingRecord { Id=Guid.NewGuid(), SourceId=source, SourcePersonId="Person", EmployeeId=employee, CompanyId=site.CompanyId, ValidFromUtc=start, ValidUntilUtcExclusive=end },
                new SourceEmployeeMappingRecord { Id=Guid.NewGuid(), SourceId=source, SourcePersonId="person", EmployeeId=otherEmployee, CompanyId=site.CompanyId, ValidFromUtc=start, ValidUntilUtcExclusive=end },
                new SourceEmployeeMappingRecord { Id=Guid.NewGuid(), SourceId=source, SourcePersonId="ambiguous", EmployeeId=employee, CompanyId=site.CompanyId, ValidFromUtc=start, ValidUntilUtcExclusive=end },
                new SourceEmployeeMappingRecord { Id=Guid.NewGuid(), SourceId=source, SourcePersonId="ambiguous", EmployeeId=otherEmployee, CompanyId=site.CompanyId, ValidFromUtc=start, ValidUntilUtcExclusive=end });
            RawAttendanceEventRecord Raw(Guid id, string? person, DateTimeOffset? time) => new() { Id=id, SourceId=source, ExternalEventId=id.ToString(), SourceEmployeeIdentifier=person, OccurredAtUtc=time, ReceivedAtUtc=start, ReportedEventType="in", SourcePayload=[1] };
            db.RawAttendanceEvents.AddRange(Raw(match,"Person",start),Raw(Guid.NewGuid(),"person",start),Raw(Guid.NewGuid(),"Person ",start),
                Raw(Guid.NewGuid(),"Person\0",start),Raw(Guid.NewGuid(),"ambiguous",start),Raw(Guid.NewGuid(),null,start),
                Raw(Guid.NewGuid(),"Person",null),Raw(boundary,"Person",end));
            await db.SaveChangesAsync();
        }
        async Task<PersonalAttendance.EventPage> Read(Guid who, DateTimeOffset from, DateTimeOffset until, int offset=0)
        {
            await using var scope=provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<PersonalAttendance>().ListAsync(who,from,until,offset);
        }
        var mine=await Read(actor,start,end);
        check(mine.Items.Count == 1 && mine.Items[0].Id == match && mine.Items[0].Source == "Personal source" && mine.Items[0].ReportedEventType == "in",
            "personal attendance returns only exact uniquely mapped self events in the half-open interval");
        var theirs=await Read(otherActor,start,end);
        check(theirs.Items.Count == 1 && theirs.Items[0].Id != match && (await Read(actor,end,end.AddDays(1))).Items.Count == 0,
            "personal attendance enforces linked identity and mapping end boundary");
        bool denied=false, invalid=false, wrongZone=false;
        try { await Read(deniedActor,start,end); } catch (UnauthorizedAccessException) { denied=true; }
        try { await Read(actor,start,start.AddDays(63)); } catch (ArgumentException) { invalid=true; }
        try { await Read(actor,start.ToOffset(TimeSpan.FromHours(1)),end); } catch (ArgumentException) { wrongZone=true; }
        check(denied && invalid && wrongZone, "personal attendance requires explicit permission and bounded UTC interval");
        await using (var scope=provider.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var user=await db.Users.SingleAsync(x=>x.Id==actor); user.IsActive=false; await db.SaveChangesAsync();
        }
        denied=false; try { await Read(actor,start,end); } catch (UnauthorizedAccessException) { denied=true; }
        check(denied, "disabled account cannot read personal attendance");
    }
}
