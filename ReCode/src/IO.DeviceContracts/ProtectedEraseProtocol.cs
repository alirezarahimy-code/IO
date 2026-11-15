using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProtectedEraseApproval(string Family,UserEraseApproval Inventory,string? AdministratorEffectReference=null,bool AllowClearAdministrators=false)
{
 public const string Users="users",Fingers="fingerprints",Admins="administrators";
 public bool IsValid()=>Inventory?.IsValid()==true&&Family is Users or Fingers or Admins&&Inventory.Mode==(Family==Fingers?UserEraseApproval.Fingerprints:UserEraseApproval.User)&&(Family==Admins?AllowClearAdministrators&&!string.IsNullOrWhiteSpace(AdministratorEffectReference)&&AdministratorEffectReference.Length<=200&&!AdministratorEffectReference.Any(char.IsControl):!AllowClearAdministrators&&AdministratorEffectReference is null);
 public override string ToString()=>"Protected erase approval [references redacted]";
}
/// <summary>Metadata ONLY: never accepts a password, template or biometric commitment.</summary>
public static class UserMetadataCommitment
{
 public static string Create(IEnumerable<ZkUser> users)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(users.OrderBy(u=>u.PersonIdentifier,StringComparer.Ordinal).ToArray())));
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProtectedEraseInput(ZkProbeInput Connection,Guid OperationId,ProtectedEraseApproval Approval,ZkUser? Expected,int ArchiveCount,string MetadataDigest,bool InspectOnly=false,bool EraseProtectedCatalogue=true)
{
 public bool IsValid()=>EraseProtectedCatalogue&&Connection?.IsValid()==true&&Connection.LocalTransport is null&&OperationId!=Guid.Empty&&Approval?.IsValid()==true&&Connection.ExpectedSerial==Approval.Inventory.Device.Serial&&ArchiveCount>=0&&ArchiveCount<=Approval.Inventory.MaximumUsers&&AttendanceArchiveDigest.Valid(MetadataDigest)&&(Approval.Family==ProtectedEraseApproval.Admins?Expected is null:InspectOnly&&Expected is null||Expected is not null&&new UserEraseInput(Connection,OperationId,Expected,Approval.Inventory).IsValid());
 public override string ToString()=>"Protected erase private IPC [metadata and credentials redacted]";
}
public interface IProtectedEraseSession:IUserEraseSession {bool ClearAdministrators();}
public sealed record ProtectedEraseOutput(Guid OperationId,string State)
{
 public bool IsValidFor(ProtectedEraseInput i)=>OperationId==i.OperationId&&(i.InspectOnly?State is "observed" or "inspection-failed":State is "failed-before-write" or "catalogue-mismatch" or "unknown" or "unsupported"||i.Approval.Family switch{ProtectedEraseApproval.Admins=>State is "administrators-cleared-verified" or "already-no-administrators",ProtectedEraseApproval.Users=>State is "user-deleted-verified" or "already-absent",_=>State is "fingerprints-deleted-verified" or "already-absent" or "already-empty"});
}
public static class ProtectedEraseExecutor
{
 public static ProtectedEraseOutput Execute(ProtectedEraseInput i,IProtectedEraseSession session)
 {
  ProtectedEraseOutput Result(string s)=>new(i.OperationId,s);bool dispatched=false;
  if(!i.IsValid())return Result(i.InspectOnly?"inspection-failed":"unsupported");
  try
  {
   var a=i.Approval.Inventory;
   UserEraseCatalogue Scan(bool matchArchive)
   {
    if(session.Identity()!=a.Device)throw new InvalidDataException();var c=session.ReadCatalogue(a);
    if(c?.IsValid(a)!=true||session.Identity()!=a.Device||matchArchive&&(c.Entries.Count!=i.ArchiveCount||UserMetadataCommitment.Create(c.Entries.Select(e=>e.User))!=i.MetadataDigest))throw new InvalidDataException();return c;
   }
   if(i.InspectOnly){_=Scan(true);return Result("observed");} // No deletion method is reachable on this branch.
   if(i.Approval.Family!=ProtectedEraseApproval.Admins)
   {
    var result=UserEraseExecutor.Execute(new(i.Connection,i.OperationId,i.Expected!,a),new BoundSession(session,()=>Scan(true)));
    return Result(result.State);
   }
   var before=Scan(true);if(before.Entries.All(e=>e.User.Privilege==0))return Result("already-no-administrators");
   if(!before.Same(Scan(true)))return Result("catalogue-mismatch");
   dispatched=true;if(!session.ClearAdministrators())return Result("unknown");
   var after=Scan(false);if(after.Entries.Count!=before.Entries.Count)return Result("unknown");
   foreach(var e in before.Entries)
   {
    var expected=new UserEraseEntry(e.User with{Privilege=0},e.Password,e.Fingerprints);
    if(after.Entries.SingleOrDefault(x=>x.User.PersonIdentifier==e.User.PersonIdentifier)is not{} found||!expected.Same(found))return Result("unknown");
   }
   return Result("administrators-cleared-verified"); // Demotion only; not deleting people, passwords, cards or templates.
  }
  catch{return Result(i.InspectOnly?"inspection-failed":dispatched?"unknown":"failed-before-write");}
 }
 private sealed class BoundSession(IProtectedEraseSession inner,Func<UserEraseCatalogue> bound):IUserEraseSession
 {
  private int scans;
  public BiometricDeviceIdentity Identity()=>inner.Identity();
  public UserEraseCatalogue ReadCatalogue(UserEraseApproval a)=>++scans<=2?bound():inner.ReadCatalogue(a);
  public IReadOnlyList<UserEraseFingerprint?> ReadFingerprints(string p,UserEraseApproval a)=>inner.ReadFingerprints(p,a);
  public bool Delete(string p,UserEraseApproval a)=>inner.Delete(p,a);
 }
}
