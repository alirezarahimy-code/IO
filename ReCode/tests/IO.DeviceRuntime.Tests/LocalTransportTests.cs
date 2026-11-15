using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using IO.DeviceContracts;
using IO.ZkBridgeTransport;

internal static class LocalTransportTests
{
 internal static ZkProbeInput Input(string kind="usb",int port=0,int baud=0)=>new(Guid.NewGuid(),Environment.MachineName.ToUpperInvariant(),port,7,"SYNTHETIC-LOCAL",0,new(kind,Environment.MachineName.ToUpperInvariant(),port,baud,"SYNTHETIC-DRIVER","SYNTHETIC-SINGLE-DEVICE-SELECTION",true));
 internal static void Run()
 {
  int count=0;void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);count++;Console.WriteLine("PASS "+message);}
  bool Reject(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
  var usb=Input();var serial=Input("serial",17,57600);var sdk=new LocalSdkDouble();
  Check(usb.IsValid()&&serial.IsValid(),"direct transport uses explicit station/physical binding, no fake IP");
  Check(!ZkSdkConnection.Connect(sdk,usb)&&sdk.Calls.Count==0,"production SDK connector refuses local call without physical lease");
  using(var lease=ZkTransportLease.Acquire([usb]))
  {
   Check(ZkSdkConnection.Connect(sdk,usb)&&sdk.Calls.SequenceEqual(["USB:7"]),"production mapper invokes Connect_USB(machine), never Connect_Net");
   Check(Reject(()=>{using var other=ZkTransportLease.Acquire([usb]);}),"same-thread mutex reentrancy cannot admit a second physical owner");
   var start=new ProcessStartInfo(Environment.ProcessPath!){RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
   start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add("--local-lease-attempt");
   using var process=Process.Start(start)!;process.StandardInput.Write(JsonSerializer.Serialize(usb));process.StandardInput.Close();
   if(!process.WaitForExit(10000)){process.Kill(true);throw new InvalidOperationException("Lease child deadline.");}
   Check(process.ExitCode==2,"independent Windows process cannot steal global USB selector ownership");
  }
  Check(!ZkSdkConnection.Connect(sdk,usb)&&sdk.Calls.Count==1,"disposed lease cannot authorize later SDK use");
  sdk=new();using(var lease=ZkTransportLease.Acquire([serial]))Check(ZkSdkConnection.Connect(sdk,serial)&&sdk.Calls.SequenceEqual(["COM:17:7:57600"]),"production mapper passes exact configured port machine baud to Connect_Com");
  using(var lease=ZkTransportLease.Acquire([usb,serial]))Check(ZkSdkConnection.Connect(sdk,usb),"independent USB and COM resources may coexist without transport fallback");
  Check(Reject(()=>{using var lease=ZkTransportLease.Acquire([usb,usb with{DeviceId=Guid.NewGuid()}]);}),"two logical devices cannot share ambiguous USB selector in one child");
  var wrong=usb with{Address="OTHER-STATION",LocalTransport=usb.LocalTransport! with{StationName="OTHER-STATION"}};
  Check(Reject(()=>{using var lease=ZkTransportLease.Acquire([wrong]);}),"wrong Windows station fails before SDK activation");
  foreach(var local in new[]{serial.LocalTransport! with{SerialPort=0},serial.LocalTransport! with{SerialPort=257},serial.LocalTransport! with{BaudRate=0},serial.LocalTransport! with{BaudRate=12345},serial.LocalTransport! with{ExclusiveAccessApproved=false},serial.LocalTransport! with{DriverReference=""},serial.LocalTransport! with{StationName="COM1/secret"},serial.LocalTransport! with{Kind="unknown"}})
   Check(!local.IsValid(),"closed local installation approval rejects invalid kind port baud station driver or authority");
  Check(!(usb with{Port=4370}).IsValid()&&!(serial with{Address="192.0.2.45"}).IsValid(),"cross-transport endpoint injection rejected");
  var network=new ZkProbeInput(Guid.NewGuid(),"192.0.2.45",4370,7,"SYNTHETIC",0);sdk=new();
  Check(ZkSdkConnection.Connect(sdk,network)&&sdk.Calls.SequenceEqual(["NET:192.0.2.45:4370"]),"network-only input retains exact legacy routing and does not acquire local resources");
  foreach(var operation in new[]{"network.ip.set","network.inspect","settings.profile.set","settings.raw.set"})Check(!new ZkOperationInput(usb,operation,null,false).IsValid(),"direct connection cannot enter unapproved network or specialized write contract");
  using(var document=JsonDocument.Parse(JsonSerializer.Serialize(new ZkUserReadInput(usb,true,true))))
  using(var lease=ZkTransportLease.AcquireEnvelope(document.RootElement))Check(ZkSdkConnection.Connect(new LocalSdkDouble(),usb),"production stdin envelope acquires nested Connection physical lease");
  using(var document=JsonDocument.Parse(JsonSerializer.Serialize(usb)))
  using(var lease=ZkTransportLease.AcquireEnvelope(document.RootElement))Check(ZkSdkConnection.Connect(new LocalSdkDouble(),usb),"production stdin envelope acquires root probe physical lease");
  var framed=new List<ZkUserFrame>();ZkUserReadExecutor.Execute(new(usb,true,true),new UserSession(usb.ExpectedSerial),framed.Add);
  Check(framed.Last().State=="succeeded"&&framed.Count(f=>f.Type=="user")==2&&framed[1].User!.PersonIdentifier=="001","local SSR uses unchanged complete serial-pinned user executor and preserves leading zero");
  var logs=new List<ZkAttendanceFrame>();ZkAttendanceReadExecutor.Execute(new(serial,true,true),new LogSession(serial.ExpectedSerial),logs.Add);
  Check(logs.Last().State=="succeeded"&&logs.Count(f=>f.Type=="record")==2,"local serial uses unchanged native attendance executor preserving duplicate tuples");
  var erase=AllDataEraseTests.Input();
  Check(!(erase with{Connection=usb with{ExpectedSerial=erase.Connection.ExpectedSerial}}).IsValid(),"local all-data deletion is explicitly rejected until its transport-specific safety contract is accepted");
  Console.WriteLine($"::notice title=Local SDK transport contracts::{count} synthetic contract and Windows physical-resource mutex checks passed; production connector uses fake SDK only. No installed driver device or destructive execution.");
 }
 internal static void Attempt(string text)
 {
  try{using var lease=ZkTransportLease.Acquire([JsonSerializer.Deserialize<ZkProbeInput>(text)!]);Environment.ExitCode=0;}
  catch{Environment.ExitCode=2;}
 }
 internal static bool IsLocal(JsonElement root)=>root.TryGetProperty("LocalTransport",out var local)&&local.ValueKind==JsonValueKind.Object||root.TryGetProperty("Connection",out var c)&&c.TryGetProperty("LocalTransport",out var nested)&&nested.ValueKind==JsonValueKind.Object;
 internal static void Child(string text)
 {
  using var document=JsonDocument.Parse(text);var root=document.RootElement;
  using var lease=ZkTransportLease.AcquireEnvelope(root);
  var input=root.TryGetProperty("Connection",out var connection)?connection.Deserialize<ZkProbeInput>()!:root.Deserialize<ZkProbeInput>()!;
  if(!ZkSdkConnection.Connect(new LocalSdkDouble(),input))throw new InvalidOperationException("Synthetic local router refused binding.");
  if(root.TryGetProperty("ReadUsers",out _))ZkUserReadExecutor.Execute(root.Deserialize<ZkUserReadInput>()!,new UserSession(input.ExpectedSerial),f=>Console.WriteLine(JsonSerializer.Serialize(f)));
  else if(root.TryGetProperty("ReadAttendance",out _))ZkAttendanceReadExecutor.Execute(root.Deserialize<ZkAttendanceReadInput>()!,new LogSession(input.ExpectedSerial),f=>Console.WriteLine(JsonSerializer.Serialize(f)));
  else if(root.TryGetProperty("Operation",out _))Console.WriteLine(JsonSerializer.Serialize(new ZkOperationOutput("succeeded",new(input.ExpectedSerial,"SYNTHETIC-PRODUCT","SYNTHETIC-FIRMWARE",null,null,2,10,2,10,0,0,10,0,0,false))));
  else Console.WriteLine("sdk-serial-matched");
 }
 private sealed class UserSession(string serial):IZkUserReadSession
 {
  private int index;public string Serial()=>serial;public int? UserCount()=>2;public bool Begin(){index=0;return true;}
  public ZkUser? Next(string format)=>index++ switch{0=>new(format==ZkUserReadFormats.Ssr?"001":"1","SYNTHETIC-ONE","1",0,true),1=>new("2","SYNTHETIC-TWO","2",0,true),_=>null};
 }
 private sealed class LogSession(string serial):IZkAttendanceReadSession
 {
  private int index;public string Serial()=>serial;public int? RecordCount()=>2;public bool Begin(){index=0;return true;}
  public ZkAttendanceRaw? Next(string format)=>index++<2?new(format==ZkAttendanceReadFormats.Ssr?"001":"1",1,0,2026,11,8,7,0,0,0,format==ZkAttendanceReadFormats.Legacy?7:null,format==ZkAttendanceReadFormats.Legacy?9:null):null;
 }
}
public sealed class LocalSdkDouble
{
 public List<string> Calls{get;}=[];
 public bool Connect_Net(string address,int port){Calls.Add($"NET:{address}:{port}");return true;}
 public bool Connect_USB(int machine){Calls.Add($"USB:{machine}");return true;}
 public bool Connect_Com(int port,int machine,int baud){Calls.Add($"COM:{port}:{machine}:{baud}");return true;}
}
