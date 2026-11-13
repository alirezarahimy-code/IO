using System.Runtime.InteropServices;
using System.Text.Json;
using IO.DeviceContracts;
internal static class FunctionKeyReadTests
{
 public static async Task RunAsync()
 {
  int count=0;void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);Console.WriteLine("PASS function-key-read "+name);count++;}
  var approval=new ZkFunctionKeyReadApproval("function-product","function-fw",[0,3],"synthetic-key-read");
  var pin=new ZkProbeInput(Guid.NewGuid(),"10.20.30.40",4370,1,"function-fixture",0);
  var input=new ZkOperationInput(pin,"settings.read",null,false,true,FunctionKeyReadApproval:approval);
  var snapshot=new ZkAdvancedSettings(ZkAdvancedSettings.Version,pin.ExpectedSerial,1,2,3,4,5,6,approval.Product,approval.Firmware);
  Check(input.IsValid()&&new ZkOperationInput(pin,"settings.read",null,false,true).IsValid(),"installed read is opt-in and old snapshots remain valid without function-key observations");
  Check(!(input with{AllowAdvancedSettingsRead=false}).IsValid()&&!(input with{Operation="information.read"}).IsValid()&&!(input with{Operation="device.restart",AllowRestart=true}).IsValid(),"approval cannot escape advanced settings read into information or mutation dispatch");
  Check(new[]{approval with{Indices=[]},approval with{Indices=[0,0]},approval with{Indices=[-1]},approval with{Indices=null!},approval with{Indices=Enumerable.Range(0,9).ToArray()},approval with{Product=" product"},approval with{Firmware="\uD800"},approval with{EvidenceReference=""}}.All(x=>!x.IsValid()),"approval requires exact descriptors reference unique explicit indices and bounded per-snapshot work");
  var session=new Session();var observed=ZkFunctionKeyReader.Add(snapshot,approval,session)!;var keys=observed.FunctionKeys!;
  Check(session.Calls.SequenceEqual(new[]{0,3})&&keys.Matches(approval)&&observed.SleepTime==4&&observed.IsValid(),"only approved indices are read once in approved order without altering existing six fields");
  Check(keys.Items[0] is {Reported:true,Function:0,StateCode:int.MinValue,AutoChange:2,StateName:"<img src=x>",AutoChangeTime:"raw;not-parsed"}&&keys.Items[1] is {Reported:false,StateCode:null,AutoChange:null},"zero signed values labels and opaque times are retained while SDK false has no invented defaults");
  session=new();Check(ZkFunctionKeyReader.Add(snapshot with{Firmware="different"},approval,session)==null&&ZkFunctionKeyReader.Add(snapshot with{Product=null},approval,session)==null&&session.Calls.Count==0,"live model mismatch or missing descriptor calls no key API and cannot certify compatibility");
  session=new(){ThrowFirst=true};observed=ZkFunctionKeyReader.Add(snapshot,approval,session)!;
  Check(observed.FunctionKeys!.Items.All(x=>!x.Reported)&&session.Calls.Count==2,"one native exception is unavailable and does not suppress other independent reads");
  foreach(var bad in new[]{new ZkFunctionKeyValue(1,true,0,4,"name",0,""),new(0,true,0,4,"\uD800",0,""),new(0,true,0,4,"bad\nname",0,""),new(0,true,0,4,new string('x',129),0,""),new(0,false,StateCode:0)})
  {
   session=new(){Override=bad};observed=ZkFunctionKeyReader.Add(snapshot,approval,session)!;
   Check(observed.FunctionKeys!.Items[0] is {Index:0,Reported:false,StateCode:null},"wrong index or malformed native output stays unavailable without truncation "+count);
  }
  observed=ZkFunctionKeyReader.Add(snapshot,approval,new Session())!;
  var output=new ZkOperationOutput("succeeded",Settings:observed);
  Check(output.IsValidFor(input)&&!output.IsValidFor(input with{FunctionKeyReadApproval=null})&&!(output with{State="failed"}).IsValidFor(input),"typed output cannot inject unsolicited observations or mix failure with successful evidence");
  Check(!output.IsValidFor(input with{FunctionKeyReadApproval=approval with{Indices=[3,0]}})&&!output.IsValidFor(input with{FunctionKeyReadApproval=approval with{EvidenceReference="other"}})&&!(output with{Settings=observed with{Firmware="other"}}).IsValidFor(input),"child evidence binds exact approval reference model and ordered indices");
  Check(!new ZkOperationOutput("succeeded",Settings:snapshot).IsValidFor(input)&&!(observed with{FunctionKeys=keys with{Items=[keys.Items[0],keys.Items[0]]}}).IsValid(),"old bridge missing requested keys or duplicate indices cannot fabricate successful approved reads");
  bool missing=false;try{JsonSerializer.Deserialize<ZkFunctionKeyValue>("{\"Index\":0}");}catch(JsonException){missing=true;}
  Check(missing&&new ZkFunctionKeyValue(0,true,0,0,"",0,"").IsValid(),"reported flag is required and genuinely empty raw SDK strings remain valid");
  var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(FunctionKeyReadTests).Assembly.Location;
  var child=await NativeProcessSupervisor.RunOperationAsync(host,assembly,input,5);
  Check(child.State=="succeeded"&&child.Settings?.FunctionKeys?.Items[1].Reported==false,"real isolated child preserves the typed per-index read result without vendor SDK execution");
  var largeApproval=approval with{Indices=Enumerable.Range(0,8).ToArray()};
  child=await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="function-large"},FunctionKeyReadApproval=largeApproval},5);
  Check(child.State=="succeeded"&&child.Settings?.FunctionKeys?.Items.Length==8&&JsonSerializer.Serialize(child).Length>4096,"bounded larger settings-only response carries eight raw Unicode observations without the old four-kilobyte truncation");
  child=await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="settings-wide"},FunctionKeyReadApproval=null},5);
  Check(child.State=="succeeded"&&child.Settings?.ReportedGateway?.Length==100&&JsonSerializer.Serialize(child).Length>4096,"fixed raw network descriptors also use bounded settings stdout without requiring or inventing key approval");
  child=await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="function-missing"}},5);
  Check(child.State=="bridge-failed","missing requested key evidence from an old or malformed child is rejected");
  child=await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="function-overflow"}},5);
  Check(child.State=="bridge-failed","oversized settings child output still fails closed at its bounded pipe budget");
  child=await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="settings-hang"}},1);
  Check(child.State=="timed-out","hung read child is killed at the external deadline and is never a completed snapshot");
  Console.WriteLine($"PASS {count} function key read checks; synthetic sessions and real child, not SDK or hardware acceptance");
 }
 internal sealed class Session:IZkFunctionKeyReadSession
 {
  public List<int> Calls=[];public bool ThrowFirst;public ZkFunctionKeyValue? Override;
  public ZkFunctionKeyValue? Read(int index){Calls.Add(index);if(ThrowFirst&&index==0)throw new IOException();return Override??(index==0?new(index,true,0,int.MinValue,"<img src=x>",2,"raw;not-parsed"):null);}
 }
}
