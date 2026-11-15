using System.Text.Json;
using IO.DeviceContracts;
internal static class TransportSupportTests
{
 internal static void Run()
 {
  int count=0;void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);count++;Console.WriteLine("PASS "+message);}
  var entries=DeviceTransportSupport.Entries;
  Check(entries.Count==4&&entries.Select(e=>e.Kind).Distinct().Count()==4,"R16 closed support inventory has four unambiguous dispositions");
  foreach(var kind in new[]{"sdk-usb-direct","sdk-serial-direct"}){var e=entries.Single(e=>e.Kind==kind);Check(e.DirectConnection&&e.SoftwareRouteAvailable&&e.RegistryProfile is not null&&e.Status=="implemented-bounded-local-exchange","R16 direct "+kind+" has an explicit bounded direct route, not silently mapped to TCP or file");}
  Check(entries.Single(e=>e.Kind=="usb-file")is{DirectConnection:false,SoftwareRouteAvailable:true},"R16 file route cannot claim USB cable support");
  Check(entries.Single(e=>e.Kind=="sdk-network")is{SoftwareRouteAvailable:true,RegistryProfile:"zk-standalone"},"R16 existing approved network route retained");
  foreach(var host in new[]{"COM1","com17"," COM999 ","USB","serial","usb:machine1","serial:COM1","\\\\.\\COM1","\\\\?\\USB#VID_TEST","/dev/ttyS0","/dev/ttyUSB0"})Check(DeviceTransportSupport.IsDirectLocalAddress(host),"R16 reserved local endpoint cannot masquerade as network hostname: "+host);
  foreach(var host in new[]{"192.0.2.45","2001:db8::45","terminal.example.test","company-device","usb-gateway.example.test"})Check(!DeviceTransportSupport.IsDirectLocalAddress(host),"R16 classifier does not revoke ordinary network endpoint syntax: "+host);
  var input=new ZkProbeInput(Guid.NewGuid(),"192.0.2.45",4370,7,"SYNTHETIC",0);
  foreach(var host in new[]{"COM1","USB","/dev/ttyUSB0","\\\\.\\COM1"})Check(!(input with{Address=host}).IsValid(),"R16 production bridge contract cannot open local device through network input");
  bool rejected=false;try{_=JsonSerializer.Deserialize<ZkProbeInput>(JsonSerializer.Serialize(new{input.DeviceId,input.Address,input.Port,input.MachineNumber,input.ExpectedSerial,input.CommKey,Transport="usb"}));}catch(JsonException){rejected=true;}
  Check(rejected,"R16 injected transport selector is rejected, never silently interpreted as network or direct USB");
  Console.WriteLine("::notice title=Transport support disposition::"+count+" checks passed; product network/file versus explicitly bound direct USB/serial boundary only, no physical transport or driver executed.");
 }
}
