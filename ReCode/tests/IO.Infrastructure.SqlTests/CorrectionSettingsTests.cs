using System.Text.Json;
using IO.Infrastructure.Security;
using IO.Infrastructure.Settings;
using IO.Infrastructure.Storage;
using IO.Infrastructure.Workflows;
using IO.Domain.Scheduling;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class CorrectionSettingsTests
{
    public static async Task RunAsync(string connection,Action<bool,string> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);
        await using var provider=services.BuildServiceProvider();
        var org=Guid.NewGuid();var company=Guid.NewGuid();var company2=Guid.NewGuid();var employee=Guid.NewGuid();var employee2=Guid.NewGuid();
        var requester=Guid.NewGuid();var requester2=Guid.NewGuid();var approver=Guid.NewGuid();var role=Guid.NewGuid();var approverRole=Guid.NewGuid();Guid admin;
        await using(var scope=provider.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();var adminRole=await db.Roles.SingleAsync(x=>x.Name=="Initial administrator");
            admin=await db.UserRoles.Where(x=>x.RoleId==adminRole.Id).Select(x=>x.UserId).SingleAsync();
            db.Organizations.Add(new OrganizationRecord{Id=org,Name="settings test org"});
            db.Companies.AddRange(new CompanyRecord{Id=company,OrganizationId=org,Name="settings company"},new CompanyRecord{Id=company2,OrganizationId=org,Name="settings other"});
            db.Employees.AddRange(new EmployeeRecord{Id=employee,CompanyId=company,PersonnelNumber="SET-1",DisplayName="Setting person"},new EmployeeRecord{Id=employee2,CompanyId=company2,PersonnelNumber="SET-2",DisplayName="Other person"});
            db.Users.AddRange(new UserRecord{Id=requester,UserName="setting-requester",EmployeeId=employee,IsActive=true},new UserRecord{Id=requester2,UserName="setting-requester-2",EmployeeId=employee2,IsActive=true},new UserRecord{Id=approver,UserName="setting-approver",IsActive=true});
            db.Roles.AddRange(new RoleRecord{Id=role,Name="setting-submitter-role"},new RoleRecord{Id=approverRole,Name="setting-approver-role"});db.UserRoles.AddRange(new UserRoleRecord{UserId=requester,RoleId=role},new UserRoleRecord{UserId=requester2,RoleId=role},new UserRoleRecord{UserId=approver,RoleId=approverRole});
            foreach(var (r,code) in new[]{(adminRole.Id,CorrectionSettings.Manage),(adminRole.Id,CorrectionWorkflow.Configure),(role,CorrectionWorkflow.Submit),(approverRole,CorrectionWorkflow.Decide),(approverRole,CorrectionWorkflow.DecideAll)})
            {
                var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new PermissionRecord{Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}
                if(!await db.RolePermissions.AnyAsync(x=>x.RoleId==r && x.PermissionId==p.Id))db.RolePermissions.Add(new RolePermissionRecord{RoleId=r,PermissionId=p.Id});
            }
            await db.SaveChangesAsync();
        }
        async Task<T> Set<T>(Func<CorrectionSettings,Task<T>> f){await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<CorrectionSettings>());}
        async Task<T> Flow<T>(Func<CorrectionWorkflow,Task<T>> f){await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<CorrectionWorkflow>());}
        async Task<bool> Reject<T>(Func<Task> f) where T:Exception{try{await f();return false;}catch(T){return true;}}
        var initial=JsonSerializer.SerializeToElement(await Set(s=>s.ReadAsync(admin,company,0)));
        check(!initial.GetProperty("configured").GetBoolean() && initial.GetProperty("revision").GetInt32()==0 && initial.GetProperty("maxRequestAgeDays").GetInt32()==0 &&
              await Reject<UnauthorizedAccessException>(()=>Set(s=>s.ReadAsync(requester,company,0))) &&
              await Reject<ArgumentException>(()=>Set(s=>s.SetAsync(admin,Guid.NewGuid(),company,0,3651,"invalid"))),
              "correction settings default to unlimited and require explicit management permission and bounded value");
        var policy=await Flow(s=>s.CreatePolicyAsync(admin,company,approver,"settings test policy"));
        var otherPolicy=await Flow(s=>s.CreatePolicyAsync(admin,company2,approver,"settings test other policy"));
        var today=DateOnly.FromDateTime(DateTime.UtcNow);var proposed=DateTimeOffset.UtcNow;
        var before=await Flow(s=>s.SubmitAsync(requester,policy,today.AddDays(-50),proposed,0,"before setting"));
        var op=Guid.NewGuid();await Set(s=>s.SetAsync(admin,op,company,0,5,"set five"));
        var own=JsonSerializer.SerializeToElement(await Set(s=>s.ReadOwnAsync(requester)));
        check(own.GetProperty("configured").GetBoolean() && own.GetProperty("maxRequestAgeDays").GetInt32()==5 &&
              await Reject<ArgumentException>(()=>Flow(s=>s.SubmitAsync(requester,policy,today.AddDays(-6),proposed,0,"too old"))) &&
              await Flow(s=>s.SubmitAsync(requester,policy,today.AddDays(-5),proposed,0,"boundary"))!=Guid.Empty &&
              await Flow(s=>s.SubmitAsync(requester2,otherPolicy,today.AddDays(-50),proposed,0,"other company"))!=Guid.Empty,
              "maximum request age is enforced at UTC date boundary for configured company only");
        check(await Set(s=>s.SetAsync(admin,op,company,0,5,"set five"))==op &&
              await Reject<DomainConflictException>(()=>Set(s=>s.SetAsync(admin,op,company,0,6,"set five"))) &&
              await Reject<DomainConflictException>(()=>Set(s=>s.SetAsync(admin,Guid.NewGuid(),company,0,2,"stale"))),
              "settings writes are idempotent and stale revisions cannot overwrite newer settings");
        var results=await Task.WhenAll(Enumerable.Range(0,2).Select(async i=>
        {try{await Set(s=>s.SetAsync(admin,Guid.NewGuid(),company,1,i+1,"concurrent"));return true;}catch(DomainConflictException){return false;}}));
        check(results.Count(x=>x)==1,"concurrent settings updates from one revision cannot both succeed");
        await Set(s=>s.SetAsync(admin,Guid.NewGuid(),company,2,0,"unlimited"));
        check(await Flow(s=>s.SubmitAsync(requester,policy,today.AddDays(-50),proposed,0,"unlimited again"))!=Guid.Empty &&
              JsonSerializer.SerializeToElement(await Set(s=>s.ReadOwnAsync(requester))).GetProperty("maxRequestAgeDays").GetInt32()==0,
              "zero restores unlimited submissions without deleting history");
        await Set(s=>s.SetAsync(admin,Guid.NewGuid(),company,3,0,"count limits",daily:1,monthly:2));
        var day1=PersianDates.ToGregorian(1408,12,20);var day2=day1.AddDays(1);var day3=day1.AddDays(2);var nextMonth=PersianDates.ToGregorian(1409,1,1);
        var first=await Flow(s=>s.SubmitAsync(requester,policy,day1,proposed,0,"daily first"));
        check(await Reject<DomainConflictException>(()=>Flow(s=>s.SubmitAsync(requester,policy,day1,proposed,1,"daily excess"))) &&
              await Flow(s=>s.CancelAsync(requester,first,"cancel first")) &&
              await Flow(s=>s.SubmitAsync(requester,policy,day1,proposed,1,"after cancel"))!=Guid.Empty,
              "daily limits count pending and approved requests and free rejected or cancelled requests");
        Guid accepted;
        await using(var sc=provider.CreateAsyncScope()) accepted=await sc.ServiceProvider.GetRequiredService<IoDbContext>().CorrectionRequests.Where(x=>x.EmployeeId==employee && x.WorkDate==day1 && x.State==0).Select(x=>x.Id).SingleAsync();
        await Flow(s=>s.DecideAsync(approver,accepted,true,"approved"));
        var rejected=await Flow(s=>s.SubmitAsync(requester,policy,day2,proposed,0,"to reject"));
        await Flow(s=>s.DecideAsync(approver,rejected,false,"rejected"));
        var third=await Flow(s=>s.SubmitAsync(requester,policy,day3,proposed,0,"after rejection"));
        check(third!=Guid.Empty && await Reject<DomainConflictException>(()=>Flow(s=>s.SubmitAsync(requester,policy,day3.AddDays(1),proposed,0,"monthly excess"))) &&
              await Flow(s=>s.SubmitAsync(requester,policy,nextMonth,proposed,0,"new Persian year"))!=Guid.Empty,
              "monthly limit counts active results in exact Persian month and resets at year boundary");
        var raceDay=PersianDates.ToGregorian(1409,2,1);
        async Task<bool> ConcurrentSubmit(){try{await Flow(s=>s.SubmitAsync(requester,policy,raceDay,proposed,0,"limit race"));return true;}catch(DomainConflictException){return false;}}
        var submissions=await Task.WhenAll(ConcurrentSubmit(),ConcurrentSubmit());
        await using(var sc=provider.CreateAsyncScope())
        {
            var db=sc.ServiceProvider.GetRequiredService<IoDbContext>();
            check(submissions.Count(x=>x)==1 && await db.CorrectionRequests.CountAsync(x=>x.EmployeeId==employee && x.WorkDate==raceDay && x.State==0)==1,
                  "concurrent submissions cannot both exceed the daily company setting");
        }
        await using(var scope=provider.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();bool immutable=false;
            try{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.CorrectionSettings SET MaxRequestAgeDays=0 WHERE Id={op}");}catch(SqlException e) when(e.Number==51040){immutable=true;}
            var audit=scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>();
            check(immutable && await db.CorrectionSettings.CountAsync(x=>x.CompanyId==company)==4 && await db.CorrectionRequests.AnyAsync(x=>x.Id==before) &&
                  await audit.SecurityAudit.AnyAsync(x=>x.Action=="settings.correction.updated:"+op.ToString("N")),
                  "setting history is immutable and audited without changing existing requests");
        }
    }
}
