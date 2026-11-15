using System.Text.Json;
using IO.DeviceContracts;
internal static class BiometricTransferTests
{
 public static async Task RunAsync()
 {
  int count=0;void Check(bool b,string message){if(!b)throw new InvalidOperationException(message);count++;Console.WriteLine("PASS "+message);}
  var a=new BiometricTransferApproval(new("SYNTHETIC-SOURCE","PRODUCT","FIRMWARE"),new("SYNTHETIC-TARGET","PRODUCT","FIRMWARE"),"fingerprint",1,"SYNTHETIC-FORMAT","SYNTHETIC-AUTH","SYNTHETIC-RETENTION","SYNTHETIC-EXCLUSIVE",-404,-404,AllowTransfer:true);
  var i=new BiometricTransferInput(new(Guid.NewGuid(),"192.0.2.1",4370,1,a.Source.Serial,0),new(Guid.NewGuid(),"192.0.2.2",4370,1,a.Target.Serial,0),Guid.NewGuid(),"001","0002",a);
  var synthetic=new BiometricMaterial("SYNTHETIC-TEMPLATE-NOT-A-PERSON",17,1);
  Session Source()=>new(a.Source,new("present",synthetic));Session Target()=>new(a.Target,new("absent"));
  string Run(Session s,Session t,BiometricTransferInput? input=null)=>BiometricTransferExecutor.Execute(input??i,s,t).State;
  Check(i.IsValid()&&!a.AllowTransfer.Equals(false),"explicit exact pair and format approval accepts a supported fingerprint intent");
  Check(!(i with{Approval=a with{AllowTransfer=false}}).IsValid(),"biometric capability is default-deny");
  Check(!(i with{Approval=a with{Kind="photo"}}).IsValid(),"photo cannot fall through to template API or arbitrary file transfer");
  Check(!(i with{Target=i.Source}).IsValid()&&!(i with{Approval=a with{Target=a.Source}}).IsValid(),"same device or aliased serial cannot be used as transfer pair");
  Check(!(i with{SourcePerson="../1"}).IsValid()&&i.SourcePerson=="001","exact decimal identity preserves leading zeros without path input or integer normalization");
  foreach(var flag in new[]{-1,2,3})Check(!(a with{FingerprintFlag=flag}).IsValid(),"unsupported fingerprint/duress flags are not guessed");
  Check(!(a with{Kind="face",Slot=1,FingerprintFlag=0}).IsValid()&&(a with{Kind="face",Slot=15,FingerprintFlag=0}).IsValid(),"face has explicit fixed reference slot and distinct flag contract");
  Check(!(a with{TargetAbsentError=0}).IsValid()&&!(a with{SourceAbsentError=int.MinValue}).IsValid(),"generic false or unknown SDK error cannot be approved absence");
  Check(!(a with{RetentionReference=""}).IsValid()&&!(a with{ExclusiveControlReference=""}).IsValid(),"retention and exclusive control contracts are required");
  var t=Target();Check(Run(Source(),t)=="copied"&&t.Writes==1&&t.LastPerson=="0002","empty target is written once and exact read-back verified");
  t=new(a.Target,new("present",synthetic));Check(Run(Source(),t)=="unchanged"&&t.Writes==0,"identical existing material is no-op, not overwrite");
  t=new(a.Target,new("present",new("DIFFERENT-SYNTHETIC",10,1)));Check(Run(Source(),t)=="existing-slot-refused"&&t.Writes==0,"different existing template is never overwritten or deleted");
  t=Target();Check(Run(new(a.Source,new("absent")),t)=="source-empty"&&t.Writes==0,"absent source never creates a write");
  t=Target();Check(Run(new(a.Source,new("unavailable")),t)=="presence-unavailable"&&t.Writes==0,"source read failure cannot fabricate an empty template");
  t=new(a.Target,new("unavailable"));Check(Run(Source(),t)=="presence-unavailable"&&t.Writes==0,"unavailable target presence fails before dispatch");
  t=Target();Check(Run(new(a.Source with{Firmware="OTHER"},new("present",synthetic)),t)=="identity-mismatch"&&t.Writes==0,"firmware pin is checked before reading or writing");
  t=Target();var s=Source();s.User=false;Check(Run(s,t)=="user-unavailable"&&t.Writes==0,"existing enabled ordinary user required on both endpoints");
  t=Target();s=Source();s.ChangedRead=2;Check(Run(s,t)=="source-changed"&&t.Writes==0,"source change between preflight and dispatch is rejected");
  t=Target();t.OccupiedRead=2;Check(Run(Source(),t)=="existing-slot-refused"&&t.Writes==0,"target becoming occupied before write is rejected");
  t=Target();t.WriteResult=false;Check(Run(Source(),t)=="unknown"&&t.Writes==1,"false setter after dispatch remains unknown and is never replayed");
  t=Target();t.ThrowOnWrite=true;Check(Run(Source(),t)=="unknown","write exception cannot claim failed-before-write");
  t=Target();t.CorruptReadback=true;Check(Run(Source(),t)=="unknown","setter success without exact full read-back is unknown");
  Check(!new BiometricMaterial("BAD\nDATA",8,1).IsValid(a)&&!new BiometricMaterial("SYNTHETIC",0,1).IsValid(a),"malformed/oversized template contract fails closed without encoding conversion");
  Check(!JsonSerializer.Serialize(synthetic).Contains("SYNTHETIC",StringComparison.Ordinal)&&!synthetic.ToString().Contains("SYNTHETIC",StringComparison.Ordinal),"accidental template serialization and diagnostics disclose no payload or digest");
  var host=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(BiometricTransferTests).Assembly.Location;
  Check((await NativeProcessSupervisor.RunBiometricTransferAsync(host,assembly,i,5)).State=="copied","production bounded child supervisor accepts only correlated metadata result");
  Check((await NativeProcessSupervisor.RunBiometricTransferAsync(host,assembly,i with{Approval=a with{FormatReference="SYNTHETIC-EXTRA"}},5)).State=="unknown","child trying to return template properties is rejected by strict output schema");
  Check((await NativeProcessSupervisor.RunBiometricTransferAsync(host,assembly,i with{Approval=a with{FormatReference="SYNTHETIC-FOREIGN"}},5)).State=="unknown","foreign operation id cannot manufacture a completed transfer");
  Check((await NativeProcessSupervisor.RunBiometricTransferAsync(host,assembly,i with{Approval=a with{FormatReference="SYNTHETIC-HANG"}},1)).State=="unknown","hung native child is killed at deadline without replay");
  using var cancelled=new CancellationTokenSource();cancelled.Cancel();Check((await NativeProcessSupervisor.RunBiometricTransferAsync(host,assembly,i,5,cancelled.Token)).State=="failed-before-write","pre-cancelled transfer does not dispatch a child");
  Console.WriteLine("::notice title=Biometric transfer contracts::"+count+" synthetic checks passed; no real biometric material, photo/file, SQL or native SDK executed.");
 }
 private sealed class Session(BiometricDeviceIdentity identity,BiometricSlot slot):IBiometricTransferSession
 {
  public int Writes,Reads,ChangedRead,OccupiedRead;public bool User=true,WriteResult=true,ThrowOnWrite,CorruptReadback;public string? LastPerson;
  public BiometricDeviceIdentity ReadIdentity()=>identity;
  public bool OrdinaryUserExists(string person)=>User;
  public BiometricSlot Read(string person,BiometricTransferApproval approval)
  {Reads++;if(ChangedRead==Reads||OccupiedRead==Reads)return new("present",new("CHANGED-SYNTHETIC",16,1));return slot;}
  public bool Write(string person,BiometricTransferApproval approval,BiometricMaterial material)
  {Writes++;LastPerson=person;if(ThrowOnWrite)throw new InvalidOperationException();slot=CorruptReadback?new("present",new("DIFFERENT",8,1)):new("present",material);return WriteResult;}
 }
}
