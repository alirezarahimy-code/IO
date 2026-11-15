using IO.DeviceContracts;
internal static class AttendanceEraseTests
{
 internal static async Task RunAsync()
 {
  int count=0;void Check(bool b,string m){if(!b)throw new InvalidOperationException(m);count++;Console.WriteLine("PASS "+m);}
  var identity=new BiometricDeviceIdentity("SYNTHETIC-ERASE","PRODUCT","FIRMWARE");
  var approval=new AttendanceEraseApproval(identity,ZkAttendanceReadFormats.Ssr,"SYNTHETIC-AUTH","SYNTHETIC-RETENTION","SYNTHETIC-PHYSICAL-CAPTURE-STOP","SYNTHETIC-RESET",AllowErase:true,CaptureStoppedDuringMaintenance:true,ResetClearsPendingWrites:true);
  ZkAttendanceRaw row=new("001",1,0,2026,11,8,7,0,0,0);ZkAttendanceRaw other=row with{PersonIdentifier="0002",CheckType=1};
  ZkAttendanceRaw[] rows=[row,other,row];
  string Digest(IEnumerable<ZkAttendanceRaw> rs){var d=new AttendanceArchiveDigest(approval.MaximumRecords);foreach(var r in rs)d.Add(r,approval.ReadFormat);return d.Finish();}
  var input=new AttendanceEraseInput(new(Guid.NewGuid(),"192.0.2.41",4370,7,identity.Serial,0),Guid.NewGuid(),approval,rows.Length,Digest(rows));
  string Run(Session s,AttendanceEraseInput? i=null)=>AttendanceEraseExecutor.Execute(i??input,s).State;
  Check(input.IsValid()&&!(approval with{AllowErase=false}).IsValid(),"delete-all is exact-model and default-off");
  Check(!(approval with{CaptureStoppedDuringMaintenance=false}).IsValid()&&!(approval with{ResetClearsPendingWrites=false}).IsValid(),"stable count is not approved capture quiescence or pending-write reset");
  Check(!(approval with{RetentionReference=""}).IsValid()&&!(approval with{MaximumRecords=100001}).IsValid(),"retention and bounded whole-archive size are mandatory");
  Check(Digest(rows)==Digest(rows.Reverse())&&Digest(rows)!=Digest([row,other]),"whole archive commitment ignores ordering but preserves duplicate multiplicity");
  Check(Digest(rows)!=Digest([row with{PersonIdentifier="1"},other,row]),"exact leading zeros are not normalized in destructive preflight");
  var s=new Session(identity,rows);Check(Run(s)=="deleted-verified"&&s.Writes==1&&s.Scans==3,"delete once only after two complete matching scans and verify actual empty log after clear");
  s=new(identity,[]);Check(Run(s,input with{ExpectedCount=0,ArchiveDigest=Digest([])})=="already-empty"&&s.Writes==0,"empty archived device is a no-op, not destructive success");
  s=new(identity,rows);Check(Run(s,input with{ArchiveDigest=Digest([row,other])})=="archive-mismatch"&&s.Writes==0,"same archive metadata with different multiset cannot dispatch clear");
  s=new(identity,rows){ChangeOnSecond=true};Check(Run(s)=="archive-mismatch"&&s.Writes==0,"same-count replacement between scans refuses deletion");
  s=new(identity,rows){FailBegin=true};Check(Run(s)=="failed-before-write"&&s.Writes==0,"read-start failure never fabricates empty log");
  s=new(identity,rows){Incomplete=true};Check(Run(s)=="failed-before-write"&&s.Writes==0,"incomplete enumeration cannot authorize clear");
  s=new(identity,rows){CountUnavailable=true};Check(Run(s)=="failed-before-write"&&s.Writes==0,"unavailable native count fails before write");
  s=new(identity with{Firmware="OTHER"},rows);Check(Run(s)=="failed-before-write"&&s.Writes==0,"full firmware/product/serial pin checked before any clear");
  s=new(identity,rows){ClearResult=false};Check(Run(s)=="unknown"&&s.Writes==1,"false clear response remains unknown and is not replayed");
  s=new(identity,rows){ThrowClear=true};Check(Run(s)=="unknown"&&s.Writes==1,"clear exception cannot claim failed-before-write");
  s=new(identity,rows){RetainAfterClear=true};Check(Run(s)=="unknown"&&s.Writes==1,"ACK without empty complete readback remains unknown");
  s=new(identity,rows);Check(Run(s,input with{EraseAllAttendance=false})=="unsupported"&&s.Writes==0,"typed discriminator cannot be downgraded into an arbitrary SDK command");
  var sdk=new EraseSdkDouble();Check(new ZkComAttendanceSession(sdk,7).ClearAttendance()&&sdk.Flag==1&&sdk.Machine==7,"production COM mapper binds ClearData to flag1 only, never all-data flag5");
  var host=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(AttendanceEraseTests).Assembly.Location;
  Check((await NativeProcessSupervisor.RunAttendanceEraseAsync(host,assembly,input,5)).State=="deleted-verified","production child supervisor accepts strictly correlated delete result");
  foreach(var kind in new[]{"SYNTHETIC-EXTRA","SYNTHETIC-FOREIGN","SYNTHETIC-HANG"})Check((await NativeProcessSupervisor.RunAttendanceEraseAsync(host,assembly,input with{Approval=approval with{AuthorizationReference=kind}},kind.EndsWith("HANG",StringComparison.Ordinal)?1:5)).State=="unknown","child "+kind+" cannot manufacture safe destructive completion");
  using var cancelled=new CancellationTokenSource();cancelled.Cancel();Check((await NativeProcessSupervisor.RunAttendanceEraseAsync(host,assembly,input,5,cancelled.Token)).State=="failed-before-write","pre-cancelled erase never starts a child");
  Console.WriteLine("::notice title=Attendance erase contracts::"+count+" synthetic checks passed; production fixed-flag mapper and supervisor, no SDK device or real deletion.");
 }
 private sealed class Session(BiometricDeviceIdentity identity,ZkAttendanceRaw[] initial):IAttendanceEraseSession
 {
  private ZkAttendanceRaw[] rows=initial.ToArray();private int cursor;
  public int Writes,Scans;public bool ChangeOnSecond,FailBegin,Incomplete,CountUnavailable,ClearResult=true,ThrowClear,RetainAfterClear;
  public BiometricDeviceIdentity Identity()=>identity;
  public string? Serial()=>identity.Serial;
  public int? RecordCount()=>CountUnavailable?null:rows.Length;
  public bool Begin(){Scans++;cursor=0;if(ChangeOnSecond&&Scans==2)rows=[rows[0] with{WorkCode=99},..rows.Skip(1)];return !FailBegin;}
  public ZkAttendanceRaw? Next(string format)=>cursor<(Incomplete?rows.Length-1:rows.Length)?rows[cursor++]:null;
  public bool ClearAttendance(){Writes++;if(ThrowClear)throw new InvalidOperationException();if(ClearResult&&!RetainAfterClear)rows=[];return ClearResult;}
 }
}
public sealed class EraseSdkDouble
{
 public int Flag,Machine;
 public bool ClearData(int machine,int flag){Machine=machine;Flag=flag;return true;}
}
