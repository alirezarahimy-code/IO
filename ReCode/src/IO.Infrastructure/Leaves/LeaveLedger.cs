using IO.Domain.Scheduling;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Leaves;

/// <summary>Experimental movement ledger, including workflow usage/compensation; not a statutory entitlement engine.</summary>
public sealed class LeaveLedger(IoDbContext db, PermissionTransaction transaction)
{
    public const string Manage = "leave.manage";
    public const string ReadSelf = "leave.read.self";
    public sealed record Balance(Guid LeaveTypeId, string Name, string Symbol, int MinutesPerDay, int PersianYear, DateOnly AsOf,
        long? OpeningMinutes, long GrantedMinutes, long UsedMinutes, long AdjustmentMinutes, long? BalanceMinutes);
    private static void Text(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length>max || value.Any(char.IsControl)) throw new ArgumentException("Invalid text.");
    }
    private static (int Year,int Month,int Day) PersianDate(DateOnly date)
    {
        var value=PersianDates.FromGregorian(date);
        if (value.Year is < 1300 or > 1500) throw new ArgumentException("Unsupported Persian year.");
        return value;
    }
    public Task<Guid> CreateTypeAsync(Guid actor, Guid companyId, string name, string symbol, int minutesPerDay, bool carryOverAllowed, CancellationToken ct=default) =>
        transaction.ExecuteAsync(actor,Manage,"leave.type.created",async token=>
        {
            Text(name,200);Text(symbol,40);
            if(companyId==Guid.Empty || minutesPerDay is < 1 or > 1440 || !await db.Companies.AnyAsync(x=>x.Id==companyId,token)) throw new ArgumentException("Invalid company or day length.");
            await transaction.LockAsync("IO.LeaveTypes."+companyId.ToString("N"),token);
            if(await db.LeaveTypes.AnyAsync(x=>x.CompanyId==companyId && x.Symbol==symbol,token)) throw new DomainConflictException("Symbol already exists for company.");
            var id=Guid.NewGuid();db.LeaveTypes.Add(new LeaveTypeRecord{Id=id,CompanyId=companyId,Name=name,Symbol=symbol,MinutesPerDay=minutesPerDay,
                CarryOverAllowed=carryOverAllowed,CreatedAtUtc=DateTimeOffset.UtcNow,CreatedByUserId=actor});
            return (id,id);
        },ct);
    public Task<object> ListTypesAsync(Guid actor,Guid companyId,int offset,CancellationToken ct=default) =>
        transaction.ExecuteAsync<object>(actor,Manage,"leave.types.viewed",async token=>
        {
            if(companyId==Guid.Empty || offset is < 0 or > 100000 || !await db.Companies.AnyAsync(x=>x.Id==companyId,token)) throw new ArgumentException("Invalid company or page.");
            var items=await db.LeaveTypes.AsNoTracking().Where(x=>x.CompanyId==companyId).OrderBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50)
                .Select(x=>new{x.Id,x.Name,x.Symbol,x.MinutesPerDay,x.CarryOverAllowed,x.CreatedAtUtc}).ToListAsync(token);
            return (new {companyId,offset,pageSize=50,items},companyId);
        },ct);
    public Task<object> ListOwnTypesAsync(Guid actor,int offset,CancellationToken ct=default) =>
        transaction.ExecuteEitherAsync<object>(actor,ReadSelf,LeaveRequests.Submit,"leave.own-types.viewed",async token=>
        {
            if(offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var employee=await (from user in db.Users join emp in db.Employees on user.EmployeeId equals (Guid?)emp.Id
                                where user.Id==actor && user.IsActive select new{emp.Id,emp.CompanyId}).SingleOrDefaultAsync(token)
                ?? throw new UnauthorizedAccessException();
            var items=await db.LeaveTypes.AsNoTracking().Where(x=>x.CompanyId==employee.CompanyId).OrderBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50)
                .Select(x=>new{x.Id,x.Name,x.Symbol,x.MinutesPerDay,x.CarryOverAllowed}).ToListAsync(token);
            return (new {offset,pageSize=50,items},employee.Id);
        },ct);
    public Task<Guid> AddEntryAsync(Guid actor,Guid operationId,Guid employeeId,Guid leaveTypeId,DateOnly effectiveDate,int kind,int minutes,
        string documentNumber,string description,CancellationToken ct=default) =>
        transaction.ExecuteAsync(actor,Manage,"leave.entry.recorded",async token=>
        {
            Text(documentNumber,80);Text(description,500);
            var date=PersianDate(effectiveDate);
            if(operationId==Guid.Empty || employeeId==Guid.Empty || leaveTypeId==Guid.Empty || minutes is < -527040 or > 527040 ||
                !((kind==0 && minutes>=0 && date.Month==1 && date.Day==1) || (kind==1 && minutes>0) || (kind==2 && minutes<0) || (kind==3 && minutes!=0)))
                throw new ArgumentException("Invalid movement or opening date.");
            await transaction.LockAsync("IO.LeaveOperation."+operationId.ToString("N"),token);
            var existing=await db.LeaveLedgerEntries.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==operationId,token);
            if(existing is not null)
            {
                if(existing.EmployeeId!=employeeId || existing.LeaveTypeId!=leaveTypeId || existing.EffectiveDate!=effectiveDate || existing.Kind!=kind || existing.Minutes!=minutes ||
                    existing.CreatedByUserId!=actor || !string.Equals(existing.DocumentNumber,documentNumber,StringComparison.Ordinal) || !string.Equals(existing.Description,description,StringComparison.Ordinal))
                    throw new DomainConflictException("Operation identifier already used for different movement.");
                return (operationId,operationId);
            }
            var company=await db.Employees.Where(x=>x.Id==employeeId).Select(x=>(Guid?)x.CompanyId).SingleOrDefaultAsync(token);
            if(company is null || !await db.LeaveTypes.AnyAsync(x=>x.Id==leaveTypeId && x.CompanyId==company,token)) throw new ArgumentException("Employee and leave type must belong to same company.");
            await transaction.LockAsync("IO.LeaveOpening."+employeeId.ToString("N")+"."+leaveTypeId.ToString("N")+"."+date.Year,token);
            if(kind==0 && await db.LeaveLedgerEntries.AnyAsync(x=>x.EmployeeId==employeeId && x.LeaveTypeId==leaveTypeId && x.PersianYear==date.Year && x.Kind==0,token))
                throw new DomainConflictException("Opening already exists for this year.");
            db.LeaveLedgerEntries.Add(new LeaveLedgerEntryRecord{Id=operationId,CompanyId=company.Value,EmployeeId=employeeId,LeaveTypeId=leaveTypeId,
                PersianYear=date.Year,EffectiveDate=effectiveDate,Kind=kind,Minutes=minutes,DocumentNumber=documentNumber,Description=description,CreatedByUserId=actor,CreatedAtUtc=DateTimeOffset.UtcNow});
            return (operationId,operationId);
        },ct);
    public Task<object> ListEntriesAsync(Guid actor,Guid employeeId,Guid leaveTypeId,DateOnly asOf,int offset,CancellationToken ct=default) =>
        transaction.ExecuteAsync<object>(actor,Manage,"leave.entries.viewed",async token=>
        {
            if(offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var year=PersianDate(asOf).Year;await RequireEmployeeTypeAsync(employeeId,leaveTypeId,token);
            var items=await db.LeaveLedgerEntries.AsNoTracking().Where(x=>x.EmployeeId==employeeId && x.LeaveTypeId==leaveTypeId && x.PersianYear==year && x.EffectiveDate<=asOf)
                .OrderBy(x=>x.EffectiveDate).ThenBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50)
                .Select(x=>new{x.Id,x.EffectiveDate,x.Kind,x.Minutes,x.DocumentNumber,x.Description,x.CreatedAtUtc,x.CreatedByUserId}).ToListAsync(token);
            return (new {offset,pageSize=50,year,items},employeeId);
        },ct);
    public Task<Balance> GetBalanceAsync(Guid actor,Guid employeeId,Guid leaveTypeId,DateOnly asOf,CancellationToken ct=default) =>
        transaction.ExecuteAsync(actor,Manage,"leave.balance.viewed",async token=>
        { var result=await BalanceCoreAsync(employeeId,leaveTypeId,asOf,token);return (result,employeeId); },ct);
    public Task<Balance> GetOwnBalanceAsync(Guid actor,Guid leaveTypeId,DateOnly asOf,CancellationToken ct=default) =>
        transaction.ExecuteAsync(actor,ReadSelf,"leave.own-balance.viewed",async token=>
        {
            var employee=await db.Users.Where(x=>x.Id==actor && x.IsActive).Select(x=>x.EmployeeId).SingleAsync(token);
            if(employee is null) throw new UnauthorizedAccessException();
            var result=await BalanceCoreAsync(employee.Value,leaveTypeId,asOf,token);return (result,employee.Value);
        },ct);
    /// <summary>Company-wide balance report per employee and leave type (RemainLeave pattern).</summary>
    public Task<object> ListBalancesAsync(Guid actor,Guid companyId,DateOnly asOf,int offset,CancellationToken ct=default) =>
        transaction.ExecuteAsync<object>(actor,Manage,"leave.balances.viewed",async token=>
        {
            if(companyId==Guid.Empty || offset is < 0 or > 100000 || !await db.Companies.AnyAsync(x=>x.Id==companyId,token)) throw new ArgumentException("Invalid company or page.");
            int year=PersianDate(asOf).Year;
            var employees=await db.Employees.AsNoTracking().Where(x=>x.CompanyId==companyId)
                .OrderBy(x=>x.PersonnelNumber).ThenBy(x=>x.Id).Skip(offset).Take(50)
                .Select(x=>new{x.Id,x.PersonnelNumber,x.DisplayName}).ToListAsync(token);
            var ids=employees.Select(x=>x.Id).ToList();
            var sums=await db.LeaveLedgerEntries.AsNoTracking()
                .Where(x=>x.CompanyId==companyId && ids.Contains(x.EmployeeId) && x.PersianYear==year && x.EffectiveDate<=asOf)
                .GroupBy(x=>new{x.EmployeeId,x.LeaveTypeId}).Select(g=>new{g.Key.EmployeeId,g.Key.LeaveTypeId,
                    Opening=g.Where(e=>e.Kind==0).Sum(e=>(long?)e.Minutes),Granted=g.Where(e=>e.Kind==1).Sum(e=>(long?)e.Minutes),
                    Used=g.Where(e=>e.Kind==2).Sum(e=>(long?)e.Minutes),Adjustment=g.Where(e=>e.Kind==3).Sum(e=>(long?)e.Minutes)})
                .ToListAsync(token);
            var types=await db.LeaveTypes.AsNoTracking().Where(x=>x.CompanyId==companyId)
                .Select(x=>new{x.Id,x.Name,x.Symbol}).ToListAsync(token);
            var items=employees.Select(e=>new{e.Id,e.PersonnelNumber,e.DisplayName,
                balances=types.Select(t=>{var sum=sums.FirstOrDefault(x=>x.EmployeeId==e.Id && x.LeaveTypeId==t.Id);
                    long? opening=sum?.Opening;long granted=sum?.Granted??0;long used=-(sum?.Used??0);long adjustment=sum?.Adjustment??0;
                    return new{leaveTypeId=t.Id,name=t.Name,symbol=t.Symbol,opening,granted,used,adjustment,
                        balance=opening is null?(long?)null:opening+granted-used+adjustment};}).Where(b=>b.opening is not null || b.granted!=0 || b.used!=0 || b.adjustment!=0)}).ToList();
            return (new{companyId,asOf,offset,pageSize=50,items},companyId);
        },ct);
    private async Task<LeaveTypeRecord> RequireEmployeeTypeAsync(Guid employee,Guid type,CancellationToken token) =>
        await (from e in db.Employees.AsNoTracking() join t in db.LeaveTypes.AsNoTracking() on e.CompanyId equals t.CompanyId
               where e.Id==employee && t.Id==type select t).SingleOrDefaultAsync(token) ?? throw new ArgumentException("Unknown employee or leave type.");
    private async Task<Balance> BalanceCoreAsync(Guid employee,Guid typeId,DateOnly asOf,CancellationToken token)
    {
        int year=PersianDate(asOf).Year;var type=await RequireEmployeeTypeAsync(employee,typeId,token);
        var sums=await db.LeaveLedgerEntries.AsNoTracking().Where(x=>x.EmployeeId==employee && x.LeaveTypeId==typeId && x.PersianYear==year && x.EffectiveDate<=asOf)
            .GroupBy(x=>x.Kind).Select(g=>new{Kind=g.Key,Total=g.Sum(x=>(long)x.Minutes)}).ToListAsync(token);
        long? opening=sums.Where(x=>x.Kind==0).Select(x=>(long?)x.Total).SingleOrDefault();
        long granted=sums.Where(x=>x.Kind==1).Select(x=>x.Total).SingleOrDefault();
        long used=-sums.Where(x=>x.Kind==2).Select(x=>x.Total).SingleOrDefault();
        long adjustment=sums.Where(x=>x.Kind==3).Select(x=>x.Total).SingleOrDefault();
        return new Balance(type.Id,type.Name,type.Symbol,type.MinutesPerDay,year,asOf,opening,granted,used,adjustment,
            opening is null ? null : opening.Value+granted-used+adjustment);
    }
}
