using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using IO.DeviceContracts;

if (args is not ["--verify"])
{
    // This executable is a test-only child fixture, never deployed as the SDK bridge.
    var fixture=JsonSerializer.Deserialize<ZkProbeInput>(await Console.In.ReadToEndAsync())!;
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
Console.WriteLine($"PASS {count} portable native-boundary checks; no real device or SDK acceptance implied");
