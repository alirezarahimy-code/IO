using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using IO.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Settings;

/// <summary>Company-scoped, versioned settings for the experimental missing-punch workflow only.</summary>
public sealed class CorrectionSettings(IoDbContext db, PermissionTransaction transaction)
{
    public const string Manage = "settings.correction.manage";
    public static string LockName(Guid company) => "IO.CorrectionSettings."+company.ToString("N");

    public Task<object> ReadAsync(Guid actor,Guid company,int offset,CancellationToken ct=default) =>
        transaction.ExecuteAsync<object>(actor,Manage,"settings.correction.viewed",async token=>
        {
            if(company==Guid.Empty || offset is < 0 or > 100000 || !await db.Companies.AnyAsync(x=>x.Id==company,token)) throw new ArgumentException("Invalid company or page.");
            var current=await db.CorrectionSettings.AsNoTracking().Where(x=>x.CompanyId==company).OrderByDescending(x=>x.Revision)
                .Select(x=>new{x.Revision,x.MaxRequestAgeDays,x.DailyRequestLimit,x.MonthlyRequestLimit}).FirstOrDefaultAsync(token);
            var items=await db.CorrectionSettings.AsNoTracking().Where(x=>x.CompanyId==company).OrderByDescending(x=>x.Revision).Skip(offset).Take(50)
                .Select(x=>new{x.Id,x.Revision,x.MaxRequestAgeDays,x.DailyRequestLimit,x.MonthlyRequestLimit,x.Reason,x.CreatedByUserId,x.CreatedAtUtc}).ToListAsync(token);
            return (new {companyId=company,revision=current?.Revision??0,maxRequestAgeDays=current?.MaxRequestAgeDays??0,dailyRequestLimit=current?.DailyRequestLimit??0,monthlyRequestLimit=current?.MonthlyRequestLimit??0,configured=current is not null,offset,pageSize=50,items},company);
        },ct);

    public Task<object> ReadOwnAsync(Guid actor,CancellationToken ct=default) =>
        transaction.ExecuteAsync<object>(actor,CorrectionWorkflow.Submit,"settings.correction.own-viewed",async token=>
        {
            var company=await (from u in db.Users join e in db.Employees on u.EmployeeId equals (Guid?)e.Id
                               where u.Id==actor && u.IsActive select (Guid?)e.CompanyId).SingleOrDefaultAsync(token)
                        ?? throw new UnauthorizedAccessException();
            var current=await db.CorrectionSettings.AsNoTracking().Where(x=>x.CompanyId==company).OrderByDescending(x=>x.Revision)
                .Select(x=>new{x.Revision,x.MaxRequestAgeDays,x.DailyRequestLimit,x.MonthlyRequestLimit}).FirstOrDefaultAsync(token);
            return (new {maxRequestAgeDays=current?.MaxRequestAgeDays??0,dailyRequestLimit=current?.DailyRequestLimit??0,monthlyRequestLimit=current?.MonthlyRequestLimit??0,configured=current is not null},actor);
        },ct);

    public Task<Guid> SetAsync(Guid actor,Guid operation,Guid company,int expectedRevision,int maxDays,string reason,CancellationToken ct=default,int daily=0,int monthly=0) =>
        transaction.ExecuteAsync(actor,Manage,"settings.correction.updated",async token=>
        {
            if(operation==Guid.Empty || company==Guid.Empty || expectedRevision<0 || expectedRevision==int.MaxValue || maxDays is < 0 or > 3650 || daily is < 0 or > 1000 || monthly is < 0 or > 1000 ||
               string.IsNullOrWhiteSpace(reason) || reason.Length>500 || reason.Any(char.IsControl)) throw new ArgumentException("Invalid setting.");
            await transaction.LockAsync("IO.CorrectionSettingsOperation."+operation.ToString("N"),token);
            var prior=await db.CorrectionSettings.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==operation,token);
            if(prior is not null)
            {
                if(prior.CompanyId!=company || prior.Revision!=expectedRevision+1 || prior.MaxRequestAgeDays!=maxDays || prior.DailyRequestLimit!=daily || prior.MonthlyRequestLimit!=monthly || prior.CreatedByUserId!=actor || !string.Equals(prior.Reason,reason,StringComparison.Ordinal))
                    throw new DomainConflictException("Operation id already used.");
                return (operation,operation);
            }
            if(!await db.Companies.AnyAsync(x=>x.Id==company,token)) throw new ArgumentException("Unknown company.");
            await transaction.LockAsync(LockName(company),token);
            int current=await db.CorrectionSettings.Where(x=>x.CompanyId==company).MaxAsync(x=>(int?)x.Revision,token)??0;
            if(current!=expectedRevision) throw new DomainConflictException("Setting has changed; refresh before updating.");
            db.CorrectionSettings.Add(new CorrectionSettingsRecord{Id=operation,CompanyId=company,Revision=current+1,MaxRequestAgeDays=maxDays,DailyRequestLimit=daily,MonthlyRequestLimit=monthly,
                Reason=reason,CreatedByUserId=actor,CreatedAtUtc=DateTimeOffset.UtcNow});
            return (operation,operation);
        },ct);
}
