using System.Security.Cryptography;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Devices;

/// <summary>Authenticated gateway quarantine. Not native device authentication or attendance processing.</summary>
public sealed class PushGatewayIngress(IoDbContext db,PermissionTransaction tx,IDataProtectionProvider protection,TimeProvider clock)
{
    public sealed record Provision(Guid Id,Guid GatewayId,Guid DeviceId,Guid ServiceUserId,byte[] DeviceVersion,string Serial,string KeyVersion,string KeyBase64)
    { public override string ToString()=>"Gateway provisioning [secret redacted]"; }
    public Task<Guid> ProvisionAsync(Guid actor,Provision input,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if(input.Id==Guid.Empty||input.ServiceUserId==Guid.Empty||input.DeviceVersion is not{Length:8})throw new ArgumentException("Explicit binding, principal and device version required.");
        byte[] key;try{key=Convert.FromBase64String(input.KeyBase64);}catch(FormatException){throw new ArgumentException("Invalid gateway key.");}
        var version=(byte[])input.DeviceVersion.Clone();
        return Save();
        async Task<Guid> Save()
        {
            try{return await tx.ExecuteAsync(actor,"devices.credentials.manage","device-gateway.provisioned",async token=>
            {
                await Locks(token);
                if(!await tx.HasDevicePermissionAsync(actor,DevicePushJournal.Retain,input.DeviceId,token)||
                   !await tx.HasDevicePermissionAsync(input.ServiceUserId,DevicePushJournal.Retain,input.DeviceId,token))throw new UnauthorizedAccessException();
                var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.DeviceId,token)??throw new ArgumentException("Unknown device.");
                var zone=await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(token);
                if(!device.Enabled||device.Profile!="zk-push"||!device.Version.SequenceEqual(version))throw new DomainConflictException("Enabled unchanged PUSH device required.");
                var binding=new PushGatewayBinding(input.GatewayId,device.Id,device.SourceId,input.Serial,input.KeyVersion,TimeZoneInfo.FindSystemTimeZoneById(zone),key);
                var protector=Protector(input.Id);
                var old=await db.PushGatewayBindings.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.Id,token);
                if(old is not null)
                {
                    if(old.RevokedAtUtc is not null||old.CreatedByUserId!=actor||old.GatewayId!=binding.GatewayId||old.DeviceId!=binding.DeviceId||old.SourceId!=binding.SourceId||old.ServiceUserId!=input.ServiceUserId||old.Serial!=binding.Serial||old.KeyVersion!=binding.KeyVersion||old.TimeZoneId!=zone||!old.ExpectedDeviceVersion.SequenceEqual(version)||!CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(protector.Unprotect(old.ProtectedKey)),key))throw new DomainConflictException("Changed gateway binding retry.");
                    return(old.Id,old.Id);
                }
                // Key rotation creates a new immutable binding only after explicit revocation.
                if(await db.PushGatewayBindings.AnyAsync(x=>x.GatewayId==binding.GatewayId&&x.DeviceId==device.Id&&x.RevokedAtUtc==null,token))throw new DomainConflictException("Revoke the previous binding before key rotation.");
                db.PushGatewayBindings.Add(new(){Id=input.Id,GatewayId=binding.GatewayId,DeviceId=device.Id,SourceId=device.SourceId,ServiceUserId=input.ServiceUserId,CreatedByUserId=actor,CreatedAtUtc=clock.GetUtcNow(),ExpectedDeviceVersion=version,Serial=binding.Serial,TimeZoneId=zone,KeyVersion=binding.KeyVersion,ProtectedKey=protector.Protect(Convert.ToBase64String(key))});
                return(input.Id,input.Id);
            },ct);}finally{CryptographicOperations.ZeroMemory(key);}
        }
    }
    public Task<Guid> RevokeAsync(Guid actor,Guid id,byte[] version,CancellationToken ct=default)=>tx.ExecuteAsync(actor,"devices.credentials.manage","device-gateway.revoked",async token=>
    {
        await Locks(token);
        var row=await db.PushGatewayBindings.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new ArgumentException("Unknown binding.");
        if(version is not{Length:8}||!row.Version.SequenceEqual(version))throw new DomainConflictException("Refresh binding version.");
        if(!await tx.HasDevicePermissionAsync(actor,DevicePushJournal.Retain,row.DeviceId,token))throw new UnauthorizedAccessException();
        row.RevokedAtUtc??=clock.GetUtcNow();row.RevokedByUserId??=actor;return(id,id);
    },ct);
    public Task<object> ListAsync(Guid actor,CancellationToken ct=default)=>tx.ExecuteAsync<object>(actor,"devices.credentials.manage","device-gateway.viewed",async token=>
    {
        var rows=await db.PushGatewayBindings.AsNoTracking().OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Take(100).Select(x=>new{x.Id,x.GatewayId,x.DeviceId,x.SourceId,x.ServiceUserId,x.Serial,x.TimeZoneId,x.KeyVersion,x.CreatedAtUtc,x.RevokedAtUtc,x.Version}).ToListAsync(token);
        return(rows,Guid.Empty);
    },ct);
    public async Task<PushGatewayRetention> ReceiveAsync(Guid bindingId,PushGatewayEnvelope input,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if(input.Body is null||input.Body.Length is 0 or >ZkPushAttendanceCodec.MaximumBytes)throw new ArgumentException("Bounded evidence required.");
        input=input with{Body=(byte[])input.Body.Clone()};
        // Lookup chooses a service principal, not authority. Everything is reloaded and authorized within the transaction.
        var service=await db.PushGatewayBindings.AsNoTracking().Where(x=>x.Id==bindingId&&x.RevokedAtUtc==null).Select(x=>(Guid?)x.ServiceUserId).SingleOrDefaultAsync(ct)??throw new UnauthorizedAccessException();
        return await tx.ExecuteAsync(service,DevicePushJournal.Retain,"device-gateway.retained",async token=>
        {
            await Locks(token);
            var row=await db.PushGatewayBindings.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==bindingId&&x.ServiceUserId==service&&x.RevokedAtUtc==null,token)??throw new UnauthorizedAccessException();
            if(!await tx.HasDevicePermissionAsync(row.CreatedByUserId,"devices.credentials.manage",row.DeviceId,token)||!await tx.HasDevicePermissionAsync(row.CreatedByUserId,DevicePushJournal.Retain,row.DeviceId,token))throw new UnauthorizedAccessException();
            var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==row.DeviceId,token)??throw new UnauthorizedAccessException();
            var site=await db.Sites.AsNoTracking().SingleAsync(x=>x.Id==device.SiteId,token);
            if(!device.Enabled||device.Profile!="zk-push"||device.SourceId!=row.SourceId||site.TimeZoneId!=row.TimeZoneId||!device.Version.SequenceEqual(row.ExpectedDeviceVersion))throw new UnauthorizedAccessException();
            byte[] key;
            try{key=Convert.FromBase64String(Protector(row.Id).Unprotect(row.ProtectedKey));}catch(CryptographicException){throw new UnauthorizedAccessException();}
            try
            {
                var binding=new PushGatewayBinding(row.GatewayId,row.DeviceId,row.SourceId,row.Serial,row.KeyVersion,TimeZoneInfo.FindSystemTimeZoneById(row.TimeZoneId),key);
                var receipt=await AuthenticatedPushGateway.ReceiveAsync(binding,input,new CommitStore(async(b,transfer,batch,t)=>
                {
                    var fingerprint=AuthenticatedPushGateway.TransferFingerprint(b,transfer,batch);
                    var old=await db.PushGatewayReceipts.AsNoTracking().SingleOrDefaultAsync(x=>x.GatewayId==b.GatewayId&&x.DeviceId==b.DeviceId&&x.TransferId==transfer,t);
                    if(old is not null)
                    {
                        var retained=await db.DevicePushJournal.AsNoTracking().SingleAsync(x=>x.Id==old.Id,t);
                        if(old.BindingId!=row.Id||old.RequestFingerprint!=fingerprint||retained.Serial!=batch.Serial||retained.Stamp!=batch.Stamp||retained.TimeZoneId!=batch.TimeZoneId||!retained.OriginalBody.AsSpan().SequenceEqual(batch.Original.Span))throw new DomainConflictException("Changed transfer retry.");
                        return new(old.Id,b.GatewayId,b.DeviceId,b.SourceId,transfer,retained.PayloadHash,retained.RowCount,fingerprint);
                    }
                    var id=Guid.NewGuid();
                    db.DevicePushJournal.Add(new(){Id=id,DeviceId=device.Id,SourceId=device.SourceId,SiteId=device.SiteId,CompanyId=site.CompanyId,RetainedByUserId=service,RetainedAtUtc=clock.GetUtcNow(),ExpectedDeviceVersion=row.ExpectedDeviceVersion.ToArray(),Serial=batch.Serial,Stamp=batch.Stamp,Contract=batch.Contract,TimeZoneId=batch.TimeZoneId,OriginalBody=batch.Original.ToArray(),PayloadHash=batch.PayloadHash,RowCount=batch.Rows.Count,ParsedCount=batch.Rows.Count(x=>x.Status=="parsed-requires-mapping-review"),RowsJson=JsonSerializer.Serialize(batch.Rows)});
                    db.PushGatewayReceipts.Add(new(){Id=id,BindingId=row.Id,GatewayId=b.GatewayId,DeviceId=b.DeviceId,TransferId=transfer,RequestFingerprint=fingerprint});
                    return new(id,b.GatewayId,b.DeviceId,b.SourceId,transfer,batch.PayloadHash,batch.Rows.Count,fingerprint);
                }),clock,token);
                // tx returns to the HTTP caller only after evidence, receipt and security audit commit.
                return(receipt,receipt.ReceiptId);
            }
            finally{CryptographicOperations.ZeroMemory(key);}
        },ct);
    }
    private IDataProtector Protector(Guid id)=>protection.CreateProtector("IO.PushGateway.Key.v1",id.ToString("N"));
    private async Task Locks(CancellationToken ct){await tx.LockAsync("IO.DeviceRegistry",ct);await tx.LockAsync("IO.DevicePushJournal",ct);await tx.LockAsync("IO.PushGateway",ct);}
    private sealed class CommitStore(Func<PushGatewayBinding,Guid,ZkPushAttendanceBatch,CancellationToken,Task<PushGatewayCommit>> commit):IPushGatewayCommitStore
    { public Task<PushGatewayCommit> CommitAsync(PushGatewayBinding binding,Guid transferId,ZkPushAttendanceBatch batch,CancellationToken ct)=>commit(binding,transferId,batch,ct); }
}
