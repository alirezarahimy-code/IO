using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using IO.DeviceContracts;

if (args is not ["--verify"])
{
    // This executable is a test-only child fixture, never deployed as the SDK bridge.
    var text=await Console.In.ReadToEndAsync();using var document=JsonDocument.Parse(text);
    if(document.RootElement.TryGetProperty("Connection",out _))
    {
        var operation=JsonSerializer.Deserialize<ZkOperationInput>(text)!;
        var information=new ZkDeviceInformation(operation.Connection.ExpectedSerial,"fixture-product",null,null,"2026-11-08 10:00:00",0,null,10,100,null,null,null,null,null,operation.AllowClockSet);
        if(operation.Connection.ExpectedSerial=="wrong-serial")information=information with{Serial="other-device"};
        if(operation.Connection.ExpectedSerial=="extra-field")Console.WriteLine("{\"State\":\"failed\",\"Secret\":\"must-not-pass\"}");
        else Console.WriteLine(JsonSerializer.Serialize(new ZkOperationOutput("succeeded",operation.Operation=="information.read"?information:null)));
        return;
    }
    var fixture=JsonSerializer.Deserialize<ZkProbeInput>(text)!;
    switch(fixture.ExpectedSerial)
    {
        case "ok": Console.WriteLine("sdk-serial-matched");break;
        case "unknown": Console.WriteLine("invented-success");break;
        case "environment": Console.WriteLine(Environment.GetEnvironmentVariable("ConnectionStrings__IO") is null && Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS") is null ? "sdk-serial-matched" : "invalid-input");break;
        case "big": Console.WriteLine(new string('X',16384));break;
        case "hang":
            await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(),"IO-BridgeFixture-"+fixture.DeviceId+".pid"),Environment.ProcessId.ToString());
            await Task.Delay(TimeSpan.FromMinutes(1));break;
    }
    return;
}
var count=0;
void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);Console.WriteLine("PASS "+name);count++;}
var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));
var fixtureAssembly=System.Reflection.Assembly.GetExecutingAssembly().Location;
var input=new ZkProbeInput(Guid.NewGuid(),"127.0.0.1",4370,1,"ok",123456789);
Check(input.IsValid()&&!input.ToString().Contains("123456789",StringComparison.Ordinal),"private native IPC contract validates pins and redacts communication key from text");
Check(!(input with{Port=0}).IsValid(),"invalid native probe port rejected");
Check(!(input with{ExpectedSerial=""}).IsValid()&&!(input with{Address="device.example"}).IsValid(),"native probe requires explicit serial and literal address rather than guessing identity or resolving DNS");
Check(await NativeProcessSupervisor.RunAsync(host,fixtureAssembly,input,2)=="sdk-serial-matched","native supervisor accepts only a bounded allowlisted child result");
Check(await NativeProcessSupervisor.RunAsync(host,fixtureAssembly,input with{ExpectedSerial="unknown"},2)=="bridge-failed","native supervisor refuses invented child success codes");
Environment.SetEnvironmentVariable("ConnectionStrings__IO","synthetic-private-connection");
try{Check(await NativeProcessSupervisor.RunAsync(host,fixtureAssembly,input with{ExpectedSerial="environment"},2)=="sdk-serial-matched","native child does not inherit database configuration");}finally{Environment.SetEnvironmentVariable("ConnectionStrings__IO",null);}
Check(await NativeProcessSupervisor.RunAsync(host,fixtureAssembly,input with{ExpectedSerial="big"},2)!="sdk-serial-matched","native supervisor caps child output and never stores an arbitrary SDK exception payload");
var pidFile=Path.Combine(Path.GetTempPath(),"IO-BridgeFixture-"+input.DeviceId+".pid");
try
{
    var watch=Stopwatch.StartNew();
    Check(await NativeProcessSupervisor.RunAsync(host,fixtureAssembly,input with{ExpectedSerial="hang"},2)=="timed-out"&&watch.Elapsed<TimeSpan.FromSeconds(8),"native hung process is bounded by an external timeout, not a blocked SDK thread");
    if(!File.Exists(pidFile))throw new InvalidOperationException("Child fixture did not start before deadline.");
    var pid=int.Parse(await File.ReadAllTextAsync(pidFile));bool stopped;
    try{using var child=Process.GetProcessById(pid);stopped=child.WaitForExit(3000);}catch(ArgumentException){stopped=true;}
    Check(stopped,"native timed-out child process is actually terminated");
}
finally{if(File.Exists(pidFile))File.Delete(pidFile);}
using var cancelled=new CancellationTokenSource();cancelled.Cancel();
Check(await NativeProcessSupervisor.RunAsync(host,fixtureAssembly,input,2,cancelled.Token)=="interrupted","cancelled native dispatch never launches a new device request");
Check(await NativeProcessSupervisor.RunAsync("relative-dotnet",fixtureAssembly,input,2)=="bridge-failed","native supervisor refuses relative executable paths");
var file=System.Text.Encoding.UTF8.GetBytes("00012\t2026-11-08 08:30:10\t1\t0\t1\n00012\t2026-11-08 08:30:10\t1\t0\t1\n");
var preview=PersonnelUsbPreview.Parse(file,"gregorian",TimeZoneInfo.Utc,1);
Check(preview.Lines.Count==2&&preview.Lines[0].PersonIdentifier=="00012"&&preview.Lines[0].OccurredAtUtc==new DateTimeOffset(2026,11,8,8,30,10,TimeSpan.Zero),"USB reference preview preserves leading-zero identity and explicit Gregorian UTC interpretation");
Check(preview.Lines[1].Status=="duplicate-in-file-requires-review"&&preview.Lines[1].SourceLine==preview.Lines[0].SourceLine,"USB repeated identical rows are retained for review rather than silently discarded as device-native duplicates");
Check(!JsonSerializer.Serialize(preview).Contains("SourceLine",StringComparison.Ordinal)&&!preview.Lines[0].ToString().Contains("00012",StringComparison.Ordinal),"USB raw row is excluded from default JSON and personal data is redacted from text rendering");
var persian=PersonnelUsbPreview.Parse(System.Text.Encoding.UTF8.GetBytes("12\t1405-08-17 08:30:10\t1\t0\t1"),"persian",TimeZoneInfo.Utc,1);
Check(persian.Lines[0].OccurredAtUtc==new DateTimeOffset(2026,11,8,8,30,10,TimeSpan.Zero),"USB Persian calendar is explicitly selected and converted without legacy year guessing");
Check(PersonnelUsbPreview.Parse(file,"gregorian",TimeZoneInfo.Utc,2).Lines.All(x=>x.Status=="machine-mismatch"),"USB preview refuses to attribute rows to a different declared machine");
var dst=TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
var ambiguity=PersonnelUsbPreview.Parse(System.Text.Encoding.UTF8.GetBytes("12\t2026-11-01 01:30:00\t1\t0\t1\n12\t2026-03-08 02:30:00\t1\t0\t1"),"gregorian",dst,1);
Check(ambiguity.Lines[0].Status=="ambiguous-local-time"&&ambiguity.Lines[1].Status=="invalid-local-time"&&ambiguity.Lines.All(x=>x.OccurredAtUtc is null),"USB DST overlaps and gaps are quarantined instead of guessing a UTC time");
var wrongLayout=PersonnelUsbPreview.Parse(System.Text.Encoding.UTF8.GetBytes("12\t2026-11-08 08:30:10\t1\t0\t1\t0"),"gregorian",TimeZoneInfo.Utc,1);
Check(wrongLayout.Lines[0].Status=="layout-mismatch","USB parser never guesses that another five/six-column vendor layout is the reference format");
bool invalidEncoding=false;try{PersonnelUsbPreview.Parse(new byte[]{255},"gregorian",TimeZoneInfo.Utc,1);}catch(ArgumentException){invalidEncoding=true;}
Check(invalidEncoding,"USB invalid UTF-8 is rejected rather than silently replacing source bytes");
var operationInput=new ZkOperationInput(input with{ExpectedSerial="information-ok"},"information.read",null,false);
var informationResult=await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,operationInput,2);
Check(informationResult.State=="succeeded"&&informationResult.Information?.Records==0&&informationResult.Information.RecordCapacity is null,"S06 typed IPC distinguishes a reported zero from an unsupported capacity");
Check((await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,operationInput with{Connection=input with{ExpectedSerial="wrong-serial"}},2)).State=="bridge-failed","S06 supervisor refuses an information response for a different serial pin");
Check((await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,operationInput with{Connection=input with{ExpectedSerial="extra-field"}},2)).State=="bridge-failed","S06 supervisor rejects unknown output fields instead of persisting arbitrary SDK data");
Check(!(operationInput with{Operation="door.open"}).IsValid()&&!(operationInput with{Operation="clock.set",ClockLocal="2026-11-08 10:00:00"}).IsValid(),"S06 operation allowlist excludes door controls and clock writes without configuration opt-in");
var clockInput=operationInput with{Operation="clock.set",ClockLocal="2026-11-08 10:00:00",AllowClockSet=true};
Check((await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,clockInput,2)).State=="succeeded"&&!(clockInput with{ClockLocal="2026-02-30 10:00:00"}).IsValid(),"S06 clock IPC accepts explicit valid wall-clock seconds and rejects invalid calendar dates");
Check(!new ZkOperationOutput("succeeded",informationResult.Information! with{Users=-1}).IsValidFor(operationInput)&&!clockInput.ToString().Contains("123456789",StringComparison.Ordinal),"S06 invalid negative counters are rejected and operation text never reveals communication key");
Console.WriteLine($"PASS {count} portable native-boundary and reference-file parser checks; no real device or SDK acceptance implied");
