using System.Text.Json;
using IO.Infrastructure.Leaves;
using IO.Infrastructure.Periods;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using IO.Infrastructure.Workflows;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class DecisionReversalTests
{
    public static async Task RunAsync(string connection, Action<bool,string> check)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        var org=Guid.NewGuid();var company=Guid.NewGuid();var unit=Guid.NewGuid();var employee=Guid.NewGuid();var self=Guid.NewGuid();
        var manager=Guid.NewGuid();var outsider=Guid.NewGuid();var duplicate=Guid.NewGuid();var role=Guid.NewGuid();var ownRole=Guid.NewGuid();
        var today=DateOnly.FromDateTime(DateTime.UtcNow);var day=today.AddDays(12);Guid policy,type;
        async Task<T> Use<TService,T>(Func<TService,Task<T>> action) where TService:notnull
        { await using var scope=provider.CreateAsyncScope();return await action(scope.ServiceProvider.GetRequiredService<TService>()); }
        async Task<T> Query<T>(Func<IoDbContext,Task<T>> action) => await Use<IoDbContext,T>(action);
        async Task<bool> Reject<T>(Func<Task> action) where T:Exception
        {try{await action();return false;}catch(T){return true;}}
        async Task SqlReject(string sql,int number)
        {try{await Query(async db=>await db.Database.ExecuteSqlRawAsync(sql));}catch(SqlException e) when(e.Number==number){return;}throw new InvalidOperationException("Expected database guard "+number);}
        await Query(async db=>{
            db.Organizations.Add(new(){Id=org,Name="Reversal organization"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Reversal company"});
            db.OrgUnits.Add(new(){Id=unit,CompanyId=company,Name="Reversal unit"});
            db.Employees.Add(new(){Id=employee,CompanyId=company,PersonnelNumber="REV-EMP",DisplayName="Reversal employee"});
            db.EmployeeAssignments.Add(new(){Id=Guid.NewGuid(),CompanyId=company,EmployeeId=employee,OrgUnitId=unit,ValidFrom=today.AddDays(-2)});
            db.Users.AddRange(new UserRecord{Id=self,UserName="reversal-self",EmployeeId=employee,IsActive=true},new UserRecord{Id=manager,UserName="reversal-manager",IsActive=true},
                new UserRecord{Id=outsider,UserName="reversal-outsider",IsActive=true},new UserRecord{Id=duplicate,UserName="reversal-duplicate",EmployeeId=employee,IsActive=true});
            db.Roles.AddRange(new RoleRecord{Id=role,Name="reversal manager"},new RoleRecord{Id=ownRole,Name="reversal employee"});
            db.UserRoles.AddRange(new UserRoleRecord{UserId=manager,RoleId=role},new UserRoleRecord{UserId=outsider,RoleId=role},new UserRoleRecord{UserId=duplicate,RoleId=role},new UserRoleRecord{UserId=self,RoleId=ownRole});
            db.UserOrgScopes.AddRange(new UserOrgScopeRecord{Id=Guid.NewGuid(),UserId=manager,OrgUnitId=unit,ValidFrom=today.AddDays(-2)},
                new UserOrgScopeRecord{Id=Guid.NewGuid(),UserId=duplicate,OrgUnitId=unit,ValidFrom=today.AddDays(-2)});
            foreach(var (r,codes) in new[]{(role,new[]{"correction.reverse","leave.request.reverse","correction.configure","correction.decide.scoped","correction.decide","leave.request.decide","leave.manage","period.draft.manage"}),
                (ownRole,new[]{"correction.submit.self","leave.request.submit","leave.read.self"})})
                foreach(var code in codes){var p=await db.Permissions.SingleOrDefaultAsync(p=>p.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}
                    db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}
            await db.SaveChangesAsync();return true;
        });
        policy=await Use<CorrectionWorkflow,Guid>(s=>s.CreatePolicyAsync(manager,company,manager,"Reversal policy"));
        type=await Use<LeaveLedger,Guid>(s=>s.CreateTypeAsync(manager,company,"Reversal leave","REV-TYPE",480,false));
        async Task<Guid> Correction(DateOnly date){return await Use<CorrectionWorkflow,Guid>(s=>s.SubmitAsync(self,policy,date,new DateTimeOffset(date.ToDateTime(new TimeOnly(8,0)),TimeSpan.Zero),0,"reversal test"));}
        Task<bool> DecideCorrection(Guid id,bool approve=true)=>Use<CorrectionWorkflow,bool>(s=>s.DecideAsync(manager,id,approve,approve?null:"rejected"));
        Task<Guid> ReverseCorrection(Guid id,Guid expected,Guid operation,string reason="reconsider",Guid? actor=null)=>Use<DecisionReversals,Guid>(s=>s.ReverseCorrectionAsync(actor??manager,id,expected,operation,reason));
        Task<Guid> CorrectionDecision(Guid id)=>Query(db=>db.CorrectionDecisions.Where(d=>d.RequestId==id).OrderByDescending(d=>d.Sequence).Select(d=>d.Id).FirstAsync());
        var correction=await Correction(day);await DecideCorrection(correction);var first=await CorrectionDecision(correction);var op=Guid.NewGuid();
        check(await Reject<UnauthorizedAccessException>(()=>ReverseCorrection(correction,first,op,actor:self)) &&
              await Reject<UnauthorizedAccessException>(()=>ReverseCorrection(correction,first,op,actor:outsider)) &&
              await Reject<UnauthorizedAccessException>(()=>ReverseCorrection(correction,first,op,actor:duplicate)),"reversal correction requires independent right, assignment, current scope and no same-employee actor");
        check(await Reject<ArgumentException>(()=>ReverseCorrection(correction,first,op," ")) &&
              await Reject<ArgumentException>(()=>ReverseCorrection(correction,first,op,new string('x',501))) &&
              await Reject<ArgumentException>(()=>ReverseCorrection(correction,first,Guid.Empty)),"reversal requires bounded reason and nonempty operation key");
        var before=await Query(db=>db.CalculationInputRevisions.Select(r=>r.Revision).SingleAsync());
        check(await ReverseCorrection(correction,first,op)==op && await Query(db=>db.CorrectionRequests.Where(r=>r.Id==correction).Select(r=>r.State).SingleAsync())==0 &&
              await Query(db=>db.CalculationInputRevisions.Select(r=>r.Revision).SingleAsync())>before,"reversal correction reopens atomically and invalidates calculation revision");
        check(await ReverseCorrection(correction,first,op)==op && await Query(db=>db.CorrectionDecisionReversals.CountAsync(v=>v.DecisionId==first))==1 &&
              await Reject<DomainConflictException>(()=>ReverseCorrection(correction,first,Guid.NewGuid())) &&
              await Reject<DomainConflictException>(()=>ReverseCorrection(correction,first,op,"changed payload")),"reversal same operation retries once while changed payload or duplicate operation conflicts");
        await DecideCorrection(correction,false);var second=await CorrectionDecision(correction);
        check(second!=first && await Query(db=>db.CorrectionDecisions.CountAsync(d=>d.RequestId==correction))==2 &&
              await Reject<DomainConflictException>(()=>ReverseCorrection(correction,first,Guid.NewGuid())),"reversal correction redecision retains old evidence and stale expected decision cannot undo new cycle");
        await ReverseCorrection(correction,second,Guid.NewGuid());
        check(await Reject<DomainConflictException>(()=>Use<CorrectionWorkflow,bool>(s=>s.EditAsync(self,correction,DateTimeOffset.UtcNow,0,"must preserve"))) &&
              await Use<CorrectionWorkflow,bool>(s=>s.CancelAsync(self,correction,"cancel after reversal")),"reversal correction historical payload remains immutable but requester may cancel reopened request");
        var history=JsonSerializer.SerializeToElement(await Use<DecisionReversals,object>(s=>s.OwnCorrectionHistoryAsync(self,correction,0)));
        check(history.GetProperty("items").GetArrayLength()==3 &&
              await Reject<UnauthorizedAccessException>(()=>Use<DecisionReversals,object>(s=>s.OwnCorrectionHistoryAsync(outsider,correction,0))),"reversal correction full own history includes all cycles and blocks foreign request id");
        await SqlReject($"UPDATE dbo.CorrectionDecisionReversals SET Reason=N'changed' WHERE Id='{op}'",51010);
        await SqlReject($"UPDATE dbo.CorrectionDecisions SET Reason=N'changed' WHERE Id='{first}'",51007);
        check(true,"reversal database protects correction decision and reversal evidence from mutation");
        var concurrent=await Correction(day.AddDays(1));await DecideCorrection(concurrent);var concurrentDecision=await CorrectionDecision(concurrent);var concurrentOp=Guid.NewGuid();
        async Task<bool> AttemptCorrection(Guid operation){try{await ReverseCorrection(concurrent,concurrentDecision,operation);return true;}catch(DomainConflictException){return false;}}
        await Task.WhenAll(AttemptCorrection(concurrentOp),AttemptCorrection(concurrentOp));
        await ReverseCorrection(concurrent,concurrentDecision,concurrentOp); // deliberate complete retry after a possible SQL deadlock victim
        check(await Query(db=>db.CorrectionDecisionReversals.CountAsync(v=>v.DecisionId==concurrentDecision))==1,"reversal concurrent identical correction retries commit one transition");
        var guarded=await Correction(day.AddDays(2));await DecideCorrection(guarded);var guardedDecision=await CorrectionDecision(guarded);
        await Use<PeriodDrafts,Guid>(s=>s.CreateAsync(manager,employee,day.AddDays(2),day.AddDays(3)));
        check(await Reject<DomainConflictException>(()=>ReverseCorrection(guarded,guardedDecision,Guid.NewGuid())) &&
              await Query(db=>db.CorrectionRequests.Where(r=>r.Id==guarded).Select(r=>r.State).SingleAsync())==1,"reversal conservatively refuses period draft overlap without pretending drafts are closed periods");
        var permission=await Query(db=>db.Permissions.Where(p=>p.Code=="correction.reverse").Select(p=>p.Id).SingleAsync());
        await Query(db=>db.RolePermissions.Where(p=>p.RoleId==role&&p.PermissionId==permission).ExecuteDeleteAsync());
        check(await Reject<UnauthorizedAccessException>(()=>Use<DecisionReversals,object>(s=>s.ListCorrectionsAsync(manager,0))) &&
              await Reject<UnauthorizedAccessException>(()=>ReverseCorrection(concurrent,concurrentDecision,concurrentOp)),"reversal revoked permission also denies idempotent replay in next request");
        async Task<Guid> SubmitLeave(int kind=0,int category=0,DateOnly? date=null){var d=date??day;var at=new DateTimeOffset(d.ToDateTime(new TimeOnly(9,0)),TimeSpan.Zero);
            return await Use<LeaveRequests,Guid>(s=>s.SubmitAsync(self,Guid.NewGuid(),type,category,kind,d,kind==1?at:null,kind==1?at.AddMinutes(60):null,"leave reversal"));}
        Task<object> DecideLeave(Guid id,int decision=1)=>Use<LeaveRequests,object>(s=>s.DecideAsync(manager,id,decision,null));
        Task<Guid> LeaveDecision(Guid id)=>Query(db=>db.LeaveDecisions.Where(d=>d.RequestId==id).OrderByDescending(d=>d.Sequence).Select(d=>d.Id).FirstAsync());
        Task<Guid> ReverseLeave(Guid id,Guid expected,Guid operation,Guid? actor=null)=>Use<DecisionReversals,Guid>(s=>s.ReverseLeaveAsync(actor??manager,id,expected,operation,"leave reconsider",default));
        var daily=await SubmitLeave();await DecideLeave(daily);var dailyDecision=await LeaveDecision(daily);var dailyOp=Guid.NewGuid();
        check(await Reject<UnauthorizedAccessException>(()=>ReverseLeave(daily,dailyDecision,dailyOp,self)) &&
              await Reject<UnauthorizedAccessException>(()=>ReverseLeave(daily,dailyDecision,dailyOp,outsider)) &&
              await Reject<UnauthorizedAccessException>(()=>ReverseLeave(daily,dailyDecision,dailyOp,duplicate)),"reversal leave denies missing right, foreign scope and same employee account");
        check(await Query(db=>db.LeaveLedgerEntries.Where(l=>l.LeaveTypeId==type).SumAsync(l=>l.Minutes))==-480,"reversal daily approval posts negative balance usage before compensation");
        await ReverseLeave(daily,dailyDecision,dailyOp);await ReverseLeave(daily,dailyDecision,dailyOp);
        check(await Query(db=>db.LeaveLedgerEntries.Where(l=>l.LeaveTypeId==type).SumAsync(l=>l.Minutes))==0 &&
              await Query(db=>db.LeaveDecisionReversals.CountAsync(v=>v.DecisionId==dailyDecision))==1,"reversal daily compensation restores negative balance exactly once including retry");
        await DecideLeave(daily);var dailySecond=await LeaveDecision(daily);
        check(await Query(db=>db.LeaveDecisions.CountAsync(d=>d.RequestId==daily))==2 &&
              await Query(db=>db.LeaveLedgerEntries.Where(l=>l.LeaveTypeId==type).SumAsync(l=>l.Minutes))==-480 &&
              await Reject<DomainConflictException>(()=>ReverseLeave(daily,dailyDecision,Guid.NewGuid())),"reversal leave reapproval posts new linked usage once and rejects stale reversal");
        await ReverseLeave(daily,dailySecond,Guid.NewGuid());
        var hourly=await SubmitLeave(1);await DecideLeave(hourly);var hourlyDecision=await LeaveDecision(hourly);var hourlyOp=Guid.NewGuid();
        await Task.WhenAll(AttemptLeave(hourlyOp),AttemptLeave(hourlyOp));await ReverseLeave(hourly,hourlyDecision,hourlyOp);
        async Task<bool> AttemptLeave(Guid operation){try{await ReverseLeave(hourly,hourlyDecision,operation);return true;}catch(DomainConflictException){return false;}}
        check(await Query(db=>db.LeaveLedgerEntries.Where(l=>l.LeaveTypeId==type).SumAsync(l=>l.Minutes))==0 &&
              await Query(db=>db.LeaveDecisionReversals.CountAsync(v=>v.DecisionId==hourlyDecision))==1,"reversal concurrent hourly retries restore precisely sixty minutes once");
        var posted=await Query(db=>(from v in db.LeaveDecisionReversals join l in db.LeaveLedgerEntries on v.CompensationLedgerEntryId equals (Guid?)l.Id
            where v.DecisionId==hourlyDecision select new{l.Kind,l.Minutes,l.PersianYear,l.EffectiveDate}).SingleAsync());
        check(posted.Kind==3&&posted.Minutes==60&&posted.EffectiveDate==day&&posted.PersianYear==IO.Domain.Scheduling.PersianDates.FromGregorian(day).Year,"reversal compensation preserves effective date, Persian year and immutable signed adjustment semantics");
        var rejection=await SubmitLeave(1);await DecideLeave(rejection,2);var rejectionDecision=await LeaveDecision(rejection);
        await ReverseLeave(rejection,rejectionDecision,Guid.NewGuid());
        check(await Query(db=>db.LeaveDecisionReversals.Where(v=>v.DecisionId==rejectionDecision).AllAsync(v=>v.CompensationLedgerEntryId==null)),"reversal rejected leave reopens with no invented ledger refund");
        var overtime=await SubmitLeave(1,1);await DecideLeave(overtime);var overtimeDecision=await LeaveDecision(overtime);await ReverseLeave(overtime,overtimeDecision,Guid.NewGuid());
        var mission=await SubmitLeave(0,2);await DecideLeave(mission);var missionDecision=await LeaveDecision(mission);await ReverseLeave(mission,missionDecision,Guid.NewGuid());
        check(await Query(db=>db.LeaveDecisionReversals.Where(v=>v.DecisionId==overtimeDecision||v.DecisionId==missionDecision).AllAsync(v=>v.CompensationLedgerEntryId==null)),"reversal overtime and mission reopen without leave consumption or refund");
        var cancelled=await SubmitLeave();await Use<LeaveRequests,object>(s=>s.CancelOwnAsync(self,cancelled,null));var cancelDecision=await LeaveDecision(cancelled);
        check(await Reject<DomainConflictException>(()=>ReverseLeave(cancelled,cancelDecision,Guid.NewGuid())),"reversal requester cancellation cannot be reversed as manager approval");
        var periodLeave=await SubmitLeave(date:day.AddDays(2));await DecideLeave(periodLeave);var periodDecision=await LeaveDecision(periodLeave);
        var countBefore=await Query(db=>db.LeaveLedgerEntries.CountAsync(l=>l.LeaveTypeId==type));
        check(await Reject<DomainConflictException>(()=>ReverseLeave(periodLeave,periodDecision,Guid.NewGuid())) &&
              await Query(db=>db.LeaveLedgerEntries.CountAsync(l=>l.LeaveTypeId==type))==countBefore,"reversal protected leave period rejects atomically with no compensation");
        await SqlReject($"UPDATE dbo.LeaveDecisionReversals SET Reason=N'changed' WHERE Id='{dailyOp}'",51010);
        await SqlReject($"UPDATE dbo.LeaveDecisions SET Reason=N'changed' WHERE Id='{dailyDecision}'",51009);
        check(true,"reversal database protects leave decisions and compensation links from mutation");
        check(await Query(db=>db.Notifications.CountAsync(n=>n.UserId==self&&n.Text.Contains("بازگردانده")))>=8 &&
              JsonSerializer.SerializeToElement(await Use<DecisionReversals,object>(s=>s.OwnLeaveHistoryAsync(self,daily,0))).GetProperty("items").GetArrayLength()==2,
              "reversal requester receives persisted notifications and complete leave decision history");
        var racing=await SubmitLeave(1);await DecideLeave(racing);var racingDecision=await LeaveDecision(racing);
        async Task<bool> RaceLeave(Guid operation){try{await ReverseLeave(racing,racingDecision,operation);return true;}catch(DomainConflictException){return false;}}
        var racingResults=await Task.WhenAll(RaceLeave(Guid.NewGuid()),RaceLeave(Guid.NewGuid()));
        // A deadlock victim is a conflict, not an automatic retry or inferred success.
        check(racingResults.Count(x=>x)==1&&await Query(db=>db.LeaveDecisionReversals.CountAsync(v=>v.DecisionId==racingDecision))==1,
              "reversal competing distinct leave operation keys cannot produce double compensation");
        await DecideLeave(racing);var afterReplay=await Query(db=>db.LeaveLedgerEntries.CountAsync(l=>l.LeaveTypeId==type));
        await ReverseLeave(daily,dailyDecision,dailyOp);
        check(await Query(db=>db.LeaveLedgerEntries.CountAsync(l=>l.LeaveTypeId==type))==afterReplay&&await Query(db=>db.LeaveRequests.Where(r=>r.Id==racing).Select(r=>r.State).SingleAsync())==1,
              "reversal replay of historical successful operation cannot undo another cycle or post new ledger entry");
        var failing=await SubmitLeave(1);await DecideLeave(failing);var failingDecision=await LeaveDecision(failing);var failingOperation=Guid.NewGuid();
        await Query(async db=>await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER dbo.TR_ReversalTest_NotificationFailure ON dbo.Notifications AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Text LIKE N'%forced rollback%') THROW 51099, 'Injected disposable notification failure', 1; END"));
        try
        {
            var failed=false;
            try{await Use<DecisionReversals,Guid>(r=>r.ReverseLeaveAsync(manager,failing,failingDecision,failingOperation,"forced rollback"));}
            catch(DbUpdateException e)when(e.InnerException is SqlException sql&&sql.Number==51099){failed=true;}
            check(failed&&await Query(db=>db.LeaveRequests.Where(r=>r.Id==failing).Select(r=>r.State).SingleAsync())==1&&
                  !await Query(db=>db.LeaveDecisionReversals.AnyAsync(v=>v.Id==failingOperation))&&
                  !await Query(db=>db.LeaveLedgerEntries.AnyAsync(l=>l.DocumentNumber=="REV-"+failingOperation.ToString("N"))),
                  "reversal final-save failure rolls back request transition, prior reversal save and compensation together");
        }
        finally{await Query(async db=>await db.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.TR_ReversalTest_NotificationFailure"));}
        var allOwn=JsonSerializer.SerializeToElement(await Use<LeaveRequests,object>(s=>s.ListOwnAsync(self,0))).GetProperty("items");
        check(allOwn.EnumerateArray().Count(x=>x.GetProperty("Id").GetGuid()==daily)==1,"reversal leave own list does not multiply rows by historical decisions");
    }
}
