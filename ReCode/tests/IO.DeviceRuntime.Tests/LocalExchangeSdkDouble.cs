using System.Globalization;
using System.Text.Json;
using IO.DeviceContracts;

// Test assembly only; never referenced by a production project or selectable in product configuration.
public sealed class LocalExchangeState
{
 public string Serial {get;set;}="SYNTHETIC-LOCAL";
 public List<LocalExchangeUser> Users {get;set;}=[];
 public int Writes {get;set;}
 public bool DropNextWriteReply {get;set;}
 public bool PauseBeforeWrite {get;set;}
 public bool PartialEnumeration {get;set;}
 public static LocalExchangeState Initial(string format)=>new(){Users=[
  new(new(format==ZkUserReadFormats.Ssr?"001":"1","SYNTHETIC-ONE","1",0,true),"synthetic-password","SYNTHETIC-GROUP",["SYNTHETIC-FINGERPRINT"]),
  new(new("2","SYNTHETIC-ADMIN","2",3,true),"synthetic-admin-password","SYNTHETIC-ADMIN-GROUP",["SYNTHETIC-ADMIN-FINGERPRINT"])]};
}
public sealed record LocalExchangeUser(ZkUser User,string? Password,string Group,string[] Fingerprints);
public sealed class LocalExchangeSdkDouble(LocalExchangeState state,Action? persist=null,Action? beforeWrite=null)
{
 private int index;private LocalExchangeUser? enumerated;private string? card;
 public LocalExchangeState State=>state;
 public List<string> Connections {get;}=[];
 public bool DropReply {get;private set;}
 public int CardSets {get;private set;}
 public List<string> WriteFormats {get;}=[];
 public bool SetCommPassword(int key)=>key>=0;
 public bool Connect_Net(string address,int port){Connections.Add($"network:{address}:{port}");return true;}
 public bool Connect_USB(int machine){Connections.Add($"usb:{machine}");return true;}
 public bool Connect_Com(int port,int machine,int baud){Connections.Add($"serial:{port}:{machine}:{baud}");return true;}
 public bool GetSerialNumber(int machine,out string serial){serial=state.Serial;return true;}
 public bool GetDeviceStatus(int machine,int code,ref int value){if(code!=2)return false;value=state.Users.Count;return true;}
 public bool ReadAllUserID(int machine){index=0;return true;}
 private LocalExchangeUser? Next()
 {
  if(index>=state.Users.Count-(state.PartialEnumeration?1:0))return null;
  return enumerated=state.Users[index++];
 }
 public bool SSR_GetAllUserInfo(int machine,out string person,out string name,out string password,out int privilege,out bool enabled)
 {
  var row=Next();person=row?.User.PersonIdentifier??"";name=row?.User.Name??"";password=row is null?"":row.Password!;privilege=row?.User.Privilege??0;enabled=row?.User.Enabled??false;return row is not null;
 }
 public bool GetAllUserInfo(int machine,ref int person,ref string name,ref string password,ref int privilege,ref bool enabled)
 {
  var row=Next();if(row is null)return false;person=int.Parse(row.User.PersonIdentifier,CultureInfo.InvariantCulture);name=row.User.Name;password=row.Password!;privilege=row.User.Privilege;enabled=row.User.Enabled;return true;
 }
 public bool GetStrCardNumber(out string value){value=enumerated?.User.Card??"";return enumerated is not null;}
 public bool SetStrCardNumber(string value)
 {
  beforeWrite?.Invoke();card=value;CardSets++;return true;
 }
 public bool SSR_SetUserInfo(int machine,string person,string name,string password,int privilege,bool enabled){WriteFormats.Add(ZkUserReadFormats.Ssr);return Write(new(person,name,card,privilege,enabled),password);}
 public bool SetUserInfo(int machine,int person,string name,string password,int privilege,bool enabled){WriteFormats.Add(ZkUserReadFormats.Legacy);return Write(new(person.ToString(CultureInfo.InvariantCulture),name,card,privilege,enabled),password);}
 private bool Write(ZkUser user,string password)
 {
  var old=state.Users.SingleOrDefault(x=>x.User.PersonIdentifier==user.PersonIdentifier);
  var replacement=old is null?new(user,password,"",[]):old with{User=user,Password=password};
  if(old is null)state.Users.Add(replacement);else state.Users[state.Users.IndexOf(old)]=replacement;
  state.Writes++;DropReply=state.DropNextWriteReply;state.DropNextWriteReply=false;persist?.Invoke();return true;
 }
}

// GUID-only paths and explicit disposable-CI guard; synthetic state never enters SQL receipts or product config.
internal static class LocalExchangeFixtureStore
{
 private static string PathFor(Guid id)
 {
  if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true"||id==Guid.Empty)throw new InvalidOperationException("Disposable synthetic CI fixture required.");
  return Path.Combine(Path.GetTempPath(),"io-local-exchange-"+id.ToString("N")+".json");
 }
 internal static bool Exists(Guid id)=>File.Exists(PathFor(id));
 internal static LocalExchangeState Read(Guid id)=>JsonSerializer.Deserialize<LocalExchangeState>(File.ReadAllText(PathFor(id)))??throw new InvalidDataException();
 internal static void Save(Guid id,LocalExchangeState value)=>File.WriteAllText(PathFor(id),JsonSerializer.Serialize(value));
 internal static bool Ready(Guid id)=>File.Exists(PathFor(id)+".ready");
 internal static void Pause(Guid id){File.WriteAllText(PathFor(id)+".ready","SYNTHETIC-PREFLIGHT");Thread.Sleep(15000);}
 internal static void Delete(Guid id){File.Delete(PathFor(id));File.Delete(PathFor(id)+".ready");}
}
