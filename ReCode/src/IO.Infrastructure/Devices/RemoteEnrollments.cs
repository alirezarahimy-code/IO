using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace IO.Infrastructure.Devices;

/// <summary>Durable single-dispatch queue. No biometric bytes in SQL. Real enrollment is default-deny at BOTH web and worker.</summary>
public sealed class RemoteEnrollments(IoDbContext db,PermissionTransaction tx,TimeProvider clock,IDataProtectionProvider protection,IConfiguration config)
{
    public const string Request="devices.enrollment.request",Read="devices.enrollment.read",Execute="devices.enrollment.execute";
    public const string Confirmation="ENROLL-NEW-FINGER-NO-OVERWRITE";
    public sealed record Input(Guid Id,Guid DeviceId,Guid EmployeeId,byte[] DeviceVersion,string PersonIdentifier,RemoteEnrollApproval Approval,int TimeoutSeconds,string Confirmation)
    {public override string ToString()=>"Enrollment intent [subject and policy redacted]";}
    public sealed record Claim(Guid Id,Guid Token,Guid DeviceId,string Host,int Port,string PersonIdentifier,RemoteEnrollApproval Approval,int TimeoutSeconds)
    {public override string ToString()=>"Enrollment claim [subject redacted]";}
    private bool Approved(RemoteEnrollApproval approval)=>approval.IsValid()&&approval.EmptySlotErrorCode is not null and not 0 and not int.MinValue&&approval.EventMask>0&&config.GetValue<bool>("IO:RemoteEnroll:EnableRequests")&&
        (config.GetSection("IO:RemoteEnroll:Approvals").Get<RemoteEnrollApproval[]>()??[]).Count(x=>x==approval)==1;
    public Task<Guid> RequestAsync(Guid actor,Input input,CancellationToken ct=default)
    {
        if(input is null||input.Id==Guid.Empty||input.DeviceId==Guid.Empty||input.EmployeeId==Guid.Empty||input.DeviceVersion is not{Length:8}||input.PersonIdentifier is not{Length:>=1 and <=100}||!input.PersonIdentifier.All(char.IsAsciiDigit)||input.Approval is null||input.TimeoutSeconds is <10 or >60||input.Confirmation!=Confirmation)throw new ArgumentException("Exact approved intent required.");
        input=input with{DeviceVersion=input.DeviceVersion.ToArray()};
        return tx.ExecuteAsync(actor,Request,"device-enrollment.requested",async token=>
        {
            await tx.LockAsync("IO.DeviceConnections",token);
            if(!Approved(input.Approval))throw new DomainConflictException("Enrollment is disabled or the exact model and biometric policy are not approved.");
            var json=JsonSerializer.Serialize(input.Approval);
            var old=await db.RemoteEnrollments.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.Id,token);
            if(old is not null)
            {
                if(old.RequestedByUserId!=actor||old.DeviceId!=input.DeviceId||old.EmployeeId!=input.EmployeeId||old.PersonIdentifier!=input.PersonIdentifier||old.ApprovalJson!=json||old.TimeoutSeconds!=input.TimeoutSeconds||!old.ExpectedDeviceVersion.SequenceEqual(input.DeviceVersion))throw new DomainConflictException("Changed enrollment retry.");
                return(old.Id,old.Id);
            }
            var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.DeviceId,token)??throw new ArgumentException("Unknown device.");
            if(!device.Enabled||device.Profile!="zk-standalone"||!device.Version.SequenceEqual(input.DeviceVersion)||!await Mapped(device.SourceId,input.EmployeeId,input.PersonIdentifier,token))throw new DomainConflictException("Unchanged standalone installation and exact current employee mapping required.");
            await EnrollmentFence.EnsureAsync(db,device.Id,true,token);
            if(await DeviceWriteSafety.UnreviewedAsync(db,device.Id,token)||await Busy(device.Id,token))throw new DomainConflictException("Pause polling and review/finish all device work before enrollment.");
            db.RemoteEnrollments.Add(new(){Id=input.Id,DeviceId=device.Id,SourceId=device.SourceId,EmployeeId=input.EmployeeId,RequestedByUserId=actor,RequestedAtUtc=clock.GetUtcNow(),ExpectedDeviceVersion=input.DeviceVersion,PersonIdentifier=input.PersonIdentifier,ApprovalJson=json,TimeoutSeconds=input.TimeoutSeconds});
            return(input.Id,input.Id);
        },ct);
    }
    public Task<object> ListAsync(Guid actor,Guid? device,int offset,CancellationToken ct=default)=>tx.ExecuteAsync<object>(actor,Read,"device-enrollment.viewed",async token=>
    {
        if(offset is <0 or >100000)throw new ArgumentException("Invalid page.");
        var rows=await db.RemoteEnrollments.AsNoTracking().Where(x=>device==null||x.DeviceId==device).OrderByDescending(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50).Select(x=>new{x.Id,x.DeviceId,x.EmployeeId,x.PersonIdentifier,x.State,x.RequestedAtUtc,x.ClaimedAtUtc,x.CancelRequestedAtUtc,x.FinishedAtUtc,x.Version}).ToListAsync(token);
        return(new{items=rows,offset,pageSize=50},actor);
    },ct);
    public Task<Guid> CancelAsync(Guid actor,Guid id,byte[] version,CancellationToken ct=default)=>tx.ExecuteAsync(actor,Request,"device-enrollment.cancel-requested",async token=>
    {
        await tx.LockAsync("IO.DeviceConnections",token);var row=await db.RemoteEnrollments.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new ArgumentException("Unknown intent.");
        if(version is not{Length:8}||!row.Version.SequenceEqual(version))throw new DomainConflictException("Refresh job version.");
        if(row.State=="pending"){row.State="cancelled-before-dispatch";row.FinishedAtUtc=clock.GetUtcNow();}
        else if(row.State=="running")row.CancelRequestedAtUtc??=clock.GetUtcNow();
        else throw new DomainConflictException("Job already terminal; unknown enrollment must never be replayed.");
        return(id,id);
    },ct);
    public Task<Claim?> ClaimAsync(Guid service,CancellationToken ct=default)=>tx.ExecuteAsync<Claim?>(service,Execute,"device-enrollment.claimed",async token=>
    {
        await tx.LockAsync("IO.DeviceConnections",token);var now=clock.GetUtcNow();
        var expired=await db.RemoteEnrollments.AsTracking().Where(x=>x.State=="running"&&x.ClaimedAtUtc<now.AddMinutes(-2)).ToListAsync(token);
        foreach(var row in expired){row.State="unknown";row.FinishedAtUtc=now;}
        var next=await db.RemoteEnrollments.AsTracking().Where(x=>x.State=="pending").OrderBy(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(token);
        if(next is null)return(null,service);
        var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==next.DeviceId,token);var approval=JsonSerializer.Deserialize<RemoteEnrollApproval>(next.ApprovalJson)!;
        if(!Approved(approval)||!await tx.HasDevicePermissionAsync(next.RequestedByUserId,Request,next.DeviceId,token)){next.State="revoked";next.FinishedAtUtc=now;return(null,next.Id);}
        if(now-next.RequestedAtUtc>TimeSpan.FromMinutes(2)||!device.Enabled||device.Profile!="zk-standalone"||device.Host is null||device.Port is null||!device.Version.SequenceEqual(next.ExpectedDeviceVersion)||device.SourceId!=next.SourceId||!await Mapped(next.SourceId,next.EmployeeId,next.PersonIdentifier,token)){next.State="device-changed";next.FinishedAtUtc=now;return(null,next.Id);}
        if(await db.RemoteEnrollments.AnyAsync(x=>x.DeviceId==device.Id&&x.Id!=next.Id&&(x.State=="pending"||x.State=="running"||x.State=="unknown"&&!db.RemoteEnrollmentRecoveries.Any(r=>r.EnrollmentId==x.Id&&r.ApprovedAtUtc!=null)),token)||await Busy(device.Id,token)||await DeviceWriteSafety.UnreviewedAsync(db,device.Id,token))return(null,next.Id);
        next.State="running";next.ClaimId=Guid.NewGuid();next.ServiceUserId=service;next.ClaimedAtUtc=now;
        return(new Claim(next.Id,next.ClaimId.Value,next.DeviceId,device.Host,device.Port.Value,next.PersonIdentifier,approval,next.TimeoutSeconds),next.Id);
    },ct);
    public Task<DeviceCommunicationKey?> CredentialAsync(Guid service,Claim claim,bool allowDefault,CancellationToken ct=default)=>tx.ExecuteAsync<DeviceCommunicationKey?>(service,Execute,"device-enrollment.credential-used",async token=>
    {
        await tx.LockAsync("IO.DeviceConnections",token);var row=await Owned(service,claim.Id,claim.Token,token);
        if(!await Current(row,token)||row.PersonIdentifier!=claim.PersonIdentifier||JsonSerializer.Deserialize<RemoteEnrollApproval>(row.ApprovalJson)!=claim.Approval||row.TimeoutSeconds!=claim.TimeoutSeconds)return(null,row.Id);
        var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==row.DeviceId,token);
        if(device.Host!=claim.Host||device.Port!=claim.Port||device.Id!=claim.DeviceId)return(null,row.Id);
        var secret=await db.DeviceSecrets.Where(x=>x.DeviceId==row.DeviceId).Select(x=>x.ProtectedValue).SingleOrDefaultAsync(token);
        if(secret is null)return(allowDefault?new DeviceCommunicationKey(0):null,row.Id);
        try{var text=protection.CreateProtector("IO.Devices.Credential.v1",row.DeviceId.ToString("N")).Unprotect(secret);return(text.Length>0&&text.All(char.IsAsciiDigit)&&int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var key)?new DeviceCommunicationKey(key):null,row.Id);}catch(CryptographicException){return(null,row.Id);}
    },ct);
    public Task<bool> ShouldStopAsync(Guid service,Claim claim,CancellationToken ct=default)=>tx.ExecuteAsync(service,Execute,"device-enrollment.authority-checked",async token=>
    {await tx.LockAsync("IO.DeviceConnections",token);var row=await Owned(service,claim.Id,claim.Token,token);return(!await Current(row,token),row.Id);},ct);
    public Task<Guid> CompleteAsync(Guid service,Claim claim,RemoteEnrollOutput result,CancellationToken ct=default)=>tx.ExecuteAsync(service,Execute,"device-enrollment.finished",async token=>
    {
        await tx.LockAsync("IO.DeviceConnections",token);var row=await Owned(service,claim.Id,claim.Token,token);
        var approval=JsonSerializer.Deserialize<RemoteEnrollApproval>(row.ApprovalJson)!;
        var validation=new RemoteEnrollInput(new(row.DeviceId,claim.Host,claim.Port,1,approval.Serial,0),row.Id,row.PersonIdentifier,approval,Math.Min(45,row.TimeoutSeconds-5));
        if(result is null||!result.IsValidFor(validation))throw new ArgumentException("Invalid correlated enrollment result.");
        // A revoke/cancel racing a completion cannot manufacture a successful result.
        row.State=await Current(row,token)?result.State:"unknown";row.FinishedAtUtc=clock.GetUtcNow();
        row.ResultJson=row.State=="completed"?JsonSerializer.Serialize(result.Completion):null;
        return(row.Id,row.Id);
    },ct);
    private async Task<RemoteEnrollmentRecord> Owned(Guid service,Guid id,Guid token,CancellationToken ct)
    {var row=await db.RemoteEnrollments.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ArgumentException("Unknown job.");if(row.State!="running"||row.ServiceUserId!=service||row.ClaimId!=token||row.ClaimedAtUtc<clock.GetUtcNow().AddMinutes(-2))throw new DomainConflictException("Expired or foreign enrollment claim.");return row;}
    private async Task<bool> Current(RemoteEnrollmentRecord row,CancellationToken ct)
    {
        var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==row.DeviceId,ct);
        return row.CancelRequestedAtUtc is null&&Approved(JsonSerializer.Deserialize<RemoteEnrollApproval>(row.ApprovalJson)!)&&clock.GetUtcNow()-row.RequestedAtUtc<=TimeSpan.FromMinutes(2)&&device.Enabled&&device.Profile=="zk-standalone"&&device.SourceId==row.SourceId&&device.Version.SequenceEqual(row.ExpectedDeviceVersion)&&await tx.HasDevicePermissionAsync(row.RequestedByUserId,Request,row.DeviceId,ct)&&await Mapped(row.SourceId,row.EmployeeId,row.PersonIdentifier,ct)&&!await Busy(row.DeviceId,ct)&&!await DeviceWriteSafety.UnreviewedAsync(db,row.DeviceId,ct);
    }
    private async Task<bool> Busy(Guid device,CancellationToken ct)=>await db.DeviceProtectedErases.IgnoreQueryFilters().AnyAsync(x=>x.DeviceId==device&&(x.State=="proposed"||x.State=="pending"||x.State=="running"||x.State=="observed"),ct)||
        await db.DeviceConnections.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceOperations.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceUserDownloads.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"||x.State=="unknown"&&!db.RemoteEnrollmentRecoveries.Any(r=>r.EnrollmentId==x.Id&&r.ApprovedAtUtc!=null)&&x.ResolutionSnapshotId==null),ct)||await db.DeviceAttendanceDownloads.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DevicePollSchedules.AnyAsync(x=>x.DeviceId==device&&x.Enabled,ct);
    private async Task<bool> Mapped(Guid source,Guid employee,string person,CancellationToken ct)
    {
        var bytes=Encoding.Unicode.GetBytes(person);var now=clock.GetUtcNow();
        var matches=await db.SourceEmployeeMappings.Where(x=>x.SourceId==source&&EF.Property<byte[]>(x,"SourcePersonIdBytes")==bytes&&EF.Property<int>(x,"SourcePersonIdByteLength")==bytes.Length&&x.ValidFromUtc<=now&&(x.ValidUntilUtcExclusive==null||now<x.ValidUntilUtcExclusive)).Select(x=>new{x.EmployeeId,x.CompanyId}).Take(2).ToListAsync(ct);
        return matches.Count==1&&matches[0].EmployeeId==employee&&await db.Employees.AnyAsync(x=>x.Id==employee&&x.CompanyId==matches[0].CompanyId,ct);
    }
}
internal static class EnrollmentFence
{
    internal static async Task EnsureAsync(IoDbContext db,Guid device,bool writing,CancellationToken ct)
    {
        await AllDataEraseFence.EnsureAsync(db,device,writing,ct);
        await AttendanceEraseFence.EnsureAsync(db,device,writing,ct);
        await BiometricTransferFence.EnsureAsync(db,device,writing,ct);
        if(await db.RemoteEnrollments.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"||writing&&x.State=="unknown"&&!db.RemoteEnrollmentRecoveries.Any(r=>r.EnrollmentId==x.Id&&r.ApprovedAtUtc!=null)),ct))throw new DomainConflictException("Enrollment holds the exclusive device or has an unknown write; no overwrite or replay.");
    }
}
