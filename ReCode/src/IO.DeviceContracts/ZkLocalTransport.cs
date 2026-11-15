using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IO.DeviceContracts;

/// <summary>Installation-only physical binding. Never chosen by a public request or inferred from an IP.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ZkLocalTransport(string Kind,string StationName,int SerialPort,int BaudRate,string DriverReference,string ExclusiveSelectionReference,bool ExclusiveAccessApproved=false)
{
    public bool IsValid()=>ExclusiveAccessApproved&&Station(StationName)&&Reference(DriverReference)&&Reference(ExclusiveSelectionReference)&&
        (Kind=="usb"?SerialPort==0&&BaudRate==0:Kind=="serial"&&SerialPort is >=1 and <=256&&BaudRate is 9600 or 19200 or 38400 or 57600 or 115200);
    public static bool Station(string? value)=>value is not null&&Regex.IsMatch(value,"^[A-Z][A-Z0-9-]{0,62}$",RegexOptions.CultureInvariant);
    private static bool Reference(string? value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=200&&!value.Any(char.IsControl);
    public string Profile=>Kind=="usb"?"zk-usb-direct":"zk-serial-direct";
    internal string Resource=>Kind=="usb"?"USB":$"COM{SerialPort}";
    public override string ToString()=>"Local SDK transport [installation binding redacted]";
}

public interface IZkTransportSession
{
    bool Network(string address,int port);
    bool Usb(int machine);
    bool Serial(int port,int machine,int baud);
}
public static class ZkTransportRouter
{
    // Local SDK methods cannot be called without the station-wide child lease.
    public static bool Connect(ZkProbeInput input,IZkTransportSession session)
    {
        if(!input.IsValid())return false;
        if(input.LocalTransport is not{} local)return session.Network(input.Address,input.Port);
        if(!ZkTransportLease.Owned(local))return false;
        return local.Kind=="usb"?session.Usb(input.MachineNumber):session.Serial(local.SerialPort,input.MachineNumber,local.BaudRate);
    }
}

/// <summary>Non-waiting Windows global physical-resource mutexes, held for the entire single-use child.
/// Serial aliases share one COM-number resource; USB selection owns the whole station SDK USB selector.
/// Cooperating IO workers only: installation must exclude vendor software and unapproved USB devices.</summary>
public sealed class ZkTransportLease:IDisposable
{
    [ThreadStatic] private static HashSet<string>? owned;
    private readonly List<(string Resource,Mutex Mutex)> leases=[];
    private bool disposed;
    private ZkTransportLease(){}
    public static ZkTransportLease Acquire(IEnumerable<ZkProbeInput> connections)
    {
        var result=new ZkTransportLease();
        try
        {
            var locals=connections.Select(c=>c.IsValid()?c.LocalTransport:throw new ArgumentException("Invalid connection binding.")).Where(c=>c is not null).Cast<ZkLocalTransport>().ToArray();
            if(locals.Length==0)return result;
            if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
            if(locals.Any(c=>c.StationName!=Environment.MachineName.ToUpperInvariant())||locals.Select(c=>c.Resource).Distinct().Count()!=locals.Length)throw new InvalidOperationException("Ambiguous or wrong-station physical binding.");
            owned??=[];
            foreach(var local in locals.OrderBy(c=>c.Resource,StringComparer.Ordinal))
            {
                if(owned.Contains(local.Resource))throw new InvalidOperationException("Physical endpoint already owned.");
                var mutex=new Mutex(false,"Global\\IO.ZkLocal.v1."+local.Resource);
                bool acquired=false;
                try
                {
                    try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){mutex.ReleaseMutex();throw new InvalidOperationException("Abandoned physical lease; inspect original durable outcome before retry.");}
                    if(!acquired)throw new InvalidOperationException("Physical endpoint busy.");
                    owned.Add(local.Resource);result.leases.Add((local.Resource,mutex));
                }
                catch{if(acquired)mutex.ReleaseMutex();mutex.Dispose();throw;}
            }
            return result;
        }
        catch{result.Dispose();throw;}
    }
    public static ZkTransportLease AcquireEnvelope(JsonElement root)
    {
        var connections=new List<ZkProbeInput>();
        void Find(JsonElement value,int depth)
        {
            if(depth>8)throw new ArgumentException("Bounded connection envelope required.");
            if(value.ValueKind!=JsonValueKind.Object)return;
            if(value.TryGetProperty("CommKey",out _)&&value.TryGetProperty("DeviceId",out _))
            {connections.Add(value.Deserialize<ZkProbeInput>()??throw new ArgumentException("Connection required."));return;}
            foreach(var property in value.EnumerateObject())if(property.Name is "Connection" or "Source" or "Target" or "Request")Find(property.Value,depth+1);
        }
        Find(root,0);return Acquire(connections);
    }
    internal static bool Owned(ZkLocalTransport local)=>OperatingSystem.IsWindows()&&local.StationName==Environment.MachineName.ToUpperInvariant()&&owned?.Contains(local.Resource)==true;
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        foreach(var (resource,mutex) in leases.AsEnumerable().Reverse()){owned?.Remove(resource);try{mutex.ReleaseMutex();}finally{mutex.Dispose();}}
        leases.Clear();
    }
}
