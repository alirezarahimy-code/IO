using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using IO.DeviceContracts;
internal static class AttendanceReadContractTests
{
 public static async Task RunAsync()
 {
  int count=0;void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);Console.WriteLine("PASS attendance-contract "+message);count++;}
  var pin=new ZkProbeInput(Guid.NewGuid(),"127.0.0.1",4370,1,"fixture",0);var input=new ZkAttendanceReadInput(pin,true,true);
  (List<ZkAttendanceFrame> Frames,Session Session) Run(ZkAttendanceReadInput request,Session? session=null){session??=new();var frames=new List<ZkAttendanceFrame>();ZkAttendanceReadExecutor.Execute(request,session,frames.Add);return(frames,session);}
  var ssr=Run(input);var legacy=Run(input with{ReadFormat=ZkAttendanceReadFormats.Legacy});
  Check(ssr.Frames[0].ReadFormat==ZkAttendanceReadFormats.Ssr&&ssr.Frames[1].Record?.PersonIdentifier=="0001"&&ssr.Frames[^1].State=="succeeded"&&ssr.Session.Formats.All(x=>x==ZkAttendanceReadFormats.Ssr),"SSR preserves string identity seconds work code and never calls legacy");
  var raw=legacy.Frames[1].Record!;
  Check(legacy.Frames[0].ReadFormat==ZkAttendanceReadFormats.Legacy&&raw.PersonIdentifier=="1"&&raw.LegacyTerminalNumber==7&&raw.LegacyEnrollmentMachineNumber==8&&raw.Second==0&&raw.WorkCode==0&&legacy.Frames[^1].State=="succeeded","explicit legacy declares minute precision and preserves the two distinct machine outputs");
  Check(JsonSerializer.Deserialize<ZkAttendanceRaw>(raw.CanonicalJson())==raw&&raw.CanonicalJson()!=(raw with{LegacyEnrollmentMachineNumber=9}).CanonicalJson()&&ZkAttendanceReadFormats.ExternalPrefix(ZkAttendanceReadFormats.Ssr)!=ZkAttendanceReadFormats.ExternalPrefix(ZkAttendanceReadFormats.Legacy),"legacy canonical bytes retain machine outputs and cannot share the SSR dedup namespace");
  Check(!ZkAttendanceReadFormats.Matches(raw with{PersonIdentifier="01"},ZkAttendanceReadFormats.Legacy)&&!ZkAttendanceReadFormats.Matches(raw with{Second=1},ZkAttendanceReadFormats.Legacy)&&!ZkAttendanceReadFormats.Matches(raw with{WorkCode=1},ZkAttendanceReadFormats.Legacy)&&!ZkAttendanceReadFormats.Matches(raw,ZkAttendanceReadFormats.Ssr),"legacy rejects padded identities and invented seconds or work codes; SSR refuses legacy metadata");
  var unknown=Run(input with{ReadFormat="auto"});Check(unknown.Frames.Single().State=="unsupported"&&unknown.Session.Calls==0,"unknown contract is rejected before any native call without automatic detection");
  var failed=Run(input with{ReadFormat=ZkAttendanceReadFormats.Legacy},new(){Throw=true});Check(failed.Frames[^1].State=="failed"&&failed.Session.Formats.SequenceEqual(new[]{ZkAttendanceReadFormats.Legacy}),"legacy exception is not retried or silently converted into an SSR call");
  Check(Run(input,new(){FinalCount=2}).Frames[^1].State=="incomplete"&&Run(input,new(){FinalCount=null}).Frames[^1].State=="incomplete"&&Run(input,new(){BeginAllowed=false}).Frames.Single().State=="failed","unstable or unavailable count and failed ReadAllGLogData cannot fabricate completion");
  Check(Run(input,new(){FirstSerial="other"}).Frames.Single().State=="identity-mismatch"&&Run(input,new(){LastSerial="other"}).Frames[^1].State=="failed","serial pin is verified both before and after complete enumeration");
  var invalid=Run(input with{ReadFormat=ZkAttendanceReadFormats.Legacy},new(){Invalid=true});Check(invalid.Frames[^1].State=="succeeded"&&invalid.Frames[1].Record?.Interpret(TimeZoneInfo.Utc).Status=="invalid-time","invalid calendar values remain original tuples for server quarantine rather than being dropped");
  var prior=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=new("fa-IR");Check(ZkAttendanceReadFormats.Matches(raw with{PersonIdentifier="-1"},ZkAttendanceReadFormats.Legacy)&&!ZkAttendanceReadFormats.Matches(raw with{PersonIdentifier="+1"},ZkAttendanceReadFormats.Legacy)&&!ZkAttendanceReadFormats.Matches(raw with{PersonIdentifier="2147483648"},ZkAttendanceReadFormats.Legacy),"signed Int32 outputs are retained culture-invariantly for quarantine without padding plus signs or overflow");}finally{CultureInfo.CurrentCulture=prior;}
  var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(AttendanceReadContractTests).Assembly.Location;var rows=new List<ZkAttendanceRaw>();
  Task Accept(IReadOnlyList<ZkAttendanceRaw> batch,CancellationToken ct){rows.AddRange(batch);return Task.CompletedTask;}
  var result=await NativeAttendanceReadSupervisor.RunAsync(host,assembly,input with{ReadFormat=ZkAttendanceReadFormats.Legacy},5,Accept);
  Check(result.State=="succeeded"&&rows.Single().LegacyEnrollmentMachineNumber==2,"private child IPC accepts only the explicitly tagged format and retains raw legacy metadata");
  rows.Clear();result=await NativeAttendanceReadSupervisor.RunAsync(host,assembly,input with{ReadFormat=ZkAttendanceReadFormats.Legacy,Connection=pin with{ExpectedSerial="logs-missing-format"}},5,Accept);
  Check(result.State=="bridge-failed"&&rows.Count==0,"old untagged child cannot masquerade as an explicitly selected legacy stream");
  await using var a=new VirtualDeviceLab.Device("virtual-lab-legacy-logs-A");await using var b=new VirtualDeviceLab.Device("virtual-lab-legacy-logs-B");
  async Task<(NativeAttendanceReadSupervisor.Result Result,List<ZkAttendanceRaw> Rows)> Read(VirtualDeviceLab.Device d){var received=new List<ZkAttendanceRaw>();var outcome=await NativeAttendanceReadSupervisor.RunAsync(host,assembly,new(d.Pin,true,true,ZkAttendanceReadFormats.Legacy),2,(batch,ct)=>{received.AddRange(batch);return Task.CompletedTask;});return(outcome,received);}
  var first=await Read(a);Check(first.Result.State=="succeeded"&&first.Rows.Count==103&&first.Rows.All(x=>ZkAttendanceReadFormats.Matches(x,ZkAttendanceReadFormats.Legacy)),"stateful TCP fixture streams legacy minute tuples through the real bounded child supervisor");
  a.Fault="truncate";var both=await Task.WhenAll(Read(a),Read(b));Check(both[0].Result.State=="bridge-failed"&&both[0].Rows.Count==50&&both[1].Result.State=="succeeded"&&both[1].Rows.Count==103,"one interrupted legacy stream retains its partial batch while another device completes independently");
  a.Fault="none";var again=await Read(a);Check(again.Result.State=="succeeded"&&again.Rows.SequenceEqual(first.Rows),"explicit reread after outage preserves exact tuples and multiplicity rather than inventing a resume cursor");
  a.Fault="hang";Check((await Read(a)).Result.State=="timed-out","hung legacy endpoint is terminated by the external deadline without automatic fallback or replay");
  Console.WriteLine($"PASS {count} attendance contract and stateful TCP checks; no COM or hardware execution");
 }
 private sealed class Session:IZkAttendanceReadSession
 {
  public int Calls;private int serials,counts,rows;public List<string> Formats=new();public int? FinalCount=1;public bool Throw,Invalid;public bool BeginAllowed=true;public string FirstSerial="fixture",LastSerial="fixture";
  public string Serial(){Calls++;return serials++==0?FirstSerial:LastSerial;}
  public int? RecordCount(){Calls++;return counts++==0?1:FinalCount;}
  public bool Begin(){Calls++;return BeginAllowed;}
  public ZkAttendanceRaw? Next(string format){Calls++;Formats.Add(format);if(Throw)throw new InvalidOperationException();if(rows++>0)return null;return format==ZkAttendanceReadFormats.Ssr?new("0001",1,4,2026,11,8,8,0,23,17):new("1",1,4,2026,Invalid?99:11,8,8,0,0,0,7,8);}
 }
}
