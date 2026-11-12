using System.Runtime.InteropServices;
using System.Text.Json;
using IO.DeviceContracts;
internal static class IdleTests
{
 public static async Task RunAsync()
 {
  int count=0;void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);Console.WriteLine("PASS idle "+message);count++;}
  var plan=new ZkIdlePlan(ZkIdlePlan.Version,Guid.NewGuid(),"idle-fixture","product-A","firmware-1",5,10,"lab-evidence-1");
  var approval=new ZkIdleApproval(plan.Product,plan.Firmware,[10],plan.ApprovalReference,"standby-or-power-off");
  var pin=new ZkProbeInput(Guid.NewGuid(),"10.20.30.40",4370,1,plan.Serial,0);
  var input=new ZkOperationInput(pin,"settings.idle.set",null,false,IdlePlan:plan,AllowIdleMinutesWrite:true,IdleApproval:approval);
  Check(input.IsValid()&&!(input with{AllowIdleMinutesWrite=false}).IsValid()&&!(input with{IdleApproval=null}).IsValid(),"write defaults off and needs independent exact installed approval");
  Check(new[]{0,-1,1000,int.MaxValue}.All(x=>!(plan with{NewMinutes=x}).IsValid()&&!(plan with{ExpectedMinutes=x}).IsValid())&&!(plan with{NewMinutes=5}).IsValid(),"zero disabled unknown out-of-envelope and unchanged values are rejected");
  Check(new[]{approval with{Product="product-a"},approval with{Firmware="firmware-2"},approval with{ApprovedMinutes=[11]},approval with{EvidenceReference="other"},approval with{Effect="sleep-only"}}.All(x=>!x.Allows(plan)),"model firmware target value evidence and power-off effect match exactly without inference");
  Check(!(approval with{ApprovedMinutes=[10,10]}).IsValid()&&!(approval with{ApprovedMinutes=null!}).IsValid()&&!(approval with{ApprovedMinutes=Enumerable.Range(1,65).ToArray()}).IsValid(),"installed allowlist is bounded nonempty unique and explicit");
  Check(!(plan with{SnapshotId=Guid.Empty}).IsValid()&&!(plan with{Contract="generic"}).IsValid()&&!(plan with{Product="\uD800"}).IsValid()&&!(plan with{ApprovalReference=" test "}).IsValid(),"snapshot version text encoding and exact reference validation are closed");
  Check(plan.PreviewHash()==(plan with{}).PreviewHash()&&plan.PreviewHash()!=(plan with{NewMinutes=11}).PreviewHash()&&plan.PreviewHash()!=(plan with{Firmware="firmware-2"}).PreviewHash(),"preview hash binds immutable snapshot descriptors old and new values");
  Check(!(input with{ClockLocal="2026-11-08 10:00:00"}).IsValid()&&!(input with{Connection=pin with{ExpectedSerial="foreign"}}).IsValid()&&!(input with{NetworkPlan=new("10.20.30.40","10.20.30.41",plan.Serial)}).IsValid()&&!(input with{Operation="information.read"}).IsValid(),"idle payload cannot cross clock network serial or generic read branches");
  var session=new Session();Check(ZkIdleSettingExecutor.Execute(input with{AllowIdleMinutesWrite=false},session).State=="unsupported"&&session.Calls==0,"unapproved request touches no SDK method");
  foreach(var bad in new[]{new Session{ProductValue="different"},new Session{FirmwareValue=null},new Session{Minutes=null},new Session{Minutes=6},new Session{ThrowPreflight=true}})
   Check(ZkIdleSettingExecutor.Execute(input,bad).State=="failed"&&bad.Writes==0,"live descriptor or old-value failure prevents dispatch "+count);
  session=new();var result=ZkIdleSettingExecutor.Execute(input,session);Check(result.State=="succeeded"&&result.IsValidFor(input)&&result.Idle?.Minutes==10&&session.Writes==1&&session.Minutes==10,"one code4 mutation succeeds only with exact typed post-readback");
  foreach(var bad in new[]{new Session{ReturnFalse=true},new Session{ThrowWrite=true},new Session{WrongReadback=true},new Session{DriftFirmware=true},new Session{ThrowReadback=true}})
   Check(ZkIdleSettingExecutor.Execute(input,bad).State=="unknown"&&bad.Writes==1,"post-dispatch ambiguity retains unknown without replay "+count);
  Check(!new ZkOperationOutput("succeeded").IsValidFor(input)&&!(result with{Idle=result.Idle! with{Minutes=11}}).IsValidFor(input)&&!(result with{Idle=result.Idle! with{Serial="other"}}).IsValidFor(input)&&!(result with{State="unknown"}).IsValidFor(input),"bare success foreign or mixed unknown evidence is rejected");
  Check(!new ZkOperationOutput("succeeded",Idle:result.Idle).IsValidFor(new(pin,"information.read",null,false)),"read operations cannot smuggle idle write evidence");
  var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(IdleTests).Assembly.Location;
  Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input,5)).Idle?.Minutes==10,"real child process validates the typed result without vendor SDK");
  var hung=plan with{Serial="idle-hang"};Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial=hung.Serial},IdlePlan=hung},1)).State=="unknown","external deadline kills a hung write and never declares safe retry");
  Console.WriteLine($"PASS {count} idle contract checks; synthetic sessions and real process isolation, not hardware acceptance");
 }
 internal sealed class Session:IZkIdleSettingSession
 {
  public string? ProductValue="product-A",FirmwareValue="firmware-1";public int? Minutes=5;public int Writes,Calls;
  public bool ThrowPreflight,ReturnFalse,ThrowWrite,WrongReadback,DriftFirmware,ThrowReadback;
  public string? Product(){Calls++;if(ThrowPreflight)throw new IOException();return ProductValue;}
  public string? Firmware(){Calls++;return DriftFirmware&&Writes>0?"changed":FirmwareValue;}
  public int? ReadIdleMinutes(){Calls++;if(ThrowReadback&&Writes>0)throw new IOException();return WrongReadback&&Writes>0?7:Minutes;}
  public bool SetIdleMinutes(int minutes){Calls++;Writes++;Minutes=minutes;if(ThrowWrite)throw new IOException();return !ReturnFalse;}
 }
}
