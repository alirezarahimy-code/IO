using System.Runtime.InteropServices;
using IO.DeviceContracts;
internal static class FunctionKeyWriteTests
{
 internal static readonly ZkFunctionKeyValue Old=new(0,true,0,4,"old label",2,"raw;old-schedule");
 internal static readonly ZkFunctionKeyValue New=Old with{StateCode=5,StateName="new label"};
 public static async Task RunAsync()
 {
  var count=0;void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);count++;Console.WriteLine("PASS key write: "+name);}
  var plan=new ZkFunctionKeyPlan(ZkFunctionKeyPlan.Version,Guid.NewGuid(),"key-fixture","product-A","fw1",Old,New,"documented-effect","lab-reference");
  var approval=new ZkFunctionKeyWriteApproval(plan.Product,plan.Firmware,[Old,New],plan.Effect,plan.ApprovalReference,AttendanceOnly:true);
  var pin=new ZkProbeInput(Guid.NewGuid(),"10.20.30.40",4370,1,plan.Serial,0);
  var input=new ZkOperationInput(pin,"settings.key.set",null,false,AllowAdvancedSettingsRead:true,FunctionKeyPlan:plan,AllowFunctionKeyWrite:true,FunctionKeyWriteApproval:approval);
  Check(!(approval with{AttendanceOnly=false}).IsValid()&&input.IsValid()&&!(input with{AllowFunctionKeyWrite=false}).IsValid()&&!(input with{FunctionKeyWriteApproval=null}).IsValid(),"default off exact installed tuple approval required");
  Check(!(plan with{Desired=Old}).IsValid()&&!(plan with{Desired=New with{Index=1}}).IsValid()&&!(plan with{Expected=new(0,false)}).IsValid(),"complete different tuples at the same explicit index required");
  Check(!(plan with{Desired=New with{AutoChangeTime=null}}).IsValid()&&!(plan with{Desired=New with{StateName="\uD800"}}).IsValid(),"missing raw schedule and malformed Unicode rejected not fabricated");
  Check(new[]{approval with{Firmware="other"},approval with{Effect="guessed"},approval with{ApprovedStates=[Old,New with{AutoChange=0,AutoChangeTime="00:00;"}]},approval with{EvidenceReference="other"}}.All(a=>!a.Allows(plan)),"model effect reference and every field of both tuples match without schedule reset");
  Check(!(approval with{ApprovedStates=[Old,Old]}).IsValid()&&!(approval with{ApprovedStates=null!}).IsValid()&&!(approval with{ApprovedStates=[Old,New with{Index=1}]}).IsValid(),"installation states bounded unique null safe and single-index");
  Check(plan.PreviewHash()!=(plan with{Desired=New with{AutoChangeTime="different"}}).PreviewHash()&&plan.PreviewHash()!=(plan with{Expected=Old with{Function=1}}).PreviewHash(),"preview binds old and new function state label auto-change and raw schedule");
  Check(!(input with{Operation="information.read"}).IsValid()&&!(input with{ClockLocal="2026-11-08 10:00:00"}).IsValid()&&!(input with{Connection=pin with{ExpectedSerial="foreign"}}).IsValid()&&!(input with{RawSettingPlan=new(ZkRawSettingPlan.Version,Guid.NewGuid(),plan.Serial,plan.Product,plan.Firmware,"repeat-time",1,2,"unit","effect","ref")}).IsValid(),"key payload cannot cross another operation serial or raw setting branch");
  var session=new Session();Check(ZkFunctionKeyWriteExecutor.Execute(input with{AllowFunctionKeyWrite=false},session).State=="unsupported"&&session.Calls==0,"unapproved request touches no SDK method");
  foreach(var bad in new[]{new Session{SerialValue="foreign"},new Session{ProductValue="other"},new Session{FirmwareValue=null},new Session{Value=Old with{AutoChangeTime="drift"}},new Session{ThrowBefore=true}})Check(ZkFunctionKeyWriteExecutor.Execute(input,bad).State=="failed"&&bad.Writes==0,"preflight exact identity and complete old tuple prevent dispatch "+count);
  session=new();var result=ZkFunctionKeyWriteExecutor.Execute(input,session);Check(result.IsValidFor(input)&&session.Writes==1&&session.Value==New&&result.FunctionKey!.Key.AutoChange==2&&result.FunctionKey.Key.AutoChangeTime==Old.AutoChangeTime,"single dispatch preserves existing schedule and success needs exact full readback");
  foreach(var bad in new[]{new Session{False=true},new Session{ThrowWrite=true},new Session{WrongReadback=true},new Session{Drift=true},new Session{ThrowAfter=true}})Check(ZkFunctionKeyWriteExecutor.Execute(input,bad).State=="unknown"&&bad.Writes==1,"post-dispatch ambiguity never retries "+count);
  Check(!new ZkOperationOutput("succeeded").IsValidFor(input)&&!(result with{FunctionKey=result.FunctionKey! with{Key=New with{AutoChange=0}}}).IsValidFor(input)&&!(result with{State="unknown"}).IsValidFor(input),"bare ACK partial schedule and mixed unknown evidence rejected");
  var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(FunctionKeyWriteTests).Assembly.Location;
  Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input,5)).IsValidFor(input),"real isolated child round-trips all tuple fields without vendor SDK");
  var largeOld=Old with{StateName=new string('ژ',128),AutoChangeTime=new string('ژ',128)};var states=Enumerable.Range(0,8).Select(i=>largeOld with{StateCode=i}).ToArray();
  var largePlan=plan with{Serial="key-long",Product=new string('ژ',120),Firmware=new string('ژ',120),Expected=states[0],Desired=states[1],Effect=new string('ژ',200),ApprovalReference=new string('ژ',200)};
  var largeApproval=new ZkFunctionKeyWriteApproval(largePlan.Product,largePlan.Firmware,states,largePlan.Effect,largePlan.ApprovalReference,true);
  var largeInput=input with{Connection=pin with{ExpectedSerial=largePlan.Serial},FunctionKeyPlan=largePlan,FunctionKeyWriteApproval=largeApproval};var size=System.Text.Json.JsonSerializer.Serialize(largeInput).Length;
  Check(size>8192&&size<=32768&&! (largeApproval with{ApprovedStates=states.Append(largeOld with{StateCode=8}).ToArray()}).IsValid()&&(await NativeProcessSupervisor.RunOperationAsync(host,assembly,largeInput,5)).IsValidFor(largeInput),"maximum Unicode tuples and eight installed states fit explicit private IPC budget without truncation");
  Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="key-hang"},FunctionKeyPlan=plan with{Serial="key-hang"}},1)).State=="unknown","external deadline kills hung child without declaring safe retry");
  Console.WriteLine($"PASS {count} function key write contract checks; synthetic only not model SDK or hardware acceptance");
 }
 internal sealed class Session:IZkFunctionKeyWriteSession
 {
  public string? SerialValue="key-fixture",ProductValue="product-A",FirmwareValue="fw1";public ZkFunctionKeyValue? Value=Old;public int Writes,Calls;public bool False,ThrowBefore,ThrowWrite,ThrowAfter,WrongReadback,Drift;
  public string? Serial(){Calls++;if(ThrowBefore)throw new IOException();return Drift&&Writes>0?"foreign":SerialValue;}
  public string? Product(){Calls++;return ProductValue;}public string? Firmware(){Calls++;return FirmwareValue;}
  public ZkFunctionKeyValue? Read(int index){Calls++;if(ThrowAfter&&Writes>0)throw new IOException();return WrongReadback&&Writes>0?Value! with{AutoChangeTime="drift"}:Value;}
  public bool Set(ZkFunctionKeyValue value){Calls++;Writes++;Value=value;if(ThrowWrite)throw new IOException();return !False;}
 }
}
