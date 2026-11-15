using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

/// <summary>Installed capture quiescence is an external physical procedure, NOT inferred from a stable count or software mutex.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AttendanceEraseApproval(BiometricDeviceIdentity Device,string ReadFormat,string AuthorizationReference,string RetentionReference,string CaptureQuiescenceReference,string ResetProcedureReference,int MaximumRecords=100000,int MaximumArchiveAgeSeconds=900,int MinimumSettlingSeconds=10,bool CaptureStoppedDuringMaintenance=false,bool ResetClearsPendingWrites=false,bool AllowErase=false)
{
 public bool IsValid()=>Device?.IsValid()==true&&ZkAttendanceReadFormats.IsKnown(ReadFormat)&&Text(AuthorizationReference)&&Text(RetentionReference)&&Text(CaptureQuiescenceReference)&&Text(ResetProcedureReference)&&MaximumRecords is >=1 and <=100000&&MaximumArchiveAgeSeconds is >=60 and <=1800&&MinimumSettlingSeconds is >=10 and <=300&&CaptureStoppedDuringMaintenance&&ResetClearsPendingWrites&&AllowErase;
 private static bool Text(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=200&&!s.Any(char.IsControl);
 public override string ToString()=>"Attendance erase approval [references redacted]";
}
public sealed record AttendanceEraseInput(ZkProbeInput Connection,Guid OperationId,AttendanceEraseApproval Approval,int ExpectedCount,string ArchiveDigest,bool EraseAllAttendance=true)
{
 public bool IsValid()=>EraseAllAttendance&&Connection?.IsValid()==true&&Connection.LocalTransport is null&&OperationId!=Guid.Empty&&Approval?.IsValid()==true&&Connection.ExpectedSerial==Approval.Device.Serial&&ExpectedCount>=0&&ExpectedCount<=Approval.MaximumRecords&&AttendanceArchiveDigest.Valid(ArchiveDigest);
 public override string ToString()=>"Attendance erase IPC [credentials and archive commitment redacted]";
}
/// <summary>Sorted SHA256 of exact canonical native tuples, retaining duplicate multiplicity; never a physical event identity.</summary>
public sealed class AttendanceArchiveDigest(int maximumRecords)
{
 private readonly List<string> hashes=[];
 public int Count=>hashes.Count;
 public static bool Valid(string? value)=>value is{Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'F');
 public void Add(ZkAttendanceRaw row,string format)
 {
  if(maximumRecords is <1 or >100000||hashes.Count>=maximumRecords||!ZkAttendanceReadFormats.Matches(row,format))throw new InvalidDataException("Bounded explicit-format archive required.");
  hashes.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.CanonicalJson()))));
 }
 public string Finish()
 {
  hashes.Sort(StringComparer.Ordinal);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
  hash.AppendData("IO-ATTENDANCE-MULTISET-V1\n"u8);
  foreach(var value in hashes)hash.AppendData(Convert.FromHexString(value));
  return Convert.ToHexString(hash.GetHashAndReset());
 }
}
public interface IAttendanceEraseSession:IZkAttendanceReadSession
{
 BiometricDeviceIdentity Identity();
 bool ClearAttendance();
}
public sealed record AttendanceEraseOutput(Guid OperationId,string State)
{
 public static bool Known(string state)=>state is "deleted-verified" or "already-empty" or "archive-mismatch" or "failed-before-write" or "unknown" or "unsupported";
 public bool IsValidFor(AttendanceEraseInput input)=>OperationId==input.OperationId&&Known(State);
}
public static class AttendanceEraseExecutor
{
 public static AttendanceEraseOutput Execute(AttendanceEraseInput input,IAttendanceEraseSession session)
 {
  AttendanceEraseOutput Result(string state)=>new(input.OperationId,state);bool dispatched=false;
  if(!input.IsValid())return Result("unsupported");
  try
  {
   if(session.Identity()!=input.Approval.Device)return Result("failed-before-write");
   // Stable counters and a multiset match do NOT stop arrivals; approved physical capture quiescence must continue through post-readback.
   (int Count,string Digest)? Scan()
   {
    var before=session.RecordCount();if(before is null or <0||before>input.Approval.MaximumRecords||!session.Begin())return null;
    var digest=new AttendanceArchiveDigest(input.Approval.MaximumRecords);
    while(session.Next(input.Approval.ReadFormat)is{} row)digest.Add(row,input.Approval.ReadFormat);
    return session.RecordCount()==before&&digest.Count==before&&session.Identity()==input.Approval.Device?(digest.Count,digest.Finish()):null;
   }
   var observed=Scan();
   if(observed is null)return Result("failed-before-write");
   if(observed.Value.Count!=input.ExpectedCount||observed.Value.Digest!=input.ArchiveDigest)return Result("archive-mismatch");
   if(observed.Value.Count==0)return Result("already-empty");
   // A second full observation rejects same-count replacements and late arrivals before dispatch, not a firmware CAS.
   var fresh=Scan();if(fresh!=observed)return Result("archive-mismatch");
   dispatched=true;if(!session.ClearAttendance())return Result("unknown");
   var after=Scan();return Result(after?.Count==0?"deleted-verified":"unknown");
  }
  catch{return Result(dispatched?"unknown":"failed-before-write");}
 }
}
