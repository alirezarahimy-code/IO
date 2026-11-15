using System.Text.Json;
using IO.DeviceContracts;
internal static class ProtectedEraseTests
{
 internal static ProtectedEraseApproval Approval(string family,string format=ZkUserReadFormats.Ssr)=>new(family,UserEraseTests.Approval(format,family=="fingerprints"?UserEraseApproval.Fingerprints:UserEraseApproval.User),family=="administrators"?"SYNTHETIC-PRIVILEGE-DEMOTION-ONLY":null,family=="administrators");
 internal static ProtectedEraseInput Input(string family,string format=ZkUserReadFormats.Ssr)
 {
  var a=Approval(family,format);var users=UserEraseSdkDouble.Initial(a.Inventory).Select(e=>e.User).ToArray();return new(new(Guid.NewGuid(),"192.0.2.43",4370,7,a.Inventory.Device.Serial,0),Guid.NewGuid(),a,family=="administrators"?null:users[0],users.Length,UserMetadataCommitment.Create(users));
 }
 internal static ProtectedEraseOutput Run(ProtectedEraseInput i,UserEraseSdkDouble s)=>ProtectedEraseExecutor.Execute(i,new ZkComUserEraseSession(s,7));
 internal static async Task RunAsync()
 {
  int count=0;void Check(bool b,string m){if(!b)throw new InvalidOperationException(m);count++;Console.WriteLine("PASS "+m);}
  foreach(var family in new[]{"users","fingerprints","administrators"})foreach(var format in new[]{ZkUserReadFormats.Ssr,ZkUserReadFormats.Legacy})
  {
   var i=Input(family,format);var s=new UserEraseSdkDouble(i.Approval.Inventory);var output=Run(i,s);
   Check(output.IsValidFor(i)&&output.State==(family=="users"?"user-deleted-verified":family=="fingerprints"?"fingerprints-deleted-verified":"administrators-cleared-verified")&&s.Writes==1&&s.Reads==3,"protected exact family "+family+" "+format+" binds whole archived metadata before single native dispatch and protected readback");
   s=new(i.Approval.Inventory);Check(Run(i with{MetadataDigest=new string('A',64)},s).State=="failed-before-write"&&s.Writes==0,"protected "+family+" stale same-count durable metadata refuses write");
   s=new(i.Approval.Inventory);Check(Run(i with{InspectOnly=true},s).State=="observed"&&s.Writes==0,"protected "+family+" recovery branch is strictly read-only");
  }
  var admin=Input("administrators");var sdk=new UserEraseSdkDouble(admin.Approval.Inventory);
  Check(!(admin.Approval with{AllowClearAdministrators=false}).IsValid()&&!(admin.Approval with{AdministratorEffectReference=""}).IsValid(),"R14 independent administrator effect and approval are mandatory");
  Check(!(admin with{Expected=UserEraseSdkDouble.Initial(admin.Approval.Inventory)[0].User}).IsValid(),"R14 fixed all-administrator demotion cannot accept individual subject or delete-user input");
  sdk.AfterWrite=x=>x.Entries[1]=new(x.Entries[1].User with{Privilege=3},x.Entries[1].Password,x.Entries[1].Fingerprints);Check(Run(admin,sdk).State=="unknown"&&sdk.Writes==1,"R14 remaining privilege despite ACK is unknown");
  sdk=new(admin.Approval.Inventory);sdk.AfterWrite=x=>x.Entries.RemoveAt(0);Check(Run(admin,sdk).State=="unknown","R14 deleting people rather than demoting them is not verified");
  sdk=new(admin.Approval.Inventory);sdk.AfterWrite=x=>x.Entries[1]=UserEraseSdkDouble.With(x.Entries[1],password:"MUTATED");Check(Run(admin,sdk).State=="unknown","R14 any password mutation is unknown, never accepted as administrator clear");
  sdk=new(admin.Approval.Inventory);sdk.AfterWrite=x=>x.Entries[1]=UserEraseSdkDouble.With(x.Entries[1],replaceFinger:true);Check(Run(admin,sdk).State=="unknown","R14 any fingerprint mutation violates whole protected catalogue readback");
  sdk=new(admin.Approval.Inventory);sdk.ReturnFalse=true;Check(Run(admin,sdk).State=="unknown"&&sdk.Writes==1,"R14 false acknowledgement is not replayed");
  sdk=new(admin.Approval.Inventory);sdk.ThrowWrite=true;Check(Run(admin,sdk).State=="unknown"&&sdk.Writes==1,"R14 exception after fixed command is unknown");
  sdk=new(admin.Approval.Inventory);sdk.Entries=sdk.Entries.Select(e=>new UserEraseEntry(e.User with{Privilege=0},e.Password,e.Fingerprints)).ToList();var empty=admin with{MetadataDigest=UserMetadataCommitment.Create(sdk.Entries.Select(e=>e.User))};Check(Run(empty,sdk).State=="already-no-administrators"&&sdk.Writes==0,"R14 already ordinary catalogue is a no-op");
  Check(!new ProtectedEraseOutput(admin.OperationId,"user-deleted-verified").IsValidFor(admin)&&!new ProtectedEraseOutput(admin.OperationId,"observed").IsValidFor(admin),"protected result rejects wrong-family success and inspection masquerading as mutation");
  var host=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(ProtectedEraseTests).Assembly.Location;
  foreach(var family in new[]{"users","fingerprints","administrators"}){var i=Input(family);Check((await NativeProcessSupervisor.RunProtectedEraseAsync(host,assembly,i,5)).IsValidFor(i),"production protected supervisor correlated "+family+" child");}
  foreach(var marker in new[]{"EXTRA","FOREIGN","HANG"})
  {
   var i=admin with{Approval=admin.Approval with{AdministratorEffectReference=marker}};Check((await NativeProcessSupervisor.RunProtectedEraseAsync(host,assembly,i,marker=="HANG"?1:5)).State=="unknown","R14 child "+marker+" cannot forge completion");
  }
  Console.WriteLine("::notice title=Protected erase contracts::"+count+" synthetic R13/R14 checks passed; no SDK/device execution or real authorization.");
 }
 internal static async Task ChildAsync(string text)
 {
  var i=JsonSerializer.Deserialize<ProtectedEraseInput>(text)!;var marker=i.Approval.AdministratorEffectReference;
  if(marker=="HANG"){await Task.Delay(TimeSpan.FromMinutes(1));return;}
  var output=Run(i,new(i.Approval.Inventory));
  if(marker=="EXTRA")Console.WriteLine("{\"OperationId\":\""+i.OperationId+"\",\"State\":\"administrators-cleared-verified\",\"Raw\":[]}");
  else Console.WriteLine(JsonSerializer.Serialize(marker=="FOREIGN"?output with{OperationId=Guid.NewGuid()}:output));
 }
}
