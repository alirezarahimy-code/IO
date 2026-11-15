using System.Globalization;
using System.Text.Json.Serialization;
namespace IO.DeviceContracts;
/// <summary>Closed wall-clock seconds on the device, NOT UTC instants. Both occurrences of a repeated local clock are deliberately selected.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AttendanceRangeEraseApproval(AttendanceEraseApproval Erase,string TimeZoneId,string BoundaryReference,string TimestampPrecisionReference,string NativeBoundaryConvention="closed-wall-second-v1",bool IncludeAllRepeatedWallClockOccurrences=false,bool AllowRangeErase=false)
{
 public bool IsValid()
 {
  if(Erase?.IsValid()!=true||!Text(TimeZoneId,100)||!Text(BoundaryReference,200)||!Text(TimestampPrecisionReference,200)||NativeBoundaryConvention!="closed-wall-second-v1"||!IncludeAllRepeatedWallClockOccurrences||!AllowRangeErase)return false;
  try{_=TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);return true;}catch(TimeZoneNotFoundException){return false;}catch(InvalidTimeZoneException){return false;}
 }
 private static bool Text(string? s,int n)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=n&&!s.Any(char.IsControl);
 public override string ToString()=>"Installed local-wall range approval [references redacted]";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AttendanceRangeErasePlan(AttendanceRangeEraseApproval Approval,string FromLocalInclusive,string ThroughLocalInclusive)
{
 public bool Bounds(out DateTime from,out DateTime through)
 {
  from=default;through=default;
  return DateTime.TryParseExact(FromLocalInclusive,"yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out from)&&DateTime.TryParseExact(ThroughLocalInclusive,"yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out through)&&from.Year>=2000&&through.Year<=2099&&from<=through&&through-from<=TimeSpan.FromDays(31);
 }
 public bool IsValid()=>Approval?.IsValid()==true&&Bounds(out var from,out var through)&&(Approval.Erase.ReadFormat!=ZkAttendanceReadFormats.Legacy||(from.Second==0&&through.Second==59));
 public bool Contains(ZkAttendanceRaw row)
 {
  if(!Bounds(out var from,out var through))throw new InvalidDataException("Exact closed wall-clock bounds required.");
  // Invalid native calendar values cannot safely be classified as outside the deletion range.
  var value=new DateTime(row.Year,row.Month,row.Day,row.Hour,row.Minute,row.Second,DateTimeKind.Unspecified);
  return from<=value&&value<=through;
 }
 public override string ToString()=>"Device-local closed deletion range [metadata redacted]";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AttendanceRangeEraseInput(ZkProbeInput Connection,Guid OperationId,AttendanceRangeErasePlan Plan,int ExpectedCount,string ArchiveDigest,int SelectedCount,string RemainingDigest,bool EraseAttendanceRange=true)
{
 public bool IsValid()=>EraseAttendanceRange&&Connection?.IsValid()==true&&Connection.LocalTransport is null&&OperationId!=Guid.Empty&&Plan?.IsValid()==true&&Connection.ExpectedSerial==Plan.Approval.Erase.Device.Serial&&ExpectedCount>=0&&ExpectedCount<=Plan.Approval.Erase.MaximumRecords&&SelectedCount>=0&&SelectedCount<=ExpectedCount&&AttendanceArchiveDigest.Valid(ArchiveDigest)&&AttendanceArchiveDigest.Valid(RemainingDigest);
 public override string ToString()=>"Attendance range erase IPC [private metadata redacted]";
}
public interface IAttendanceRangeEraseSession:IAttendanceEraseSession
{
 bool DeleteAttendanceRange(string fromLocalInclusive,string throughLocalInclusive);
}
public static class AttendanceRangeEraseExecutor
{
 public static bool Known(string state)=>state is "range-deleted-verified" or "range-empty" or "archive-mismatch" or "failed-before-write" or "unknown" or "unsupported";
 public static AttendanceEraseOutput Execute(AttendanceRangeEraseInput input,IAttendanceRangeEraseSession session)
 {
  AttendanceEraseOutput Result(string state)=>new(input.OperationId,state);bool dispatched=false;
  if(!input.IsValid())return Result("unsupported");
  try
  {
   var a=input.Plan.Approval.Erase;
   (int Count,string Digest,int Selected,string Remaining)? Scan()
   {
    if(session.Identity()!=a.Device)return null;
    var before=session.RecordCount();if(before is null or <0||before>a.MaximumRecords||!session.Begin())return null;
    var all=new AttendanceArchiveDigest(a.MaximumRecords);var outside=new AttendanceArchiveDigest(a.MaximumRecords);int selected=0;
    while(session.Next(a.ReadFormat)is{} row){all.Add(row,a.ReadFormat);if(input.Plan.Contains(row))selected++;else outside.Add(row,a.ReadFormat);}
    return session.RecordCount()==before&&all.Count==before&&session.Identity()==a.Device?(all.Count,all.Finish(),selected,outside.Finish()):null;
   }
   var observed=Scan();if(observed is null)return Result("failed-before-write");
   if(observed.Value!=(input.ExpectedCount,input.ArchiveDigest,input.SelectedCount,input.RemainingDigest))return Result("archive-mismatch");
   if(input.SelectedCount==0)return Result("range-empty");
   if(Scan()!=observed)return Result("archive-mismatch");
   // External installed capture quiescence must still hold. This is not a firmware CAS.
   dispatched=true;if(!session.DeleteAttendanceRange(input.Plan.FromLocalInclusive,input.Plan.ThroughLocalInclusive))return Result("unknown");
   var after=Scan();return Result(after is{} left&&left.Count==input.ExpectedCount-input.SelectedCount&&left.Selected==0&&left.Digest==input.RemainingDigest?"range-deleted-verified":"unknown");
  }
  catch{return Result(dispatched?"unknown":"failed-before-write");}
 }
}
