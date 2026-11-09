using System.Text.Json;
using IO.Infrastructure.Delegation;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using IO.Infrastructure.Workflows;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class DelegationRegistryTests
{
    public static async Task RunAsync(string connection,Action<bool,string> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);
        await using var provider=services.BuildServiceProvider();
        var principal=Guid.NewGuid();var candidate=Guid.NewGuid();var foreign=Guid.NewGuid();var same=Guid.NewGuid();var noReceive=Guid.NewGuid();var unlinked=Guid.NewGuid();
        var principalRole=Guid.NewGuid();var receiverRole=Guid.NewGuid();var requestId=Guid.NewGuid();Guid principalEmployee,candidateEmployee,otherEmployee,foreignEmployee,company;
        await using(var scope=provider.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var p=await db.Employees.SingleAsync(x=>x.PersonnelNumber=="PERSONAL-TEST-1");principalEmployee=p.Id;company=p.CompanyId;
            candidateEmployee=await db.Employees.Where(x=>x.CompanyId==company && x.Id!=principalEmployee).Select(x=>x.Id).FirstAsync();
            otherEmployee=Guid.NewGuid(); db.Employees.Add(new EmployeeRecord{Id=otherEmployee,CompanyId=company,PersonnelNumber="DELEGATION-OTHER",DisplayName="Other delegation person"});
            foreignEmployee=await db.Employees.Where(x=>x.CompanyId!=company).Select(x=>x.Id).FirstAsync();
            db.Users.AddRange(new UserRecord{Id=principal,UserName="delegate-principal",EmployeeId=principalEmployee,IsActive=true},
                new UserRecord{Id=candidate,UserName="delegate-candidate",EmployeeId=candidateEmployee,IsActive=true},
                new UserRecord{Id=foreign,UserName="delegate-foreign",EmployeeId=foreignEmployee,IsActive=true},
                new UserRecord{Id=same,UserName="delegate-same-person",EmployeeId=principalEmployee,IsActive=true},
                new UserRecord{Id=noReceive,UserName="delegate-no-receive",EmployeeId=otherEmployee,IsActive=true},
                new UserRecord{Id=unlinked,UserName="delegate-unlinked",IsActive=true});
            db.Roles.AddRange(new RoleRecord{Id=principalRole,Name="delegate-principal-role"},new RoleRecord{Id=receiverRole,Name="delegate-receiver-role"});
            db.UserRoles.AddRange(new UserRoleRecord{UserId=principal,RoleId=principalRole},new UserRoleRecord{UserId=unlinked,RoleId=principalRole},
                new UserRoleRecord{UserId=candidate,RoleId=receiverRole},new UserRoleRecord{UserId=foreign,RoleId=receiverRole},new UserRoleRecord{UserId=same,RoleId=receiverRole});
            foreach(var (r,code) in new[]{(principalRole,DelegationRegistry.ManageSelf),(receiverRole,DelegationRegistry.Receive),(receiverRole,CorrectionWorkflow.Decide),(receiverRole,CorrectionWorkflow.DecideAll)})
            {
                var permission=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);
                if(permission is null){permission=new PermissionRecord{Id=Guid.NewGuid(),Code=code};db.Permissions.Add(permission);}
                db.RolePermissions.Add(new RolePermissionRecord{RoleId=r,PermissionId=permission.Id});
            }
            await db.SaveChangesAsync();
            var policy=Guid.NewGuid();
            db.CorrectionPolicies.Add(new CorrectionPolicyRecord {Id=policy,CompanyId=company,Name="delegation negative test",ApproverUserId=principal,CreatedByUserId=principal,CreatedAtUtc=DateTimeOffset.UtcNow});
            await db.SaveChangesAsync();
            db.CorrectionRequests.Add(new CorrectionRequestRecord {Id=requestId,CompanyId=company,EmployeeId=otherEmployee,RequesterUserId=noReceive,PolicyId=policy,WorkDate=new DateOnly(2026,1,1),ProposedAtUtc=new DateTimeOffset(2026,1,1,8,0,0,TimeSpan.Zero),PunchKind=0,Reason="delegation negative test",State=0,CreatedAtUtc=DateTimeOffset.UtcNow});
            await db.SaveChangesAsync();
        }
        async Task<T> Use<T>(Func<DelegationRegistry,Task<T>> f){await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<DelegationRegistry>());}
        async Task<bool> Reject<T>(Func<Task> f) where T:Exception{try{await f();return false;}catch(T){return true;}}
        var today=DateOnly.FromDateTime(DateTime.UtcNow);var tomorrow=today.AddDays(1);var end=today.AddDays(3);
        var list=JsonSerializer.SerializeToElement(await Use(s=>s.CandidatesAsync(principal,0))).GetProperty("items");
        var ids=list.EnumerateArray().Select(x=>x.GetProperty("Id").GetGuid()).ToHashSet();
        check(ids.Contains(candidate) && !ids.Contains(same) && !ids.Contains(foreign) && !ids.Contains(noReceive) &&
              await Reject<UnauthorizedAccessException>(()=>Use(s=>s.CandidatesAsync(candidate,0))) && await Reject<UnauthorizedAccessException>(()=>Use(s=>s.CandidatesAsync(unlinked,0))),
              "delegation candidates require principal permission, linked identity, same company, other person and explicit receiver eligibility");
        check(await Reject<ArgumentException>(()=>Use(s=>s.AssignAsync(principal,Guid.NewGuid(),same,today,tomorrow))) &&
              await Reject<ArgumentException>(()=>Use(s=>s.AssignAsync(principal,Guid.NewGuid(),foreign,today,tomorrow))) &&
              await Reject<ArgumentException>(()=>Use(s=>s.AssignAsync(principal,Guid.NewGuid(),noReceive,today,tomorrow))) &&
              await Reject<ArgumentException>(()=>Use(s=>s.AssignAsync(principal,Guid.NewGuid(),candidate,today.AddDays(-1),tomorrow))) &&
              await Reject<ArgumentException>(()=>Use(s=>s.AssignAsync(principal,Guid.NewGuid(),candidate,today,today.AddDays(367)))),
              "delegation assignment rejects invalid candidate, past dates and oversized periods");
        var id=Guid.NewGuid();await Use(s=>s.AssignAsync(principal,id,candidate,today,end));
        check(await Use(s=>s.AssignAsync(principal,id,candidate,today,end))==id &&
              await Reject<DomainConflictException>(()=>Use(s=>s.AssignAsync(principal,id,candidate,today,tomorrow))) &&
              await Reject<DomainConflictException>(()=>Use(s=>s.AssignAsync(principal,Guid.NewGuid(),candidate,tomorrow,end.AddDays(1)))) &&
              await Reject<UnauthorizedAccessException>(()=>Use(s=>s.RevokeAsync(candidate,id,"not mine"))),
              "delegation assignment is idempotent, overlap-protected and only principal can revoke");
        // An informational assignment must not authorize a delegated workflow decision.
        await using(var scope=provider.CreateAsyncScope())
        {
            var workflow=scope.ServiceProvider.GetRequiredService<CorrectionWorkflow>();
            check(await Reject<UnauthorizedAccessException>(()=>workflow.DecideAsync(candidate,requestId,true,null)),
                "information-only delegation never grants correction decision authority");
        }
        check(await Use(s=>s.RevokeAsync(principal,id,"reason")) && await Use(s=>s.RevokeAsync(principal,id,"reason")) &&
              await Reject<DomainConflictException>(()=>Use(s=>s.RevokeAsync(principal,id,"different"))),
              "delegation revocation is auditable and retry-safe without changing the assignment");
        // Exercise only the structural evidence tables. This is not an operational grant or decision API.
        await using(var scope=provider.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var decisionId=Guid.NewGuid();
            db.CorrectionDelegationGrants.Add(new CorrectionDelegationGrantRecord { DelegationId=id, PrincipalUserId=principal, GrantedAtUtc=DateTimeOffset.UtcNow });
            db.CorrectionDecisions.Add(new CorrectionDecisionRecord { Id=decisionId, RequestId=requestId, ActorUserId=candidate, Decision=1, AtUtc=DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            bool grantUpdate=false,grantDelete=false,evidenceUpdate=false,evidenceDelete=false,badGrant=false,badEvidence=false;
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.CorrectionDelegationGrants SET PrincipalUserId=PrincipalUserId WHERE DelegationId={id}"); } catch(SqlException e) when(e.Number==51032) { grantUpdate=true; }
            try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.CorrectionDelegationGrants WHERE DelegationId={id}"); } catch(SqlException e) when(e.Number==51032) { grantDelete=true; }
            db.DelegatedCorrectionDecisions.Add(new DelegatedCorrectionDecisionRecord { DecisionId=decisionId, DelegationId=id });
            await db.SaveChangesAsync();
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DelegatedCorrectionDecisions SET DelegationId=DelegationId WHERE DecisionId={decisionId}"); } catch(SqlException e) when(e.Number==51033) { evidenceUpdate=true; }
            try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.DelegatedCorrectionDecisions WHERE DecisionId={decisionId}"); } catch(SqlException e) when(e.Number==51033) { evidenceDelete=true; }
            try { await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.CorrectionDelegationGrants (DelegationId,PrincipalUserId,GrantedAtUtc) VALUES ({Guid.NewGuid()},{principal},{DateTimeOffset.UtcNow})"); } catch(SqlException e) when(e.Number==547) { badGrant=true; }
            try { await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.DelegatedCorrectionDecisions (DecisionId,DelegationId) VALUES ({Guid.NewGuid()},{id})"); } catch(SqlException e) when(e.Number==547) { badEvidence=true; }
            check(grantUpdate && grantDelete && evidenceUpdate && evidenceDelete && badGrant && badEvidence,
                "delegated correction evidence inserts, foreign keys and immutable triggers enforced");
        }
        var pair=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>
        {
            try{await Use(s=>s.AssignAsync(principal,Guid.NewGuid(),candidate,today,end));return true;}
            catch(DomainConflictException){return false;}
        }));
        check(pair.Count(x=>x)==1,"concurrent overlapping delegations cannot both commit");
        await using(var scope=provider.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();bool assignmentBlocked=false,revokeBlocked=false;
            try{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Delegations SET PrincipalUserId=PrincipalUserId WHERE Id={id}");}catch(SqlException e) when(e.Number==51030){assignmentBlocked=true;}
            try{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DelegationRevocations SET Reason={'x'.ToString()} WHERE DelegationId={id}");}catch(SqlException e) when(e.Number==51031){revokeBlocked=true;}
            var audit=scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>();
            check(assignmentBlocked && revokeBlocked && await audit.SecurityAudit.AnyAsync(x=>x.Action=="delegation.assigned:"+id.ToString("N")) &&
                  await audit.SecurityAudit.AnyAsync(x=>x.Action=="delegation.revoked:"+id.ToString("N")),
                  "delegation assignments and revocations are immutable and both actions audited");
        }
    }
}
