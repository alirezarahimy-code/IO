using System.Runtime.InteropServices;
using IO.DeviceContracts;

internal static class RestartTests
{
    public static async Task RunAsync()
    {
        var count=0;
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);Console.WriteLine("PASS restart "+name);count++;}
        var pin=new ZkProbeInput(Guid.NewGuid(),"127.0.0.1",4370,1,"restart-ok",0);
        var input=new ZkOperationInput(pin,"device.restart",null,false,AllowRestart:true);
        Check(input.IsValid()&&!(input with{AllowRestart=false}).IsValid()&&!(input with{ClockLocal="2026-11-08 10:00:00"}).IsValid(),"explicit restart approval and no clock payload required");
        Check(!(input with{Operation="device.reset"}).IsValid()&&!(input with{Operation="ClearGLog"}).IsValid(),"factory reset and data deletion cannot enter the closed restart contract");
        var session=new Session();
        Check(ZkRestartExecutor.Execute(false,session).State=="unsupported"&&session.Calls==0,"disabled approval makes zero SDK calls");
        Check(ZkRestartExecutor.Execute(true,session).State=="succeeded"&&session.Calls==1,"SDK acknowledgement recorded after exactly one call");
        session=new Session{Result=false};
        Check(ZkRestartExecutor.Execute(true,session).State=="unknown"&&session.Calls==1,"false SDK return is unknown and never replayed");
        session=new Session{Throw=true};
        Check(ZkRestartExecutor.Execute(true,session).State=="unknown"&&session.Calls==1,"disconnect or exception after invocation remains unknown");
        Check(new ZkOperationOutput("succeeded").IsValidFor(input)&&!new ZkOperationOutput("succeeded").IsValidFor(input with{AllowRestart=false}),"successful response cannot bypass input approval");
        var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));
        var assembly=typeof(RestartTests).Assembly.Location;
        Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input,5)).State=="succeeded","actual child accepts the typed restart message");
        Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="restart-hang"}},1)).State=="unknown","child deadline kills process without falsely reporting no restart");
        Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="extra-field"}},5)).State=="unknown","untrusted extra response fields cannot prove restart result");
        Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{AllowRestart=false},5)).State=="unsupported","supervisor rejects unapproved dispatch");
        Console.WriteLine($"PASS {count} restart checks; synthetic sessions and real process boundary, not vendor SDK or hardware acceptance");
    }
    private sealed class Session:IZkRestartSession
    {
        public int Calls;public bool Result=true;public bool Throw;
        public bool Restart(){Calls++;if(Throw)throw new IOException();return Result;}
    }
}
