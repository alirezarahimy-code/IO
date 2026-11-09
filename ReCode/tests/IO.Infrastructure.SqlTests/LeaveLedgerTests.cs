using IO.Infrastructure.Leaves;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class LeaveLedgerTests
{
    public static async Task RunAsync(string connection,Action<bool,string> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);
        await using var provider=services.BuildServiceProvider();
        Guid admin,company,employee,otherEmployee,foreignEmployee;
        var self=Guid.NewGuid();var other=Guid.NewGuid();var unlinked=Guid.NewGuid();var role=Guid.NewGuid();
        await using(var scope=provider.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var adminRole=await db.Roles.SingleAsync(x=>x.Name=="Initial administrator");
            admin=await db.UserRoles.Where(x=>x.RoleId==adminRole.Id).Select(x=>x.UserId).SingleAsync();
            var me=await db.Employees.SingleAsync(x=>x.PersonnelNumber=="PERSONAL-TEST-1");company=me.CompanyId;employee=me.Id;
            otherEmployee=await db.Employees.Where(x=>x.CompanyId==company && x.Id!=employee).Select(x=>x.Id).FirstAsync();
            foreignEmployee=await db.Employees.Where(x=>x.CompanyId!=company).Select(x=>x.Id).FirstAsync();
            db.Users.AddRange(new UserRecord{Id=self,UserName="leave-test-self",EmployeeId=employee,IsActive=true},
                new UserRecord{Id=other,UserName="leave-test-other",EmployeeId=otherEmployee,IsActive=true},
                new UserRecord{Id=unlinked,UserName="leave-test-unlinked",IsActive=true});
            db.Roles.Add(new RoleRecord{Id=role,Name="leave-test-self-role"});
            db.UserRoles.AddRange(new UserRoleRecord{UserId=self,RoleId=role},new UserRoleRecord{UserId=other,RoleId=role},new UserRoleRecord{UserId=unlinked,RoleId=role});
            foreach(var (r,code) in new[]{(adminRole.Id,LeaveLedger.Manage),(role,LeaveLedger.ReadSelf)})
            {
                var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);
                if(p is null){p=new PermissionRecord{Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}
                if(!await db.RolePermissions.AnyAsync(x=>x.RoleId==r && x.PermissionId==p.Id))db.RolePermissions.Add(new RolePermissionRecord{RoleId=r,PermissionId=p.Id});
            }
            await db.SaveChangesAsync();
        }
        async Task<T> Use<T>(Func<LeaveLedger,Task<T>> f){await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<LeaveLedger>());}
        async Task<bool> Reject<T>(Func<Task> f) where T:Exception{try{await f();return false;}catch(T){return true;}}
        var openingDate=new DateOnly(2024,3,20);var day=new DateOnly(2024,3,25);var asOf=new DateOnly(2024,3,30);
        check(await Reject<UnauthorizedAccessException>(()=>Use(s=>s.CreateTypeAsync(self,company,"denied","denied",480,false))) &&
              await Reject<ArgumentException>(()=>Use(s=>s.CreateTypeAsync(admin,company,"invalid","invalid",0,false))),
              "leave type management requires explicit permission and bounded day length");
        var type=await Use(s=>s.CreateTypeAsync(admin,company,"Synthetic annual leave","SYN-LEAVE",480,true));
        check(await Reject<DomainConflictException>(()=>Use(s=>s.CreateTypeAsync(admin,company,"duplicate","SYN-LEAVE",480,false))) &&
              await Reject<ArgumentException>(()=>Use(s=>s.AddEntryAsync(admin,Guid.NewGuid(),foreignEmployee,type,day,1,60,"DOC","foreign"))) &&
              await Reject<ArgumentException>(()=>Use(s=>s.AddEntryAsync(admin,Guid.NewGuid(),employee,type,day,0,0,"DOC","late opening"))),
              "leave types are company-bound with unique symbols and opening only on Persian year start");
        var grantId=Guid.NewGuid();
        await Use(s=>s.AddEntryAsync(admin,grantId,employee,type,day,1,120,"DOC-GRANT","grant"));
        var missing=await Use(s=>s.GetOwnBalanceAsync(self,type,asOf));
        check(missing.PersianYear==1403 && missing.OpeningMinutes is null && missing.GrantedMinutes==120 && missing.BalanceMinutes is null &&
              await Reject<UnauthorizedAccessException>(()=>Use(s=>s.GetOwnBalanceAsync(unlinked,type,asOf))) &&
              await Reject<UnauthorizedAccessException>(()=>Use(s=>s.GetBalanceAsync(self,employee,type,asOf))),
              "missing opening never becomes zero and own balances require linked authorized identity");
        var opening=Guid.NewGuid();await Use(s=>s.AddEntryAsync(admin,opening,employee,type,openingDate,0,960,"DOC-OPEN","opening"));
        var usage=Guid.NewGuid();await Use(s=>s.AddEntryAsync(admin,usage,employee,type,day,2,-60,"DOC-USE","manual consumption"));
        var adjust=Guid.NewGuid();await Use(s=>s.AddEntryAsync(admin,adjust,employee,type,day,3,-30,"DOC-ADJ","adjustment"));
        await Use(s=>s.AddEntryAsync(admin,Guid.NewGuid(),employee,type,new DateOnly(2024,4,5),1,500,"FUTURE","later"));
        var balance=await Use(s=>s.GetOwnBalanceAsync(self,type,asOf));var otherBalance=await Use(s=>s.GetOwnBalanceAsync(other,type,asOf));
        check(balance.OpeningMinutes==960 && balance.GrantedMinutes==120 && balance.UsedMinutes==60 && balance.AdjustmentMinutes==-30 && balance.BalanceMinutes==990 &&
              otherBalance.BalanceMinutes is null && (await Use(s=>s.GetOwnBalanceAsync(self,type,new DateOnly(2025,3,22)))).BalanceMinutes is null,
              "manual leave balance is year and as-of bounded and scoped to linked employee");
        check(await Use(s=>s.AddEntryAsync(admin,grantId,employee,type,day,1,120,"DOC-GRANT","grant"))==grantId &&
              await Reject<DomainConflictException>(()=>Use(s=>s.AddEntryAsync(admin,grantId,employee,type,day,1,121,"DOC-GRANT","grant"))) &&
              await Reject<DomainConflictException>(()=>Use(s=>s.AddEntryAsync(admin,Guid.NewGuid(),employee,type,openingDate,0,1,"OTHER","duplicate"))) &&
              await Reject<ArgumentException>(()=>Use(s=>s.AddEntryAsync(admin,Guid.NewGuid(),employee,type,day,2,1,"DOC","bad sign"))),
              "ledger retries are idempotent and conflicting operations, duplicate openings and invalid signs are rejected");
        var pair=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>
        {
            try{await Use(s=>s.AddEntryAsync(admin,Guid.NewGuid(),otherEmployee,type,openingDate,0,0,"CONCURRENT","opening"));return true;}
            catch(DomainConflictException){return false;}
        }));
        check(pair.Count(x=>x)==1,"concurrent opening entries cannot both commit");
        await using(var scope=provider.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();
            bool typeBlocked=false,entryBlocked=false;
            try{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.LeaveTypes SET Name = {'x'.ToString()} WHERE Id = {type}");}catch(SqlException e) when(e.Number==51020){typeBlocked=true;}
            try{await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.LeaveLedgerEntries WHERE Id = {grantId}");}catch(SqlException e) when(e.Number==51021){entryBlocked=true;}
            var audit=scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>();
            check(typeBlocked && entryBlocked && await audit.SecurityAudit.AnyAsync(x=>x.Action=="leave.entry.recorded:"+grantId.ToString("N")) &&
                  await db.LeaveLedgerEntries.CountAsync(x=>x.Id==grantId)==1,
                  "leave type and ledger history are immutable and writes are audited");
        }
    }
}
