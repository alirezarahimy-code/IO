using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Security;

public sealed class DeviceOperatorPolicy
{
    public Guid UserId { get; set; }
    public bool Restricted { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
    public byte[] Version { get; set; } = null!;
}
public sealed class DeviceOperatorGrant
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public DateTimeOffset GrantedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public byte[] Version { get; set; } = null!;
}

/// <summary>Device selection narrows existing operation rights; it never grants them.
/// Existing global operators remain global until an explicit versioned policy change.</summary>
public sealed class DeviceAccess(LoginIdentityDbContext identity,IoDbContext db,PermissionTransaction transaction,TimeProvider clock)
{
    public const string Manage="devices.access.manage";
    public Task<object> ReadAsync(Guid actor,Guid userId,int offset,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Manage,"device-access.viewed",async token=>
    {
        if(userId==Guid.Empty||offset is <0 or >100000)throw new ArgumentException("Invalid operator page.");
        if(!await db.Users.AnyAsync(x=>x.Id==userId,token))throw new ArgumentException("Unknown operator.");
        var policy=await identity.DeviceOperatorPolicies.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==userId,token);
        var items=await identity.DeviceOperatorGrants.AsNoTracking().Where(x=>x.UserId==userId).OrderByDescending(x=>x.GrantedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50).ToListAsync(token);
        return(new{userId,restricted=policy?.Restricted??false,version=policy?.Version,offset,pageSize=50,items},userId);
    },ct);
    public Task<Guid> SetPolicyAsync(Guid actor,Guid userId,byte[]? version,bool restricted,string confirmation,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,Manage,"device-access.policy-changed",async token=>
    {
        if(userId==Guid.Empty||userId==actor||confirmation!=(restricted?"RESTRICT-DEVICE-OPERATOR":"ALLOW-ALL-PERMITTED-DEVICES"))throw new ArgumentException("Explicit other-operator scope approval required.");
        await transaction.LockAsync("IO.DeviceAccess",token);
        if(!await db.Users.AnyAsync(x=>x.Id==userId&&x.IsActive,token))throw new ArgumentException("Unknown active operator.");
        var old=await identity.DeviceOperatorPolicies.SingleOrDefaultAsync(x=>x.UserId==userId,token);
        if(old is null){if(version is not null)throw new DomainConflictException("Refresh operator policy.");identity.DeviceOperatorPolicies.Add(new(){UserId=userId,Restricted=restricted,ChangedAtUtc=clock.GetUtcNow()});}
        else{if(version is not{Length:8}||!old.Version.SequenceEqual(version))throw new DomainConflictException("Stale operator policy.");old.Restricted=restricted;old.ChangedAtUtc=clock.GetUtcNow();}
        return(userId,userId);
    },ct);
    public Task<Guid> GrantAsync(Guid actor,Guid id,Guid userId,Guid deviceId,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,Manage,"device-access.granted",async token=>
    {
        if(id==Guid.Empty||userId==Guid.Empty||deviceId==Guid.Empty||actor==userId)throw new ArgumentException("Explicit other-operator target required.");
        await transaction.LockAsync("IO.DeviceAccess",token);
        if(!await db.Users.AnyAsync(x=>x.Id==userId&&x.IsActive,token)||!await db.Devices.AnyAsync(x=>x.Id==deviceId,token))throw new ArgumentException("Unknown operator or device.");
        var old=await identity.DeviceOperatorGrants.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token);
        if(old is not null){if(old.UserId!=userId||old.DeviceId!=deviceId||old.RevokedAtUtc!=null)throw new DomainConflictException("Changed or revoked grant replay.");return(id,id);}
        if(await identity.DeviceOperatorGrants.AnyAsync(x=>x.UserId==userId&&x.DeviceId==deviceId&&x.RevokedAtUtc==null,token))throw new DomainConflictException("Device already assigned.");
        identity.DeviceOperatorGrants.Add(new(){Id=id,UserId=userId,DeviceId=deviceId,GrantedAtUtc=clock.GetUtcNow()});return(id,id);
    },ct);
    public Task<Guid> RevokeAsync(Guid actor,Guid id,byte[] version,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,Manage,"device-access.revoked",async token=>
    {
        if(version is not{Length:8})throw new ArgumentException("Grant version required.");await transaction.LockAsync("IO.DeviceAccess",token);
        var grant=await identity.DeviceOperatorGrants.SingleOrDefaultAsync(x=>x.Id==id,token)??throw new ArgumentException("Unknown device grant.");
        if(grant.UserId==actor)throw new ArgumentException("Cannot administer own device scope.");
        if(!grant.Version.SequenceEqual(version)||grant.RevokedAtUtc!=null)throw new DomainConflictException("Stale or revoked device grant.");
        grant.RevokedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);
}
