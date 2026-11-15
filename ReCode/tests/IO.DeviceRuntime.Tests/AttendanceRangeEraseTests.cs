using System.Text.Json;
using IO.DeviceContracts;
internal static class AttendanceRangeEraseTests
{
 internal static async Task RunAsync()
 {
  int checks=0;void Check(bool b,string m){if(!b)throw new InvalidOperationException(m);checks++;Console.WriteLine("PASS "+m);}
  var a=Approval();var plan=Plan(a);var rows=Rows();var input=Input(plan,rows);
  string Run(Session s,AttendanceRangeEraseInput? i=null)=>AttendanceRangeEraseExecutor.Execute(i??input,s).State;
  Check(input.IsValid()&&input.SelectedCount==3,"range commitment selects both inclusive seconds and duplicate multiplicity");
  Check(!(a with{AllowRangeErase=false}).IsValid()&&!(a with{IncludeAllRepeatedWallClockOccurrences=false}).IsValid(),"range approval and repeated-wall-clock semantics are explicit default-off");
  Check(!(a with{BoundaryReference=""}).IsValid()&&!(a with{TimestampPrecisionReference=""}).IsValid(),"installed boundary and native timestamp precision evidence cannot be inferred from method name");
  Check(!(a with{TimeZoneId="unknown-zone"}).IsValid()&&!(a with{NativeBoundaryConvention="UTC"}).IsValid(),"unknown zone or UTC reinterpretation is unsupported");
  Check(!(plan with{FromLocalInclusive="2026-11-08T06:00:00Z"}).IsValid()&&!(plan with{ThroughLocalInclusive="2026-11-08 06:00:00.001"}).IsValid(),"strict local-wall seconds reject UTC offsets and fractional rounding");
  Check(!(plan with{ThroughLocalInclusive="2026-02-30 06:00:00"}).IsValid()&&!(plan with{ThroughLocalInclusive="2026-11-08 05:59:59"}).IsValid(),"invalid calendar and reversed interval fail before dispatch");
  Check(!(plan with{ThroughLocalInclusive="2027-01-01 00:00:00"}).IsValid()&&!(plan with{FromLocalInclusive="1999-11-08 06:00:00"}).IsValid(),"oversized or out-of-year range cannot be split or guessed");
  var minute=plan with{Approval=a with{Erase=a.Erase with{ReadFormat=ZkAttendanceReadFormats.Legacy}}};
  Check(!minute.IsValid()&&(minute with{ThroughLocalInclusive="2026-11-08 06:00:59"}).IsValid(),"legacy minute precision permits only complete local minutes");
  var repeated=plan with{FromLocalInclusive="2026-11-01 01:00:00",ThroughLocalInclusive="2026-11-01 01:59:59"};
  Check(repeated.IsValid()&&repeated.Contains(rows[1] with{Day=1,Hour=1,Minute=30}),"fall-back wall interval explicitly selects every occurrence, never one guessed UTC instant");
  var s=new Session(plan,rows);Check(Run(s)=="range-deleted-verified"&&s.Writes==1&&s.Scans==3&&s.ClearWrites==0,"two whole scans then one range-only call and exact complete outside-multiset readback");
  s=new(plan,rows);Check(Run(s,input with{SelectedCount=2})=="archive-mismatch"&&s.Writes==0,"wrong selected count cannot authorize deletion");
  s=new(plan,rows);Check(Run(s,input with{RemainingDigest=Digest([])})=="archive-mismatch"&&s.Writes==0,"wrong preserved multiset cannot authorize deletion");
  s=new(plan,rows);Check(Run(s,input with{ArchiveDigest=Digest(rows.Skip(1))})=="archive-mismatch"&&s.Writes==0,"whole native archive remains mandatory even for narrow selection");
  s=new(plan,rows){ChangeSecond=true};Check(Run(s)=="archive-mismatch"&&s.Writes==0,"same-count change between two scans fails closed");
  s=new(plan,rows){BadIdentity=true};Check(Run(s)=="failed-before-write"&&s.Writes==0,"exact installed serial product firmware required");
  s=new(plan,rows){Incomplete=true};Check(Run(s)=="failed-before-write"&&s.Writes==0,"incomplete native enumeration is not an empty range");
  s=new(plan,rows){FailCount=true};Check(Run(s)=="failed-before-write"&&s.Writes==0,"unavailable count cannot authorize range erase");
  s=new(plan,rows){FailBegin=true};Check(Run(s)=="failed-before-write"&&s.Writes==0,"failed full read start never fabricates a safe range");
  s=new(plan,[rows[0] with{Month=13}]);Check(Run(s)=="failed-before-write"&&s.Writes==0,"unclassifiable native date is not silently preserved as outside range");
  var outside=rows.Where(r=>!plan.Contains(r)).ToArray();s=new(plan,outside);Check(Run(s,Input(plan,outside))=="range-empty"&&s.Writes==0,"fresh complete archive with zero selected rows is a no-op");
  s=new(plan,rows){ReturnFalse=true};Check(Run(s)=="unknown"&&s.Writes==1,"false range response is ambiguous and never replayed");
  s=new(plan,rows){ThrowWrite=true};Check(Run(s)=="unknown"&&s.Writes==1,"exception after range dispatch remains unknown");
  s=new(plan,rows){KeepSelected=true};Check(Run(s)=="unknown"&&s.Writes==1,"ACK retaining even one boundary record fails closed");
  s=new(plan,rows){DeleteOutside=true};Check(Run(s)=="unknown"&&s.Writes==1,"over-deletion outside range cannot be reported as verified");
  s=new(plan,rows){CorruptOutside=true};Check(Run(s)=="unknown"&&s.Writes==1,"same remaining count but altered outside tuple cannot be reported as verified");
  s=new(plan,rows){FailPostRead=true};Check(Run(s)=="unknown"&&s.Writes==1,"post-dispatch read failure remains unknown");
  s=new(plan,rows);Check(Run(s,input with{EraseAttendanceRange=false})=="unsupported"&&s.Writes==0,"range discriminator cannot be downgraded into another operation");
  var sdk=new RangeEraseSdkDouble();Check(new ZkComAttendanceSession(sdk,7).DeleteAttendanceRange(plan.FromLocalInclusive,plan.ThroughLocalInclusive)&&sdk.Machine==7&&sdk.From==plan.FromLocalInclusive&&sdk.Through==plan.ThroughLocalInclusive,"production COM adapter passes exact machine and two invariant local strings to pinned method only");
  var host=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(AttendanceRangeEraseTests).Assembly.Location;
  Check((await NativeProcessSupervisor.RunAttendanceRangeEraseAsync(host,assembly,input,5)).State=="range-deleted-verified","production process supervisor executes synthetic range executor and correlated readback");
  foreach(var kind in new[]{"SYNTHETIC-EXTRA","SYNTHETIC-FOREIGN","SYNTHETIC-HANG","SYNTHETIC-ALL-STATE"})
  {
   var altered=input with{Plan=plan with{Approval=a with{BoundaryReference=kind}}};
   Check((await NativeProcessSupervisor.RunAttendanceRangeEraseAsync(host,assembly,altered,kind.EndsWith("HANG",StringComparison.Ordinal)?1:5)).State=="unknown","range child "+kind+" cannot forge safe completion or all-log result");
  }
  using var stop=new CancellationTokenSource();stop.Cancel();Check((await NativeProcessSupervisor.RunAttendanceRangeEraseAsync(host,assembly,input,5,stop.Token)).State=="failed-before-write","pre-cancelled range never starts child");
  Console.WriteLine("::notice title=Attendance range erase contracts::"+checks+" synthetic checks passed; real production mapper and supervisor, no installed SDK boundary proof or device deletion.");
 }
 internal static AttendanceRangeEraseApproval Approval()=>new(new(new("SYNTHETIC-RANGE","PRODUCT","FIRMWARE"),ZkAttendanceReadFormats.Ssr,"SYNTHETIC-AUTH","SYNTHETIC-RETENTION","SYNTHETIC-STOP","SYNTHETIC-RESET",CaptureStoppedDuringMaintenance:true,ResetClearsPendingWrites:true,AllowErase:true),"America/Los_Angeles","SYNTHETIC-CLOSED-BOUNDARY","SYNTHETIC-SECONDS",IncludeAllRepeatedWallClockOccurrences:true,AllowRangeErase:true);
 internal static AttendanceRangeErasePlan Plan(AttendanceRangeEraseApproval a)=>new(a,"2026-11-08 06:00:00","2026-11-08 06:00:01");
 internal static ZkAttendanceRaw[] Rows()
 {
  ZkAttendanceRaw r=new("001",1,77,2026,11,8,6,0,0,5);
  return [r with{Hour=5,Minute=59,Second=59},r,r,r with{Second=1},r with{Second=2},r with{Second=2}];
 }
 internal static string Digest(IEnumerable<ZkAttendanceRaw> rows){var d=new AttendanceArchiveDigest(100000);foreach(var row in rows)d.Add(row,ZkAttendanceReadFormats.Ssr);return d.Finish();}
 internal static AttendanceRangeEraseInput Input(AttendanceRangeErasePlan plan,ZkAttendanceRaw[] rows)=>new(new(Guid.NewGuid(),"192.0.2.42",4370,7,plan.Approval.Erase.Device.Serial,0),Guid.NewGuid(),plan,rows.Length,Digest(rows),rows.Count(plan.Contains),Digest(rows.Where(r=>!plan.Contains(r))));
 internal static async Task ChildAsync(string text)
 {
  var input=JsonSerializer.Deserialize<AttendanceRangeEraseInput>(text)!;var marker=input.Plan.Approval.BoundaryReference;
  if(marker=="SYNTHETIC-HANG"){await Task.Delay(TimeSpan.FromMinutes(1));return;}
  var output=AttendanceRangeEraseExecutor.Execute(input,new Session(input.Plan,Rows()));
  if(marker=="SYNTHETIC-EXTRA")Console.WriteLine("{\"OperationId\":\""+input.OperationId+"\",\"State\":\"range-deleted-verified\",\"Raw\":[]}");
  else Console.WriteLine(JsonSerializer.Serialize(marker=="SYNTHETIC-FOREIGN"?output with{OperationId=Guid.NewGuid()}:marker=="SYNTHETIC-ALL-STATE"?output with{State="deleted-verified"}:output));
 }
 private sealed class Session(AttendanceRangeErasePlan plan,ZkAttendanceRaw[] initial):IAttendanceRangeEraseSession
 {
  private ZkAttendanceRaw[] rows=initial.ToArray();private int cursor;
  public int Writes,Scans,ClearWrites;public bool ChangeSecond,BadIdentity,Incomplete,FailCount,FailBegin,ReturnFalse,ThrowWrite,KeepSelected,DeleteOutside,CorruptOutside,FailPostRead;
  public BiometricDeviceIdentity Identity()=>BadIdentity?plan.Approval.Erase.Device with{Firmware="OTHER"}:plan.Approval.Erase.Device;
  public string? Serial()=>Identity().Serial;
  public int? RecordCount()=>FailCount?null:rows.Length;
  public bool Begin(){cursor=0;Scans++;if(ChangeSecond&&Scans==2)rows[0]=rows[0] with{WorkCode=99};return !FailBegin&&!(FailPostRead&&Writes>0);}
  public ZkAttendanceRaw? Next(string format)=>cursor<(Incomplete?rows.Length-1:rows.Length)?rows[cursor++]:null;
  public bool ClearAttendance(){ClearWrites++;throw new InvalidOperationException("All-log clear forbidden.");}
  public bool DeleteAttendanceRange(string from,string through)
  {
   Writes++;if(from!=plan.FromLocalInclusive||through!=plan.ThroughLocalInclusive)throw new InvalidOperationException();
   if(ThrowWrite)throw new InvalidOperationException();if(ReturnFalse)return false;
   rows=rows.Where(r=>!plan.Contains(r)).ToArray();
   if(KeepSelected)rows=[..rows,initial.First(plan.Contains)];if(DeleteOutside)rows=rows.Skip(1).ToArray();if(CorruptOutside&&rows.Length>0)rows[0]=rows[0] with{WorkCode=99};return true;
  }
 }
}
public sealed class RangeEraseSdkDouble
{
 public int Machine;public string? From,Through;
 public bool DeleteAttlogBetweenTheDate(int machine,string from,string through){Machine=machine;From=from;Through=through;return true;}
}
