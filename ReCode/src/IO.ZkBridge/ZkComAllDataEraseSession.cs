using IO.DeviceContracts;
/// <summary>Only ClearData(machine,5). Never a factory reset or a caller-selected flag.</summary>
internal sealed class ZkComAllDataEraseSession(object instance,ZkProbeInput pin):IAllDataEraseSession
{
 private readonly ZkComUserEraseSession users=new(instance,pin.MachineNumber);
 private readonly ZkComAttendanceSession logs=new(instance,pin.MachineNumber);
 public BiometricDeviceIdentity Identity()=>users.Identity();
 public UserEraseCatalogue Users(UserEraseApproval a)=>users.ReadCatalogue(a);
 public IReadOnlyList<UserEraseFingerprint?> Fingerprints(string person,UserEraseApproval a)=>users.ReadFingerprints(person,a);
 public int? RecordCount()=>logs.RecordCount();
 public bool BeginAttendance()=>logs.Begin();
 public ZkAttendanceRaw? NextAttendance(string format)=>logs.Next(format);
 public bool ClearAllData()=>(bool)((dynamic)instance).ClearData(pin.MachineNumber,5);
 public bool ReconnectOriginal()
 {
  dynamic sdk=instance;sdk.Disconnect();
  return (bool)sdk.SetCommPassword(pin.CommKey)&&(bool)sdk.Connect_Net(pin.Address,pin.Port);
 }
}
