using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

internal static class DecisionReversalUpgradeTests
{
    // Called only after Program's disposable-CI/loopback guard. This DB never accepts caller-selected hosts.
    public static async Task RunAsync(string connection, Action<bool,string> check)
    {
        var builder=new SqlConnectionStringBuilder(connection){InitialCatalog="IO_ReversalUpgrade_"+Guid.NewGuid().ToString("N")};
        await using var db=new IoDbContext(new DbContextOptionsBuilder<IoDbContext>().UseSqlServer(builder.ConnectionString).Options);
        try
        {
            var migrator=db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261110060000_Notifications");
            var org=Guid.NewGuid();var company=Guid.NewGuid();var emp=Guid.NewGuid();var user=Guid.NewGuid();var manager=Guid.NewGuid();var type=Guid.NewGuid();var policy=Guid.NewGuid();
            var day=new DateOnly(2027,1,10);var now=DateTimeOffset.UtcNow;
            db.Organizations.Add(new(){Id=org,Name="Legacy reversal upgrade"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Legacy reversal company"});
            db.Employees.Add(new(){Id=emp,CompanyId=company,PersonnelNumber="LEGACY-REV",DisplayName="Legacy reversal employee"});
            db.Users.AddRange(new UserRecord{Id=user,EmployeeId=emp,UserName="legacy-reversal-self",IsActive=true},new UserRecord{Id=manager,UserName="legacy-reversal-manager",IsActive=true});
            db.LeaveTypes.Add(new(){Id=type,CompanyId=company,Name="Legacy reversal type",Symbol="LEGACY-REV",MinutesPerDay=480,CarryOverAllowed=false,CreatedByUserId=manager,CreatedAtUtc=now});
            db.CorrectionPolicies.Add(new(){Id=policy,CompanyId=company,ApproverUserId=manager,Name="Legacy reversal policy",CreatedByUserId=manager,CreatedAtUtc=now});
            var leaveIds=new[]{Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()};var decisions=new[]{Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()};var original=Guid.NewGuid();
            for(var i=0;i<3;i++)db.LeaveRequests.Add(new(){Id=leaveIds[i],OperationId=Guid.NewGuid(),CompanyId=company,EmployeeId=emp,RequesterUserId=user,LeaveTypeId=type,Category=0,Kind=0,WorkDate=day,Minutes=480,Reason="old approval",State=0,CreatedAtUtc=now});
            var correction=Guid.NewGuid();var correctionDecision=Guid.NewGuid();
            db.CorrectionRequests.Add(new(){Id=correction,CompanyId=company,EmployeeId=emp,RequesterUserId=user,PolicyId=policy,WorkDate=day,ProposedAtUtc=new DateTimeOffset(2027,1,10,8,0,0,TimeSpan.Zero),PunchKind=0,Reason="old correction",State=0,CreatedAtUtc=now});
            for(var i=0;i<3;i++)db.LeaveLedgerEntries.Add(new(){Id=i==0?original:Guid.NewGuid(),CompanyId=company,EmployeeId=emp,LeaveTypeId=type,PersianYear=1405,EffectiveDate=day,Kind=2,Minutes=-480,DocumentNumber="LEAVE-"+leaveIds[i==0?0:1].ToString("N")[..12],Description="legacy consumption",CreatedByUserId=manager,CreatedAtUtc=now});
            await db.SaveChangesAsync();
            for(var i=0;i<3;i++)await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.LeaveDecisions(Id,RequestId,ActorUserId,Decision,Reason,AtUtc) VALUES({decisions[i]},{leaveIds[i]},{manager},{1},{"legacy"},{now}); UPDATE dbo.LeaveRequests SET State=1 WHERE Id={leaveIds[i]};");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.CorrectionDecisions(Id,RequestId,ActorUserId,Decision,Reason,AtUtc) VALUES({correctionDecision},{correction},{manager},{1},{"legacy"},{now}); UPDATE dbo.CorrectionRequests SET State=1 WHERE Id={correction};");
            db.ChangeTracker.Clear();await migrator.MigrateAsync();await migrator.MigrateAsync();
            var valid=await db.LeaveDecisions.AsNoTracking().SingleAsync(d=>d.Id==decisions[0]);
            check(valid.Sequence==1&&valid.LedgerEntryId==original&&await db.CorrectionDecisions.AllAsync(d=>d.Sequence==1)&&await db.LeaveLedgerEntries.CountAsync()==3,
                "reversal upgrade preserves legacy decision and usage evidence and links one exact original consumption");
            check(await db.LeaveDecisions.Where(d=>d.Id==decisions[1]||d.Id==decisions[2]).AllAsync(d=>d.LedgerEntryId==null),
                "reversal upgrade refuses to guess ambiguous or missing legacy ledger linkage");
            db.CorrectionDecisionReversals.Add(new(){Id=Guid.NewGuid(),DecisionId=correctionDecision,ActorUserId=manager,Reason="retained reversal evidence",AtUtc=now});await db.SaveChangesAsync();
            var refused=false;
            try{await migrator.MigrateAsync("20261110060000_Notifications");}catch(SqlException e)when(e.Number==51010){refused=true;}
            check(refused&&await db.CorrectionDecisionReversals.CountAsync()==1&&await db.CorrectionDecisions.CountAsync()==1,
                "reversal downgrade fails closed rather than deleting multi-cycle audit history");
        }
        finally{await db.Database.EnsureDeletedAsync();}
    }
}
