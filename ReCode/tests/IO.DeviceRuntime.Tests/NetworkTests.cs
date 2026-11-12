using System.Runtime.InteropServices;
using IO.DeviceContracts;
internal static class NetworkTests
{
 public static async Task RunAsync()
 {
  int count=0;void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);Console.WriteLine("PASS network "+message);count++;}
  var plan=new ZkNetworkPlan("10.20.30.40","10.20.30.41","network-fixture");
  var pin=new ZkProbeInput(Guid.NewGuid(),plan.OldAddress,4370,1,plan.Serial,0);
  var input=new ZkOperationInput(pin,"network.ip.set",null,false,NetworkPlan:plan,AllowNetworkIpChange:true);
  Check(input.IsValid()&&!(input with{AllowNetworkIpChange=false}).IsValid()&&!(input with{ClockLocal="2026-11-08 10:00:00"}).IsValid(),"network write is a separate explicit typed approval without clock payload");
  Check(new[]{"1.2.3","1.2.3.004","0.0.0.0","127.0.0.1","224.1.1.1","255.255.255.255","::1","device.example"," 10.20.30.41","10.20.30.256"}.All(x=>!ZkNetworkPlan.Ipv4(x)),"noncanonical DNS IPv6 loopback multicast and malformed addresses rejected");
  Check(!(input with{NetworkPlan=plan with{NewAddress=plan.OldAddress}}).IsValid()&&!(input with{NetworkPlan=plan with{ChangeId=Guid.NewGuid()}}).IsValid()&&!(input with{Connection=pin with{ExpectedSerial="other"}}).IsValid(),"same-address transitions foreign change identifiers and wrong serials rejected");
  Check(!ZkNetworkPolicy.Allows(plan,input.Operation,plan.OldAddress,plan.Serial,true,true,null)&&!ZkNetworkPolicy.Allows(plan,input.Operation,plan.OldAddress,plan.Serial,true,true,new[]{plan.OldAddress}),"both endpoints must be explicitly installed in the allowlist");
  Check(ZkNetworkPolicy.Allows(plan,input.Operation,plan.OldAddress,plan.Serial,true,false,new[]{plan.OldAddress,plan.NewAddress})&&!ZkNetworkPolicy.Allows(plan,input.Operation,plan.NewAddress,plan.Serial,true,true,new[]{plan.OldAddress,plan.NewAddress}),"approved transition cannot silently retarget the installed old endpoint");
  var session=new Session(plan.OldAddress);Check(ZkNetworkExecutor.Execute(input with{AllowNetworkIpChange=false},session).State=="unsupported"&&session.Writes==0&&session.Reads==0,"no approval means no SDK reads or writes");
  session=new("10.20.30.42");Check(ZkNetworkExecutor.Execute(input,session).State=="failed"&&session.Writes==0,"preflight changed DHCP or NAT IP cannot be overwritten");
  session=new(null);Check(ZkNetworkExecutor.Execute(input,session).State=="failed"&&session.Writes==0,"unavailable GetDeviceIP fails before mutation");
  session=new(plan.OldAddress);Check(ZkNetworkExecutor.Execute(input,session).State=="unknown"&&session.Writes==1&&session.Refreshes==1&&session.Ip==plan.NewAddress,"true ACK triggers one RefreshData but never fabricates confirmed new reachability");
  session=new(plan.OldAddress){ReturnFalse=true};Check(ZkNetworkExecutor.Execute(input,session).State=="unknown"&&session.Writes==1&&session.Refreshes==0,"false ACK remains unknown with no automatic replay");
  session=new(plan.OldAddress){ThrowAfterWrite=true};Check(ZkNetworkExecutor.Execute(input,session).State=="unknown"&&session.Writes==1,"lost connection after device-side change remains unknown");
  var inspectPlan=plan with{ChangeId=Guid.NewGuid(),ObserveNewAddress=true};var inspect=input with{Operation="network.inspect",Connection=pin with{Address=plan.NewAddress},NetworkPlan=inspectPlan,AllowNetworkIpChange=false,AllowNetworkInspect=true};
  session=new(plan.NewAddress);var observed=ZkNetworkExecutor.Execute(inspect,session);Check(inspect.IsValid()&&observed.IsValidFor(inspect)&&observed.Network?.DeviceIp==plan.NewAddress&&session.Writes==0&&session.Refreshes==0,"fresh inspection binds exact new endpoint and serial without any mutation");
  Check(!(observed with{Network=observed.Network! with{Serial="foreign"}}).IsValidFor(inspect)&&!(observed with{Network=observed.Network! with{ConnectedAddress=plan.OldAddress}}).IsValidFor(inspect)&&!new ZkOperationOutput("succeeded").IsValidFor(input),"foreign read-back or bare write success cannot bypass the result contract");
  Check(!(input with{Operation="clock.set",ClockLocal="2026-11-08 10:00:00",AllowClockSet=true}).IsValid(),"network data cannot enter clock restart or other generic operation branches");
  var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(NetworkTests).Assembly.Location;
  Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input,5)).State=="unknown"&&(await NativeProcessSupervisor.RunOperationAsync(host,assembly,inspect,5)).Network?.ConnectedAddress==plan.NewAddress,"actual child boundary separates an unknown write from a fresh read-only observation");
  var hung=plan with{Serial="network-hang"};Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial=hung.Serial},NetworkPlan=hung},1)).State=="unknown","network-write external deadline kills child without declaring the mutation failed");
  Console.WriteLine($"PASS {count} network contract checks; synthetic SDK sessions and real process isolation, not actual SDK or model acceptance");
 }
 internal sealed class Session(string? initial):IZkNetworkSession
 {
  public string? Ip=initial;public int Reads,Writes,Refreshes;public bool ReturnFalse,ThrowAfterWrite;
  public string? ReadIp(){Reads++;return Ip;}
  public bool SetIp(string address){Writes++;Ip=address;if(ThrowAfterWrite)throw new IOException();return !ReturnFalse;}
  public void Refresh(){Refreshes++;}
 }
}
