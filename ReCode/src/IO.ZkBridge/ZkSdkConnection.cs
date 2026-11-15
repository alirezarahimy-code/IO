using IO.DeviceContracts;
namespace IO.ZkBridgeTransport;
public static class ZkSdkConnection
{
 public static bool Connect(object instance,ZkProbeInput input)=>ZkTransportRouter.Connect(input,new Session(instance));
 private sealed class Session(object instance):IZkTransportSession
 {
  public bool Network(string address,int port)=>(bool)((dynamic)instance).Connect_Net(address,port);
  public bool Usb(int machine)=>(bool)((dynamic)instance).Connect_USB(machine);
  public bool Serial(int port,int machine,int baud)=>(bool)((dynamic)instance).Connect_Com(port,machine,baud);
 }
}
