using System.Runtime.InteropServices;
using IO.DeviceContracts;
internal static class RawSettingTests
{
 public static async Task RunAsync()
 {
  var count=0;void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);count++;Console.WriteLine("PASS raw setting: "+name);}
  var plan=new ZkRawSettingPlan(ZkRawSettingPlan.Version,Guid.NewGuid(),"raw-fixture","product-A","fw1","repeat-time",-1,0,"model-defined-unit","model-documented-effect","lab-reference");
  var approval=new ZkRawSettingApproval(plan.Product,plan.Firmware,plan.Parameter,[-1,0,1],plan.Unit,plan.Effect,plan.ApprovalReference);
  var pin=new ZkProbeInput(Guid.NewGuid(),"10.20.30.40",4370,1,plan.Serial,0);
  var input=new ZkOperationInput(pin,"settings.raw.set",null,false,AllowAdvancedSettingsRead:true,RawSettingPlan:plan,AllowRawSettingsWrite:true,RawSettingApproval:approval);
  Check(input.IsValid()&&!(input with{AllowRawSettingsWrite=false}).IsValid()&&!(input with{RawSettingApproval=null}).IsValid(),"write off by default and exact independent installed approval required");
  Check(new[]{"one-to-one","one-to-n","repeat-time","card-registration","mifare-sector"}.Select(ZkRawParameters.Code).SequenceEqual(new[]{25,23,8,30,62})&&!ZkRawParameters.IsKnown("4")&&!ZkRawParameters.IsKnown("repeat-time "),"only five exact names map to observed reference codes never generic numeric options");
  Check(!(plan with{ExpectedValue=0}).IsValid()&&!(plan with{SnapshotId=Guid.Empty}).IsValid()&&!(plan with{Product="\uD800"}).IsValid(),"changed value valid encoding and source snapshot required");
  Check(new[]{approval with{Firmware="other"},approval with{Unit="seconds"},approval with{Effect="guessed"},approval with{EvidenceReference="other"},approval with{ApprovedValues=[0,1]}}.All(x=>!x.Allows(plan)),"firmware unit effect evidence and both old and new raw values match exactly");
  Check(!(approval with{ApprovedValues=[0,0]}).IsValid()&&!(approval with{ApprovedValues=null!}).IsValid()&&!(approval with{ApprovedValues=Enumerable.Range(0,65).ToArray()}).IsValid(),"installed list unique explicit bounded and null safe");
  Check(plan.PreviewHash()!=(plan with{Unit="other"}).PreviewHash()&&plan.PreviewHash()!=(plan with{Parameter="one-to-n"}).PreviewHash(),"preview binds parameter unit and all descriptors");
  Check(!(input with{Operation="information.read"}).IsValid()&&!(input with{ClockLocal="2026-11-08 10:00:00"}).IsValid()&&!(input with{Connection=pin with{ExpectedSerial="foreign"}}).IsValid(),"raw payload cannot cross read clock or serial branches");
  var session=new Session();Check(ZkRawSettingExecutor.Execute(input with{AllowRawSettingsWrite=false},session).State=="unsupported"&&session.Calls==0,"invalid request touches no SDK method");
  foreach(var bad in new[]{new Session{SerialValue="other"},new Session{ProductValue="other"},new Session{FirmwareValue=null},new Session{Value=2},new Session{ThrowBefore=true}})Check(ZkRawSettingExecutor.Execute(input,bad).State=="failed"&&bad.Writes==0,"preflight mismatch prevents dispatch "+count);
  session=new();var result=ZkRawSettingExecutor.Execute(input,session);Check(result.IsValidFor(input)&&result.RawSetting?.Value==0&&session.Writes==1,"one dispatch succeeds only after exact model pinned readback");
  foreach(var bad in new[]{new Session{False=true},new Session{ThrowWrite=true},new Session{WrongReadback=true},new Session{Drift=true},new Session{ThrowAfter=true}})Check(ZkRawSettingExecutor.Execute(input,bad).State=="unknown"&&bad.Writes==1,"post-dispatch ambiguity never replays "+count);
  Check(!new ZkOperationOutput("succeeded").IsValidFor(input)&&!(result with{RawSetting=result.RawSetting! with{Value=1}}).IsValidFor(input)&&!(result with{State="unknown"}).IsValidFor(input),"bare ACK wrong value and mixed unknown evidence rejected");
  var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(RawSettingTests).Assembly.Location;
  Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input,5)).IsValidFor(input),"real isolated child round-trips typed approved result not vendor acceptance");
  var largePlan=plan with{Serial="raw-long",Product=new string('ژ',120),Firmware=new string('ژ',120),Unit=new string('ژ',80),Effect=new string('ژ',200),ApprovalReference=new string('ژ',200)};
  var largeApproval=new ZkRawSettingApproval(largePlan.Product,largePlan.Firmware,largePlan.Parameter,[-1,0],largePlan.Unit,largePlan.Effect,largePlan.ApprovalReference);
  var largeInput=input with{Connection=pin with{ExpectedSerial=largePlan.Serial},RawSettingPlan=largePlan,RawSettingApproval=largeApproval};
  Check(System.Text.Json.JsonSerializer.Serialize(largeInput).Length>8192&&(await NativeProcessSupervisor.RunOperationAsync(host,assembly,largeInput,5)).IsValidFor(largeInput),"bounded maximum Unicode descriptors round-trip beyond old input budget without trimming or truncation");
  Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="raw-hang"},RawSettingPlan=plan with{Serial="raw-hang"}},1)).State=="unknown","external deadline kills child and preserves unknown without write retry");
  Console.WriteLine($"PASS {count} raw setting contract checks; synthetic only not SDK or hardware acceptance");
 }
 internal sealed class Session:IZkRawSettingSession
 {
  public string? SerialValue="raw-fixture",ProductValue="product-A",FirmwareValue="fw1";public int? Value=-1;public int Writes,Calls;public bool False,ThrowBefore,ThrowWrite,ThrowAfter,WrongReadback,Drift;
  public string? Serial(){Calls++;if(ThrowBefore)throw new IOException();return Drift&&Writes>0?"foreign":SerialValue;}
  public string? Product(){Calls++;return ProductValue;}public string? Firmware(){Calls++;return FirmwareValue;}
  public int? Read(string parameter){Calls++;if(ThrowAfter&&Writes>0)throw new IOException();return WrongReadback&&Writes>0?2:Value;}
  public bool Set(string parameter,int value){Calls++;Writes++;Value=value;if(ThrowWrite)throw new IOException();return !False;}
 }
}
