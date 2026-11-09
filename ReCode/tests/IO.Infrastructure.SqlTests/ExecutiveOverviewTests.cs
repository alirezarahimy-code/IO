using IO.Infrastructure.Reports;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class ExecutiveOverviewTests
{
    public static async Task RunAsync(string connection, Action<bool,string> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);
        await using var provider=services.BuildServiceProvider();
        var actor=Guid.NewGuid();var denied=Guid.NewGuid();var role=Guid.NewGuid();Guid company,otherCompany;
        var from=new DateTimeOffset(2031,1,1,0,0,0,TimeSpan.Zero);var until=from.AddDays(1);
        int employeeCount, accountCount, pendingCount, eventCount;
        await using(var scope=provider.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();
            company=await db.Employees.Where(e=>e.PersonnelNumber=="PERSONAL-TEST-1").Select(e=>e.CompanyId).SingleAsync();
            otherCompany=await db.Companies.Where(x=>x.Id!=company).Select(x=>x.Id).FirstAsync();
            db.Users.AddRange(new UserRecord{Id=actor,UserName="executive test actor",IsActive=true},new UserRecord{Id=denied,UserName="executive denied actor",IsActive=true});
            db.Roles.Add(new RoleRecord{Id=role,Name="executive test role"});
            var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==ExecutiveOverview.Read);
            if(p is null){p=new PermissionRecord{Id=Guid.NewGuid(),Code=ExecutiveOverview.Read};db.Permissions.Add(p);}
            db.UserRoles.Add(new UserRoleRecord{UserId=actor,RoleId=role});db.RolePermissions.Add(new RolePermissionRecord{RoleId=role,PermissionId=p.Id});
            await db.SaveChangesAsync();
            employeeCount=await db.Employees.CountAsync(x=>x.CompanyId==company);
            accountCount=await (from u in db.Users join e in db.Employees on u.EmployeeId equals (Guid?)e.Id where u.IsActive && e.CompanyId==company select u.Id).CountAsync();
            pendingCount=await db.CorrectionRequests.CountAsync(x=>x.CompanyId==company && x.State==0);
            eventCount=await (from raw in db.RawAttendanceEvents join source in db.EventSources on raw.SourceId equals source.Id join site in db.Sites on source.SiteId equals (Guid?)site.Id
                              where site.CompanyId==company && raw.OccurredAtUtc>=from && raw.OccurredAtUtc<until select raw.Id).CountAsync();
        }
        async Task<ExecutiveOverview.Summary> Read(Guid who,Guid c,DateTimeOffset a,DateTimeOffset b)
        {await using var scope=provider.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<ExecutiveOverview>().InspectAsync(who,c,a,b);}
        var result=await Read(actor,company,from,until);
        check(result.CompanyId==company && result.EmployeeCount==employeeCount && result.ActiveLinkedAccountCount==accountCount &&
              result.PendingCorrectionCount==pendingCount && result.RawEventCount==eventCount && eventCount>=6,
              "executive overview counts only defined company records and dated raw events in the UTC interval");
        var other=await Read(actor,otherCompany,from,until);
        check(other.CompanyId==otherCompany && other.RawEventCount==0 && result.RawEventCount>other.RawEventCount,
              "executive overview does not mix company event counts");
        bool unauthorized=false,invalid=false,nonUtc=false,unknown=false;
        try{await Read(denied,company,from,until);}catch(UnauthorizedAccessException){unauthorized=true;}
        try{await Read(actor,company,from,from.AddDays(63));}catch(ArgumentException){invalid=true;}
        try{await Read(actor,company,from.ToOffset(TimeSpan.FromHours(1)),until);}catch(ArgumentException){nonUtc=true;}
        try{await Read(actor,Guid.NewGuid(),from,until);}catch(ArgumentException){unknown=true;}
        check(unauthorized && invalid && nonUtc && unknown,"executive overview requires independent permission and valid bounded company UTC interval");
    }
}
