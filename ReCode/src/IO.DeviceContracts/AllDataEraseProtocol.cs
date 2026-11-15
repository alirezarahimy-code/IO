using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

/// <summary>Closed ClearData(5) profile. Never factory reset; no vendor/hidden stores inferred from the method name.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AllDataEraseApproval(string Family,UserEraseApproval Inventory,AttendanceEraseApproval Attendance,string EffectReference,bool AllowClearAllData=false,bool PreserveEndpointAndCredential=false,bool NoOtherStores=false)
{
 public bool IsValid()=>Family=="all-data"&&Inventory?.IsValid()==true&&Inventory.Mode==UserEraseApproval.User&&Attendance?.IsValid()==true&&Attendance.Device==Inventory.Device&&AllowClearAllData&&PreserveEndpointAndCredential&&NoOtherStores&&!string.IsNullOrWhiteSpace(EffectReference)&&EffectReference.Length<=200&&!EffectReference.Any(char.IsControl);
 public override string ToString()=>"All-data clear approval [references redacted; not factory reset]";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AllDataEraseInput(ZkProbeInput Connection,Guid OperationId,AllDataEraseApproval Approval,int ArchiveCount,string MetadataDigest,int AttendanceCount,string AttendanceDigest,bool InspectOnly=false,bool EraseAllData=true)
{
 public bool IsValid()=>EraseAllData&&Connection?.IsValid()==true&&OperationId!=Guid.Empty&&Approval?.IsValid()==true&&Connection.ExpectedSerial==Approval.Inventory.Device.Serial&&ArchiveCount>=0&&ArchiveCount<=Approval.Inventory.MaximumUsers&&AttendanceCount>=0&&AttendanceCount<=Approval.Attendance.MaximumRecords&&AttendanceArchiveDigest.Valid(MetadataDigest)&&AttendanceArchiveDigest.Valid(AttendanceDigest);
 public override string ToString()=>"All-data clear IPC [private metadata redacted]";
}
public interface IAllDataEraseSession
{
 BiometricDeviceIdentity Identity();
 UserEraseCatalogue Users(UserEraseApproval approval);
 IReadOnlyList<UserEraseFingerprint?> Fingerprints(string person,UserEraseApproval approval);
 int? RecordCount();bool BeginAttendance();ZkAttendanceRaw? NextAttendance(string format);
 bool ClearAllData();
 // A single read-only reconnect with the ORIGINAL endpoint/key; never another ClearData or a fallback credential.
 bool ReconnectOriginal();
}
public sealed record AllDataEraseOutput(Guid OperationId,string State)
{
 public bool IsValidFor(AllDataEraseInput i)=>OperationId==i.OperationId&&(i.InspectOnly?State is "observed" or "inspection-failed":State is "all-data-cleared-verified" or "already-no-profile-data" or "archive-mismatch" or "failed-before-write" or "unknown" or "unsupported");
}
public static class AllDataEraseExecutor
{
 public static AllDataEraseOutput Execute(AllDataEraseInput i,IAllDataEraseSession session)
 {
  AllDataEraseOutput Result(string state)=>new(i.OperationId,state);bool dispatched=false;
  if(!i.IsValid())return Result(i.InspectOnly?"inspection-failed":"unsupported");
  try
  {
   var a=i.Approval;
   (UserEraseCatalogue Users,int Count,string Digest) Scan(bool archived)
   {
    if(session.Identity()!=a.Inventory.Device)throw new InvalidDataException();
    var users=session.Users(a.Inventory);if(users?.IsValid(a.Inventory)!=true)throw new InvalidDataException();
    var count=session.RecordCount();if(count is null or <0||count>a.Attendance.MaximumRecords||!session.BeginAttendance())throw new InvalidDataException();
    var digest=new AttendanceArchiveDigest(a.Attendance.MaximumRecords);
    while(session.NextAttendance(a.Attendance.ReadFormat)is{} row)digest.Add(row,a.Attendance.ReadFormat);
    var hash=digest.Finish();
    if(count!=digest.Count||session.RecordCount()!=count||session.Identity()!=a.Inventory.Device)throw new InvalidDataException();
    if(archived&&(users.Entries.Count!=i.ArchiveCount||UserMetadataCommitment.Create(users.Entries.Select(e=>e.User))!=i.MetadataDigest||digest.Count!=i.AttendanceCount||hash!=i.AttendanceDigest))throw new InvalidDataException();
    return(users,digest.Count,hash);
   }
   var before=Scan(true);
   if(i.InspectOnly)return Result("observed"); // Inspection has NO mutation or reconnect command path.
   if(before.Users.Entries.Count==0&&before.Count==0)return Result("already-no-profile-data");
   var fresh=Scan(true);
   if(!before.Users.Same(fresh.Users)||before.Count!=fresh.Count||before.Digest!=fresh.Digest)return Result("archive-mismatch");
   // Physical all-channel capture/user/biometric quiescence is an external prerequisite, NOT a firmware CAS.
   dispatched=true;if(!session.ClearAllData())return Result("unknown");
   if(!session.ReconnectOriginal())return Result("unknown");
   var after=Scan(false);if(after.Users.Entries.Count!=0||after.Count!=0)return Result("unknown");
   // An empty user enumeration alone cannot hide accessible orphan templates for the original catalogue.
   foreach(var e in before.Users.Entries)
   {
    var slots=session.Fingerprints(e.User.PersonIdentifier,a.Inventory);
    if(slots is not{Count:10}||slots.Any(f=>f is not null)||session.Identity()!=a.Inventory.Device)return Result("unknown");
   }
   return Result("all-data-cleared-verified"); // Only declared stores; not reset, secure erase, or proof about hidden media.
  }
  catch{return Result(i.InspectOnly?"inspection-failed":dispatched?"unknown":"failed-before-write");}
 }
}
