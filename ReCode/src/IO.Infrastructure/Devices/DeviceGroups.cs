using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Devices;

/// <summary>Reference Gate grouping metadata; never a physical access-control service.</summary>
public sealed class DeviceGroups(IoDbContext db, PermissionTransaction transaction)
{
    public const string Read="devices.groups.read", Manage="devices.groups.manage", Assign="devices.groups.assign";
    public sealed record Input(Guid CompanyId, Guid? ParentId, string Name);
    public Task<Guid> CreateAsync(Guid actor, Guid operationId, Input input, CancellationToken ct=default) =>
        transaction.ExecuteAsync(actor,Manage,"device-groups.created",async token=>
        {
            var value=Validate(input);
            if(operationId==Guid.Empty)throw new ArgumentException("Operation id required.");
            var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
            await transaction.LockAsync("IO.DeviceRegistry",token);
            var existing=await db.DeviceGroups.AsNoTracking().SingleOrDefaultAsync(g=>g.Id==operationId,token);
            if(existing is not null)
            {
                if(existing.DeletedAtUtc is not null||existing.CreatedByUserId!=actor||existing.CreationFingerprint!=fingerprint)
                    throw new DomainConflictException("Changed or deleted group create replay.");
                return(existing.Id,existing.Id);
            }
            if(!await db.Companies.AnyAsync(c=>c.Id==value.CompanyId,token))throw new ArgumentException("Unknown company.");
            await CheckParent(value.CompanyId,operationId,value.ParentId,token);
            await CheckName(value,operationId,token);
            var now=DateTimeOffset.UtcNow;
            db.DeviceGroups.Add(new(){Id=operationId,CompanyId=value.CompanyId,ParentId=value.ParentId,Name=value.Name,
                CreationFingerprint=fingerprint,CreatedByUserId=actor,CreatedAtUtc=now,UpdatedAtUtc=now});
            return(operationId,operationId);
        },ct);
    public Task<Guid> UpdateAsync(Guid actor,Guid id,byte[] expectedVersion,Input input,CancellationToken ct=default) =>
        transaction.ExecuteAsync(actor,Manage,"device-groups.updated",async token=>
        {
            var value=Validate(input);
            await transaction.LockAsync("IO.DeviceRegistry",token);
            var group=await Load(id,expectedVersion,token);
            if(group.DeletedAtUtc is not null)throw new DomainConflictException("Group deleted.");
            if(value.CompanyId!=group.CompanyId)throw new DomainConflictException("Company is immutable; create a new group.");
            await CheckParent(value.CompanyId,id,value.ParentId,token);await CheckName(value,id,token);
            group.Name=value.Name;group.ParentId=value.ParentId;group.UpdatedAtUtc=DateTimeOffset.UtcNow;
            return(id,id);
        },ct);
    public Task<Guid> DeleteAsync(Guid actor,Guid id,byte[] expectedVersion,CancellationToken ct=default) =>
        transaction.ExecuteAsync(actor,Manage,"device-groups.deleted",async token=>
        {
            Version(expectedVersion);
            await transaction.LockAsync("IO.DeviceRegistry",token);
            var group=await db.DeviceGroups.AsTracking().SingleOrDefaultAsync(g=>g.Id==id,token)??throw new ArgumentException("Unknown group.");
            if(group.DeletedAtUtc is not null)
            {
                if(group.DeletedByUserId==actor&&group.DeletedFromVersion is not null&&group.DeletedFromVersion.SequenceEqual(expectedVersion))return(id,id);
                throw new DomainConflictException("Changed delete replay.");
            }
            if(!group.Version.SequenceEqual(expectedVersion))throw new DomainConflictException("Stale version.");
            if(await db.DeviceGroups.AnyAsync(g=>g.ParentId==id&&g.DeletedAtUtc==null,token)||await db.Devices.AnyAsync(d=>d.GroupId==id,token))
                throw new DomainConflictException("Only empty leaves can be deleted; detach devices first.");
            db.Entry(group).Property(g=>g.Version).OriginalValue=expectedVersion;
            group.DeletedFromVersion=expectedVersion.ToArray();group.DeletedByUserId=actor;
            group.DeletedAtUtc=DateTimeOffset.UtcNow;group.UpdatedAtUtc=group.DeletedAtUtc.Value;
            return(id,id);
        },ct);
    public Task<Guid> AssignAsync(Guid actor,Guid deviceId,byte[] expectedVersion,Guid? groupId,CancellationToken ct=default) =>
        transaction.ExecuteAsync(actor,Assign,"devices.group.changed",async token=>
        {
            Version(expectedVersion);
            if(groupId==Guid.Empty)throw new ArgumentException("Empty group id.");
            await transaction.LockAsync("IO.DeviceRegistry",token);
            await transaction.LockAsync("IO.DeviceConnections",token);
            if(await db.DeviceOperations.AnyAsync(x=>x.DeviceId==deviceId&&(x.Kind=="clock.set"||x.Kind=="device.restart"||x.Kind=="network.ip.set"||x.Kind=="settings.idle.set")&&(x.State=="pending"||x.State=="running"||(x.State=="unknown"&&x.ResolvedAtUtc==null)),token))throw new DomainConflictException("Resolve or cancel clock work before changing device version.");
            var device=await db.Devices.AsTracking().SingleOrDefaultAsync(d=>d.Id==deviceId,token)??throw new ArgumentException("Unknown device.");
            if(!device.Version.SequenceEqual(expectedVersion))throw new DomainConflictException("Stale device version.");
            if(groupId is not null)
            {
                var company=await db.Sites.Where(s=>s.Id==device.SiteId).Select(s=>s.CompanyId).SingleAsync(token);
                if(!await db.DeviceGroups.AnyAsync(g=>g.Id==groupId&&g.CompanyId==company&&g.DeletedAtUtc==null,token))
                    throw new ArgumentException("Group must be active in the installation company.");
            }
            db.Entry(device).Property(d=>d.Version).OriginalValue=expectedVersion;
            device.GroupId=groupId;device.UpdatedAtUtc=DateTimeOffset.UtcNow;
            return(deviceId,deviceId);
        },ct);
    public Task<object> ListAsync(Guid actor,Guid? companyId,Guid? parentId,int offset,CancellationToken ct=default) =>
        transaction.ExecuteAsync<object>(actor,Read,"device-groups.viewed",async token=>
        {
            if(companyId==Guid.Empty||parentId==Guid.Empty||offset is <0 or >100000)throw new ArgumentException("Invalid filter.");
            var parent=await db.DeviceGroups.AsNoTracking().Where(g=>g.Id==parentId&&g.DeletedAtUtc==null)
                .Select(g=>new{g.Id,g.ParentId,g.CompanyId,g.Name}).SingleOrDefaultAsync(token);
            if(parentId is not null&&(parent is null||(companyId is not null&&parent.CompanyId!=companyId)))throw new ArgumentException("Invalid parent.");
            var items=await db.DeviceGroups.AsNoTracking().Where(g=>g.DeletedAtUtc==null&&g.ParentId==parentId&&(companyId==null||g.CompanyId==companyId))
                .OrderBy(g=>g.Name).ThenBy(g=>g.Id).Skip(offset).Take(50).Select(g=>new{g.Id,g.CompanyId,g.ParentId,g.Name,g.Version,
                    hasChildren=db.DeviceGroups.Any(c=>c.ParentId==g.Id&&c.DeletedAtUtc==null),deviceCount=db.Devices.Count(d=>d.GroupId==g.Id)}).ToListAsync(token);
            return(new{offset,pageSize=50,parent,items},actor);
        },ct);
    private async Task<DeviceGroupRecord> Load(Guid id,byte[] version,CancellationToken ct)
    {
        Version(version);
        var group=await db.DeviceGroups.AsTracking().SingleOrDefaultAsync(g=>g.Id==id,ct)??throw new ArgumentException("Unknown group.");
        if(!group.Version.SequenceEqual(version))throw new DomainConflictException("Stale group version.");
        db.Entry(group).Property(g=>g.Version).OriginalValue=version;return group;
    }
    private async Task CheckParent(Guid company,Guid id,Guid? parent,CancellationToken ct)
    {
        var seen=new HashSet<Guid>{id};
        while(parent is not null)
        {
            if(!seen.Add(parent.Value))throw new DomainConflictException("Group cycle.");
            var next=await db.DeviceGroups.AsNoTracking().SingleOrDefaultAsync(g=>g.Id==parent,ct);
            if(next is null||next.CompanyId!=company||next.DeletedAtUtc is not null)throw new ArgumentException("Invalid group parent.");
            parent=next.ParentId;
        }
    }
    private async Task CheckName(Input value,Guid id,CancellationToken ct)
    {if(await db.DeviceGroups.AnyAsync(g=>g.Id!=id&&g.CompanyId==value.CompanyId&&g.ParentId==value.ParentId&&g.DeletedAtUtc==null&&g.Name==value.Name,ct))throw new DomainConflictException("Sibling name already exists.");}
    private static void Version(byte[] version)
    {if(version is null||version.Length!=8)throw new ArgumentException("Rowversion required.");}
    private static Input Validate(Input input)
    {
        if(input is null||input.CompanyId==Guid.Empty||input.ParentId==Guid.Empty||string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>150||input.Name.Any(char.IsControl))throw new ArgumentException("Invalid group.");
        return input with{Name=input.Name.Trim()};
    }
}
