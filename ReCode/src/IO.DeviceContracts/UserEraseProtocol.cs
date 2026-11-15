using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

/// <summary>Exact installed fingerprint-only attendance profile; not a general user, face, photo, door or administrator erase capability.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UserEraseApproval(BiometricDeviceIdentity Device,string UserFormat,string Mode,string StorageProfileReference,string NativeEffectReference,string AuthorizationReference,string IrrecoverableDeletionReference,string ExclusiveControlReference,string ResetProcedureReference,int FingerprintAbsentError,int MaximumUsers=100,int MaximumTemplateCharacters=65536,int MaximumNativeLength=65536,int MaximumTotalTemplateCharacters=1048576,bool RequiresReadAllTemplate=false,bool FingerprintOnlyAttendanceProfile=false,bool AllowTransientProtectedCatalogue=false,bool AllowDelete=false)
{
 public const string User="ordinary-user",Fingerprints="all-fingerprints";
 public bool IsValid()=>Device?.IsValid()==true&&ZkUserReadFormats.IsKnown(UserFormat)&&Mode is User or Fingerprints&&Text(StorageProfileReference)&&Text(NativeEffectReference)&&Text(AuthorizationReference)&&Text(IrrecoverableDeletionReference)&&Text(ExclusiveControlReference)&&Text(ResetProcedureReference)&&FingerprintAbsentError is not 0 and not int.MinValue&&MaximumUsers is >=1 and <=1000&&MaximumTemplateCharacters is >=1 and <=65536&&MaximumNativeLength is >=1 and <=65536&&MaximumTotalTemplateCharacters is >=1 and <=8388608&&FingerprintOnlyAttendanceProfile&&AllowTransientProtectedCatalogue&&AllowDelete;
 private static bool Text(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=200&&!s.Any(char.IsControl);
 public override string ToString()=>"User erase installed approval [references redacted]";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UserEraseInput(ZkProbeInput Connection,Guid OperationId,ZkUser Expected,UserEraseApproval Approval,bool EraseUserOrFingerprints=true)
{
 public bool IsValid()=>EraseUserOrFingerprints&&Connection?.IsValid()==true&&OperationId!=Guid.Empty&&Approval?.IsValid()==true&&Connection.ExpectedSerial==Approval.Device.Serial&&Expected?.IsValid()==true&&Expected.Privilege==0&&Expected.Card is not null&&Expected.PersonIdentifier.All(char.IsAsciiDigit)&&(Approval.UserFormat!=ZkUserReadFormats.Legacy||ZkUserReadFormats.IsCanonicalLegacyIdentity(Expected.PersonIdentifier));
 public override string ToString()=>"User erase private IPC [subject, expected metadata and credential redacted]";
}
// Raw protected catalogue is deliberately non-serializable, unhashed and never a receipt or a backup.
// Managed string reclamation is NOT secure memory erasure; the isolated child must exit after one request.
public sealed class UserEraseFingerprint(string data,int nativeLength,int flag)
{
 [JsonIgnore]public string Data {get;}=data;
 [JsonIgnore]public int NativeLength {get;}=nativeLength;
 [JsonIgnore]public int Flag {get;}=flag;
 public bool IsValid(UserEraseApproval a)=>Data is{Length:>0}&&Data.Length<=a.MaximumTemplateCharacters&&Data.All(c=>c is >=(char)33 and <=(char)126)&&NativeLength is >0&&NativeLength<=a.MaximumNativeLength&&(a.UserFormat==ZkUserReadFormats.Ssr?Flag is 0 or 1:Flag==0);
 public bool Same(UserEraseFingerprint? other)=>other is not null&&Data==other.Data&&NativeLength==other.NativeLength&&Flag==other.Flag;
 public override string ToString()=>"Fingerprint [transient protected data redacted]";
}
public sealed class UserEraseEntry(ZkUser user,string password,IReadOnlyList<UserEraseFingerprint?> fingerprints)
{
 [JsonIgnore]public ZkUser User {get;}=user;
 [JsonIgnore]public string Password {get;}=password;
 [JsonIgnore]public IReadOnlyList<UserEraseFingerprint?> Fingerprints {get;}=fingerprints;
 public bool IsValid(UserEraseApproval a)=>User?.IsValid()==true&&User.Card is not null&&User.PersonIdentifier.All(char.IsAsciiDigit)&&Password is not null&&Password.Length<=128&&(a.UserFormat!=ZkUserReadFormats.Legacy||ZkUserReadFormats.IsCanonicalLegacyIdentity(User.PersonIdentifier))&&Fingerprints is{Count:10}&&Fingerprints.All(f=>f is null||f.IsValid(a));
 public bool Same(UserEraseEntry other,bool ignoreFingerprints=false)=>User==other.User&&Password==other.Password&&(ignoreFingerprints||Fingerprints.Zip(other.Fingerprints).All(p=>p.First is null?p.Second is null:p.First.Same(p.Second)));
 public override string ToString()=>"User catalogue entry [protected data redacted]";
}
public sealed class UserEraseCatalogue(IReadOnlyList<UserEraseEntry> entries)
{
 [JsonIgnore]public IReadOnlyList<UserEraseEntry> Entries {get;}=entries;
 public bool IsValid(UserEraseApproval a)=>Entries is not null&&Entries.Count<=a.MaximumUsers&&Entries.All(e=>e?.IsValid(a)==true)&&Entries.Select(e=>e.User.PersonIdentifier).Distinct(StringComparer.Ordinal).Count()==Entries.Count&&Entries.Sum(e=>e.Fingerprints.Sum(f=>(long)(f?.Data.Length??0)))<=a.MaximumTotalTemplateCharacters;
 public bool Same(UserEraseCatalogue other)=>Entries.Count==other.Entries.Count&&Entries.All(e=>other.Entries.SingleOrDefault(o=>o.User.PersonIdentifier==e.User.PersonIdentifier)is{} found&&e.Same(found));
 public override string ToString()=>"User catalogue [no raw data outside isolated child]";
}
public interface IUserEraseSession
{
 BiometricDeviceIdentity Identity();
 // Must return the COMPLETE bounded user/password/card/ten-slot catalogue; every absent slot must have installed exact absence evidence.
 UserEraseCatalogue ReadCatalogue(UserEraseApproval approval);
 IReadOnlyList<UserEraseFingerprint?> ReadFingerprints(string person,UserEraseApproval approval);
 // Implementations map only the two closed modes and two formats; no caller-supplied native flag or fallback.
 bool Delete(string person,UserEraseApproval approval);
}
public sealed record UserEraseOutput(Guid OperationId,string State)
{
 public static bool Known(string? s)=>s is "user-deleted-verified" or "fingerprints-deleted-verified" or "already-absent" or "already-empty" or "catalogue-mismatch" or "failed-before-write" or "unknown" or "unsupported";
 public bool IsValidFor(UserEraseInput input)=>OperationId==input.OperationId&&Known(State)&&(State!="user-deleted-verified"||input.Approval.Mode==UserEraseApproval.User)&&(State!="fingerprints-deleted-verified"||input.Approval.Mode==UserEraseApproval.Fingerprints)&&(State!="already-empty"||input.Approval.Mode==UserEraseApproval.Fingerprints);
}
/// <summary>One command only; both preflight scans and exact protected outside-catalogue readback live exclusively in native child memory. No automatic restore/retry.</summary>
public static class UserEraseExecutor
{
 public static UserEraseOutput Execute(UserEraseInput input,IUserEraseSession session)
 {
  UserEraseOutput Result(string s)=>new(input.OperationId,s);bool dispatched=false;
  if(!input.IsValid())return Result("unsupported");
  try
  {
   var a=input.Approval;
   UserEraseCatalogue Scan()
   {
    if(session.Identity()!=a.Device)throw new InvalidDataException();
    var c=session.ReadCatalogue(a);
    if(c?.IsValid(a)!=true||session.Identity()!=a.Device)throw new InvalidDataException();return c;
   }
   var before=Scan();var target=before.Entries.SingleOrDefault(e=>e.User.PersonIdentifier==input.Expected.PersonIdentifier);
   if(target is null)return Result("already-absent"); // Complete enumeration only, never a failed per-user getter.
   if(target.User!=input.Expected||target.User.Privilege!=0)return Result("catalogue-mismatch");
   if(a.Mode==UserEraseApproval.Fingerprints&&target.Fingerprints.All(f=>f is null))return Result("already-empty");
   if(!before.Same(Scan()))return Result("catalogue-mismatch");
   // Two stable snapshots are NOT a firmware CAS. Exclusive installed control must start before metadata capture and remain through readback.
   dispatched=true;if(!session.Delete(input.Expected.PersonIdentifier,a))return Result("unknown");
   var after=Scan();var remaining=after.Entries.SingleOrDefault(e=>e.User.PersonIdentifier==input.Expected.PersonIdentifier);
   var expectedCount=before.Entries.Count-(a.Mode==UserEraseApproval.User?1:0);
   if(after.Entries.Count!=expectedCount)return Result("unknown");
   if(a.Mode==UserEraseApproval.User?remaining is not null:remaining is null||!target.Same(remaining,true)||remaining.Fingerprints.Any(f=>f is not null))return Result("unknown");
   if(a.Mode==UserEraseApproval.User){var slots=session.ReadFingerprints(input.Expected.PersonIdentifier,a);if(slots is not{Count:10}||slots.Any(f=>f is not null)||session.Identity()!=a.Device)return Result("unknown");}
   // Includes privileged users' passwords/cards and all other users' fingerprint slots. No protected value leaves the process.
   if(before.Entries.Where(e=>e.User.PersonIdentifier!=input.Expected.PersonIdentifier).Any(e=>after.Entries.SingleOrDefault(o=>o.User.PersonIdentifier==e.User.PersonIdentifier)is not{} other||!e.Same(other)))return Result("unknown");
   return Result(a.Mode==UserEraseApproval.User?"user-deleted-verified":"fingerprints-deleted-verified");
  }
  catch{return Result(dispatched?"unknown":"failed-before-write");}
 }
}
