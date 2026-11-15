namespace IO.DeviceContracts;

/// <summary>Product support disposition, NOT a hardware capability detector or automatic fallback selector.
/// Direct USB/serial have real reference SDK methods but are not transports supported by this delivery.</summary>
public static class DeviceTransportSupport
{
 public sealed record Entry(string Kind,string Status,string? RegistryProfile,bool DirectConnection,bool SoftwareRouteAvailable,string Limitation);
 public static IReadOnlyList<Entry> Entries {get;}=Array.AsReadOnly(new[]{
  new Entry("sdk-network","implemented-model-approval-required","zk-standalone",true,true,"Only explicit IP/port allowlists, owned credentials, pinned installation and supported per-operation contracts; TCP reachability is not SDK acceptance."),
  new Entry("usb-file","implemented-explicit-file-format-only","offline-file",false,true,"Durable bounded file preview/quarantine is NOT a USB cable, Connect_USB, serial connection or a universal vendor file layout."),
  new Entry("sdk-usb-direct","unsupported-in-this-delivery",null,true,false,"Connect_USB(machine) exists in the reference ABI; this delivery has no driver/physical-endpoint ownership contract or local USB transport adapter. Never infer hardware incapability."),
  new Entry("sdk-serial-direct","unsupported-in-this-delivery",null,true,false,"Connect_Com(port,machine,baud) exists; reference COM1/115200 is not an installation default or permission to open a local port. No serial adapter or fallback."),
 });
 public static bool IsDirectLocalAddress(string? host)
 {
  if(string.IsNullOrEmpty(host))return false;
  var s=host.Trim();
  return s.Equals("USB",StringComparison.OrdinalIgnoreCase)||s.Equals("SERIAL",StringComparison.OrdinalIgnoreCase)||
   s.StartsWith("COM",StringComparison.OrdinalIgnoreCase)&&s.Length>3&&s.AsSpan(3).IndexOfAnyExceptInRange('0','9')<0||
   s.StartsWith("\\\\.\\",StringComparison.Ordinal)||s.StartsWith("\\\\?\\",StringComparison.Ordinal)||s.StartsWith("/dev/",StringComparison.Ordinal)||
   s.StartsWith("usb:",StringComparison.OrdinalIgnoreCase)||s.StartsWith("serial:",StringComparison.OrdinalIgnoreCase);
 }
}
