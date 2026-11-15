using System.Globalization;
using IO.DeviceContracts;

/// <summary>Closed reference mappings only. Not routed by Program or any worker until durable independent authorization/recovery is implemented.</summary>
internal sealed class ZkComUserEraseSession(object instance,int machine):IProtectedEraseSession
{
 public BiometricDeviceIdentity Identity()
 {
  dynamic sdk=instance;string serial,product,firmware;
  if(!(bool)sdk.GetSerialNumber(machine,out serial)||!(bool)sdk.GetProductCode(machine,out product)||!(bool)sdk.GetFirmwareVersion(machine,out firmware))throw new InvalidDataException();
  return new(serial.Trim(),product.Trim(),firmware.Trim());
 }
 public UserEraseCatalogue ReadCatalogue(UserEraseApproval a)
 {
  if(!a.IsValid())throw new InvalidDataException();dynamic sdk=instance;
  int before=0;if(!(bool)sdk.GetDeviceStatus(machine,2,ref before)||before<0||before>a.MaximumUsers||!(bool)sdk.ReadAllUserID(machine))throw new InvalidDataException();
  var users=new List<(ZkUser User,string Password)>();
  while(true)
  {
   string person,name="",password="";int privilege=0;bool enabled=false;
   try
   {
    bool found;
    if(a.UserFormat==ZkUserReadFormats.Ssr)found=(bool)sdk.SSR_GetAllUserInfo(machine,out person,out name,out password,out privilege,out enabled);
    else if(a.UserFormat==ZkUserReadFormats.Legacy){int number=0;found=(bool)sdk.GetAllUserInfo(machine,ref number,ref name,ref password,ref privilege,ref enabled);person=ZkUserReadFormats.LegacyIdentity(number);}
    else throw new InvalidDataException();
    if(!found)break;
    if(users.Count>=before||password is null||password.Length>128)throw new InvalidDataException();
    string card;if(!(bool)sdk.GetStrCardNumber(out card)||card is null)throw new InvalidDataException();
    var user=new ZkUser(person,name,card,privilege,enabled);if(!user.IsValid()||users.Any(u=>u.User.PersonIdentifier==person))throw new InvalidDataException();
    users.Add((user,password));
   }
   finally{password=string.Empty;} // Releasing managed references cannot promise secure memory erasure.
  }
  int after=0;if(!(bool)sdk.GetDeviceStatus(machine,2,ref after)||before!=after||users.Count!=before)throw new InvalidDataException();
  if(a.RequiresReadAllTemplate&&!(bool)sdk.ReadAllTemplate(machine))throw new InvalidDataException();
  var entries=new List<UserEraseEntry>();long size=0;
  foreach(var u in users)
  {
   var fingers=ReadFingerprints(u.User.PersonIdentifier,a);size+=fingers.Sum(f=>(long)(f?.Data.Length??0));
   if(size>a.MaximumTotalTemplateCharacters)throw new InvalidDataException();entries.Add(new(u.User,u.Password,fingers));
  }
  int finished=0;if(!(bool)sdk.GetDeviceStatus(machine,2,ref finished)||finished!=before)throw new InvalidDataException();
  var result=new UserEraseCatalogue(entries);return result.IsValid(a)?result:throw new InvalidDataException();
 }
 public IReadOnlyList<UserEraseFingerprint?> ReadFingerprints(string person,UserEraseApproval a)
 {
  if(!a.IsValid()||!Person(person,a))throw new InvalidDataException();dynamic sdk=instance;
  var slots=new List<UserEraseFingerprint?>();long size=0;
  for(int slot=0;slot<10;slot++)
  {
   string data="";int length=0,flag=0;bool found;
   if(a.UserFormat==ZkUserReadFormats.Ssr)found=(bool)sdk.GetUserTmpExStr(machine,person,slot,out flag,out data,out length);
   else found=(bool)sdk.GetUserTmpStr(machine,int.Parse(person,NumberStyles.None,CultureInfo.InvariantCulture),slot,ref data,ref length);
   if(found)
   {
    var finger=new UserEraseFingerprint(data,length,flag);if(!finger.IsValid(a))throw new InvalidDataException();
    size+=data.Length;if(size>a.MaximumTotalTemplateCharacters)throw new InvalidDataException();slots.Add(finger);
   }
   else
   {
    if(!string.IsNullOrEmpty(data)||length!=0||flag!=0)throw new InvalidDataException();
    int error=int.MinValue;sdk.GetLastError(ref error);if(error!=a.FingerprintAbsentError)throw new InvalidDataException();slots.Add(null);
   }
  }
  return slots;
 }
 private static bool Person(string? s,UserEraseApproval a)=>s is{Length:>=1 and <=40}&&s.All(char.IsAsciiDigit)&&(a.UserFormat!=ZkUserReadFormats.Legacy||ZkUserReadFormats.IsCanonicalLegacyIdentity(s));
 public bool ClearAdministrators()=>(bool)((dynamic)instance).ClearAdministrators(machine);
 public bool Delete(string person,UserEraseApproval a)
 {
  if(!a.IsValid()||!Person(person,a))return false;
  // SSR13 != legacy11. Never guess from IsTFTMachine, coerce a string badge or fall back after a failed write.
  if(a.UserFormat==ZkUserReadFormats.Ssr)return (bool)((dynamic)instance).SSR_DeleteEnrollDataExt(machine,person,a.Mode==UserEraseApproval.User?12:13);
  return (bool)((dynamic)instance).DeleteEnrollData(machine,int.Parse(person,NumberStyles.None,CultureInfo.InvariantCulture),machine,a.Mode==UserEraseApproval.User?12:11);
 }
}
