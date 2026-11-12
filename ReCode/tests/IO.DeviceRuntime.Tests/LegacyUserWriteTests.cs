using System.Runtime.InteropServices;
using System.Text.Json;
using IO.DeviceContracts;
internal static class LegacyUserWriteTests
{
 public static async Task RunAsync()
 {
  int count=0;void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);Console.WriteLine("PASS legacy-write "+name);count++;}
  var pin=new ZkProbeInput(Guid.NewGuid(),"127.0.0.1",4370,1,"legacy-write-fixture",0);
  var original=new ZkUser("1","Old","000123",0,true);var desired=original with{Name="New",Card="000456"};var input=new ZkUserWriteInput(pin,true,true,original,desired,ZkUserReadFormats.Legacy);
  Check(input.IsValid()&&(input with{Expected=null,Desired=desired with{PersonIdentifier=int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}}).IsValid(),"legacy writes accept only explicitly selected canonical nonnegative Int32 identities");
  Check(!(input with{Desired=desired with{PersonIdentifier="01"}}).IsValid()&&!(input with{Desired=desired with{PersonIdentifier="2147483648"}}).IsValid()&&!(input with{Desired=desired with{PersonIdentifier="-1"}}).IsValid()&&!(input with{WriteFormat="auto"}).IsValid(),"padding overflow negatives and automatic format selection fail before dispatch");
  using(var wire=JsonDocument.Parse(ZkUserWriteWire.Serialize(input)))
   Check(wire.RootElement.GetProperty("WriteLegacyUser").GetBoolean()&&!wire.RootElement.TryGetProperty("WriteUser",out _)&&!wire.RootElement.TryGetProperty("Operation",out _),"legacy request uses a distinct discriminator and cannot enter an old bridge SSR WriteUser branch");
  Check(JsonSerializer.Deserialize<ZkLegacyUserWriteInput>(ZkUserWriteWire.Serialize(input))!.AsRequest()==input&&JsonDocument.Parse(ZkUserWriteWire.Serialize(input with{WriteFormat=ZkUserReadFormats.Ssr})).RootElement.GetProperty("WriteUser").GetBoolean(),"private serialization keeps legacy and SSR routing distinct without changing default SSR semantics");
  var session=new Session(new(original,"private-password"));var result=ZkUserWriteExecutor.Execute(input,session);
  Check(result.State=="succeeded"&&session.Current?.User==desired&&session.Current.Password=="private-password"&&session.Writes==1,"legacy executor preserves existing password only in native-session memory and verifies exact metadata read-back");
  session=new(null);result=ZkUserWriteExecutor.Execute(input with{Expected=null},session);
  Check(result.State=="succeeded"&&session.Current?.Password==""&&session.Writes==1,"explicit absent legacy-user creation invents no password");
  session=new(new(original with{Privilege=3},"private-password"));
  Check(ZkUserWriteExecutor.Execute(input with{Expected=original with{Privilege=3}},session).State=="unsupported"&&session.Writes==0&&!(input with{Desired=desired with{Name=new string('x',21)}}).IsValid(),"admin modification and silent name truncation remain unsupported");
  session=new(new(original with{Name="External"},"private-password"));
  Check(ZkUserWriteExecutor.Execute(input,session).State=="conflict"&&session.Writes==0,"legacy data changed since preview is never silently overwritten");
  session=new(new(original,"private-password")){Fail=true};
  Check(ZkUserWriteExecutor.Execute(input,session).State=="unknown"&&session.Writes==1,"even a false legacy write result is unknown without automatic replay");
  session=new(new(original,"private-password")){WrongReadback=true};
  Check(ZkUserWriteExecutor.Execute(input,session).State=="unknown"&&!JsonSerializer.Serialize(session.Current).Contains("private-password",StringComparison.Ordinal),"normalized card or different legacy read-back cannot produce success or expose the password in JSON");
  var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(LegacyUserWriteTests).Assembly.Location;
  Check((await NativeProcessSupervisor.RunUserWriteAsync(host,assembly,input,5)).State=="succeeded","actual child-process boundary carries the distinct legacy request");
  Check((await NativeProcessSupervisor.RunUserWriteAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="legacy-write-hang"}},1)).State=="unknown","external legacy write deadline kills the child and preserves uncertainty");
  Console.WriteLine($"PASS {count} legacy user-write contract checks; synthetic native sessions, not COM/model acceptance");
 }
 private sealed class Session(ZkUserWriteExisting? initial):IZkUserWriteSession
 {
  public ZkUserWriteExisting? Current=initial;public int Writes;public bool Fail,WrongReadback;
  public ZkUserWriteExisting? ReadExact(string id)=>Writes>0&&WrongReadback?Current! with{User=Current!.User with{Card="456"}}:Current;
  public bool SetCard(string card)=>true;
  public bool Write(ZkUser user,string password){Writes++;if(Fail)return false;Current=new(user,password);return true;}
 }
}
