using System.Globalization;
using System.Runtime.InteropServices;
using IO.DeviceContracts;

internal static class UserReadContractTests
{
    public static async Task RunAsync()
    {
        var count=0;
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);Console.WriteLine("PASS user-contract "+name);count++;}
        var pin=new ZkProbeInput(Guid.NewGuid(),"127.0.0.1",4370,1,"fixture",0);
        var input=new ZkUserReadInput(pin,true,true);
        (List<ZkUserFrame> Frames,Session Session) Execute(ZkUserReadInput request,Session? session=null)
        {var frames=new List<ZkUserFrame>();session??=new();ZkUserReadExecutor.Execute(request,session,frames.Add);return(frames,session);}
        var ssr=Execute(input);
        Check(ssr.Frames[0].ReadFormat==ZkUserReadFormats.Ssr&&ssr.Frames[1].User?.PersonIdentifier=="0001"&&ssr.Frames[^1].State=="succeeded"&&ssr.Session.Formats.All(x=>x==ZkUserReadFormats.Ssr),"default SSR keeps exact leading zeros and never tries the legacy method");
        var legacy=Execute(input with{ReadFormat=ZkUserReadFormats.Legacy});
        Check(legacy.Frames[0].ReadFormat==ZkUserReadFormats.Legacy&&legacy.Frames[1].User?.PersonIdentifier=="1"&&legacy.Frames[1].User?.Card=="000123"&&legacy.Frames[^1].State=="succeeded"&&legacy.Session.Formats.All(x=>x==ZkUserReadFormats.Legacy),"explicit legacy uses canonical Int32 identity without padding or normalizing card text");
        var culture=CultureInfo.CurrentCulture;
        try{CultureInfo.CurrentCulture=new("fa-IR");bool rejected=false;try{ZkUserReadFormats.LegacyIdentity(-1);}catch(ArgumentException){rejected=true;}Check(ZkUserReadFormats.LegacyIdentity(int.MaxValue)=="2147483647"&&ZkUserReadFormats.LegacyIdentity(0)=="0"&&rejected&&!ZkUserReadFormats.IsCanonicalLegacyIdentity("01")&&!ZkUserReadFormats.IsCanonicalLegacyIdentity("2147483648")&&!ZkUserReadFormats.IsCanonicalLegacyIdentity("+1"),"legacy integer identity is culture invariant and rejects negative values");}finally{CultureInfo.CurrentCulture=culture;}
        var invalid=Execute(input with{ReadFormat="auto"});
        Check(invalid.Frames.Single().State=="unsupported"&&invalid.Session.Calls==0,"unknown format fails before any SDK call and no automatic format detection exists");
        var partial=Execute(input,new(){AfterCount=2});
        Check(partial.Frames[^1].State=="incomplete"&&partial.Frames[^1].Count==1,"changed count retains emitted rows but is not a complete snapshot");
        var fail=Execute(input with{ReadFormat=ZkUserReadFormats.Legacy},new(){ThrowNext=true});
        Check(fail.Frames[^1].State=="failed"&&fail.Session.Formats.SequenceEqual(new[]{ZkUserReadFormats.Legacy}),"legacy API failure never falls back to SSR or fabricates an empty successful snapshot");
        Check(Execute(input,new(){InitialSerial="different"}).Frames.Single().State=="identity-mismatch"&&Execute(input,new(){FinalSerial="different"}).Frames[^1].State=="failed","serial is pinned before emission and checked again after enumeration");
        Check(Execute(input,new(){AfterCount=null}).Frames[^1].State=="incomplete"&&Execute(input,new(){BeginAllowed=false}).Frames.Single().State=="failed","unavailable final counter and failed enumeration start cannot report success");
        var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(UserReadContractTests).Assembly.Location;
        var rows=new List<ZkUser>();
        var result=await NativeUserReadSupervisor.RunAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="users-legacy"},ReadFormat=ZkUserReadFormats.Legacy},5,(batch,ct)=>{rows.AddRange(batch);return Task.CompletedTask;});
        Check(result.State=="succeeded"&&rows.Count==1,"private child IPC accepts a specifically declared legacy stream");
        rows.Clear();result=await NativeUserReadSupervisor.RunAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="users-missing-format"},ReadFormat=ZkUserReadFormats.Legacy},5,(batch,ct)=>{rows.AddRange(batch);return Task.CompletedTask;});
        Check(result.State=="bridge-failed"&&rows.Count==0,"legacy request rejects an old untagged SSR child before exposing any user");
        Console.WriteLine($"PASS {count} explicit user-read contract checks; synthetic sessions and child IPC, not COM or device acceptance");
    }
    private sealed class Session:IZkUserReadSession
    {
        public List<string> Formats=new();public int Calls;private int serials,counts,rows;
        public int? AfterCount=1;public bool ThrowNext;public bool BeginAllowed=true;public string InitialSerial="fixture",FinalSerial="fixture";
        public string Serial(){Calls++;return serials++==0?InitialSerial:FinalSerial;}
        public int? UserCount(){Calls++;return counts++==0?1:AfterCount;}
        public bool Begin(){Calls++;return BeginAllowed;}
        public ZkUser? Next(string format){Calls++;Formats.Add(format);if(ThrowNext)throw new InvalidOperationException();return rows++==0?new(format==ZkUserReadFormats.Ssr?"0001":ZkUserReadFormats.LegacyIdentity(1),"Synthetic","000123",0,true):null;}
    }
}
