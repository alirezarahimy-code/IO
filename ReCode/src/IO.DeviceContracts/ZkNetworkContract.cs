using System.Globalization;
using System.Text.Json.Serialization;

namespace IO.DeviceContracts;

/// <summary>Closed IPv4 transition; no DNS, port, gateway, mask, DHCP, MAC or communication-key writes.</summary>
public sealed record ZkNetworkPlan(string OldAddress,string NewAddress,string Serial,Guid? ChangeId=null,bool ObserveNewAddress=false)
{
    public bool IsValidFor(string operation)=>Ipv4(OldAddress)&&Ipv4(NewAddress)&&OldAddress!=NewAddress&&
        !string.IsNullOrWhiteSpace(Serial)&&Serial.Length<=100&&Serial==Serial.Trim()&&!Serial.Any(char.IsControl)&&
        (operation=="network.ip.set"?ChangeId is null&&!ObserveNewAddress:operation=="network.inspect"&&ChangeId is not null&&ChangeId!=Guid.Empty);
    public string TargetAddress(string operation)=>operation=="network.inspect"&&ObserveNewAddress?NewAddress:OldAddress;
    public static bool Ipv4(string? value)
    {
        if(value is null)return false;
        var parts=value.Split('.');
        if(parts.Length!=4||!parts.All(p=>byte.TryParse(p,NumberStyles.None,CultureInfo.InvariantCulture,out var b)&&b.ToString(CultureInfo.InvariantCulture)==p))return false;
        // Reject unspecified, loopback, multicast and obvious all-host broadcast. Subnet-specific
        // network/broadcast/reservation checks remain the installation administrator's responsibility.
        var first=byte.Parse(parts[0],CultureInfo.InvariantCulture);
        return first is >0 and <224&&first!=127&&value!="255.255.255.255";
    }
}
public sealed record ZkNetworkObservation([property:JsonRequired]string Serial,[property:JsonRequired]string ConnectedAddress,[property:JsonRequired]string DeviceIp)
{
    public bool IsValid()=>!string.IsNullOrWhiteSpace(Serial)&&Serial.Length<=100&&Serial==Serial.Trim()&&!Serial.Any(char.IsControl)&&ZkNetworkPlan.Ipv4(ConnectedAddress)&&ZkNetworkPlan.Ipv4(DeviceIp);
}
public static class ZkNetworkPolicy
{
    public static bool Allows(ZkNetworkPlan plan,string operation,string installedOldAddress,string installedSerial,bool allowWrite,bool allowInspect,IEnumerable<string>? approvedAddresses)=>
        plan.IsValidFor(operation)&&plan.OldAddress==installedOldAddress&&plan.Serial==installedSerial&&
        (operation=="network.ip.set"?allowWrite:allowInspect)&&approvedAddresses is not null&&
        approvedAddresses.Contains(plan.OldAddress,StringComparer.Ordinal)&&approvedAddresses.Contains(plan.NewAddress,StringComparer.Ordinal);
}
public interface IZkNetworkSession
{
    string? ReadIp();
    bool SetIp(string address);
    void Refresh();
}
public static class ZkNetworkExecutor
{
    // Caller must connect to the approved address and verify the exact configured serial first.
    public static ZkOperationOutput Execute(ZkOperationInput input,IZkNetworkSession session)
    {
        if(!input.IsValid()||input.NetworkPlan is not{} plan)return new("unsupported");
        string? before;
        try{before=session.ReadIp();}catch{return new("failed");}
        if(!ZkNetworkPlan.Ipv4(before))return new("failed");
        if(input.Operation=="network.inspect")return new("succeeded",Network:new(plan.Serial,input.Connection.Address,before!));
        if(before!=plan.OldAddress)return new("failed"); // no silent NAT/DHCP or changed-setting overwrite
        try
        {
            if(session.SetIp(plan.NewAddress))session.Refresh();
        }
        catch{ /* Even false or an exception can follow a device-side mutation. */ }
        // ACK is not a proof of reachability at the new IP. Separate fresh read-only recovery is mandatory.
        return new("unknown");
    }
}
