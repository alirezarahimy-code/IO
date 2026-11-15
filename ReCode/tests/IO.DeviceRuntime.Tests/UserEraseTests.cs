using System.Text.Json;
using IO.DeviceContracts;
internal static class UserEraseTests
{
 internal static UserEraseApproval Approval(string format=ZkUserReadFormats.Ssr,string mode=UserEraseApproval.User)=>new(new("SYNTHETIC-USER-ERASE","SYNTHETIC-PRODUCT","SYNTHETIC-FIRMWARE"),format,mode,"SYNTHETIC-NO-FACE-PHOTO-DOOR","SYNTHETIC-EXACT-EFFECT","SYNTHETIC-LEGAL","SYNTHETIC-IRRECOVERABLE","SYNTHETIC-EXCLUSIVE","SYNTHETIC-RESET",-404,FingerprintOnlyAttendanceProfile:true,AllowTransientProtectedCatalogue:true,AllowDelete:true);
 internal static UserEraseInput Input(UserEraseApproval? approval=null)
 {
  var a=approval??Approval();return new(new(Guid.NewGuid(),"192.0.2.43",4370,7,a.Device.Serial,0),Guid.NewGuid(),UserEraseSdkDouble.Initial(a)[0].User,a);
 }
 internal static UserEraseOutput Execute(UserEraseInput input,UserEraseSdkDouble sdk)=>UserEraseExecutor.Execute(input,new ZkComUserEraseSession(sdk,7));
 internal static async Task RunAsync()
 {
  int count=0;void Check(bool b,string text){if(!b)throw new InvalidOperationException(text);count++;Console.WriteLine("PASS "+text);}
  var a=Approval();var input=Input(a);
  Check(input.IsValid()&&!(a with{AllowDelete=false}).IsValid(),"R13 delete capability is explicit default-off");
  Check(!(a with{FingerprintOnlyAttendanceProfile=false}).IsValid()&&!(a with{AllowTransientProtectedCatalogue=false}).IsValid(),"R13 no-other-biometric-door-stores and transient protected-read policy are independent prerequisites");
  Check(!(a with{Mode="face"}).IsValid()&&!(a with{Mode="all-data"}).IsValid(),"R13 closed modes cannot become face photo admin or all-data erasure");
  Check(!(a with{NativeEffectReference=""}).IsValid()&&!(a with{IrrecoverableDeletionReference=""}).IsValid()&&!(a with{ExclusiveControlReference=""}).IsValid(),"R13 installed exact effect irreversible-disposition and exclusive-control evidence required");
  Check(!(a with{FingerprintAbsentError=0}).IsValid()&&!(a with{MaximumUsers=1001}).IsValid()&&!(a with{MaximumTotalTemplateCharacters=8388609}).IsValid(),"R13 exact nonzero absence code and bounded whole protected inventory required");
  Check(!(input with{Expected=input.Expected with{Privilege=3}}).IsValid(),"R13 administrator target is rejected even with deletion approval");
  Check(!(Input(Approval(ZkUserReadFormats.Legacy)) with{Expected=input.Expected}).IsValid(),"R13 legacy cannot alias SSR leading zeroes by numeric coercion");
  Check(!(input with{EraseUserOrFingerprints=false}).IsValid(),"R13 discriminator cannot be downgraded to generic native dispatch");
  foreach(var format in new[]{ZkUserReadFormats.Ssr,ZkUserReadFormats.Legacy})foreach(var mode in new[]{UserEraseApproval.User,UserEraseApproval.Fingerprints})
  {
   var i=Input(Approval(format,mode));var sdk=new UserEraseSdkDouble(i.Approval);var result=Execute(i,sdk);
   Check(result.State==(mode==UserEraseApproval.User?"user-deleted-verified":"fingerprints-deleted-verified")&&sdk.Writes==1&&sdk.Reads==3&&sdk.LastMachine==7&&sdk.LastPerson==i.Expected.PersonIdentifier&&sdk.LastFlag==(mode==UserEraseApproval.User?12:format==ZkUserReadFormats.Ssr?13:11)&&sdk.LastApi==(format==ZkUserReadFormats.Ssr?"ssr":"legacy")&&(format!=ZkUserReadFormats.Legacy||sdk.LastEnrollmentMachine==7),"R13 actual production adapter/executor "+format+" "+mode+" keeps exact reference flag/API and validates three whole catalogues");
  }
  UserEraseSdkDouble S(UserEraseApproval? x=null)=>new(x??a);
  var s=S();s.Entries=s.Entries.Skip(1).ToList();Check(Execute(input,s).State=="already-absent"&&s.Writes==0,"R13 only complete enumeration can prove absent target no-op");
  var fingerInput=Input(a with{Mode=UserEraseApproval.Fingerprints});s=S(fingerInput.Approval);s.Entries[0]=UserEraseSdkDouble.With(s.Entries[0],empty:true);Check(Execute(fingerInput,s).State=="already-empty"&&s.Writes==0,"R13 zero fingerprints is a no-op not user deletion");
  s=S();Check(Execute(input with{Expected=input.Expected with{Name="CHANGED"}},s).State=="catalogue-mismatch"&&s.Writes==0,"R13 expected metadata is bound before any destructive call");
  s=S();s.BeforeSecond=x=>x.Entries[1]=UserEraseSdkDouble.With(x.Entries[1],password:"REPLACED");Check(Execute(input,s).State=="catalogue-mismatch"&&s.Writes==0,"R13 same-count protected password change between scans refuses dispatch");
  s=S();s.BeforeSecond=x=>x.Entries[1]=UserEraseSdkDouble.With(x.Entries[1],replaceFinger:true);Check(Execute(input,s).State=="catalogue-mismatch"&&s.Writes==0,"R13 other-user fingerprint replacement between scans refuses dispatch");
  s=S();s.ReturnFalse=true;Check(Execute(input,s).State=="unknown"&&s.Writes==1,"R13 false response after possible deletion is unknown without retry");
  s=S();s.ThrowWrite=true;Check(Execute(input,s).State=="unknown"&&s.Writes==1,"R13 destructive exception is never failed-before-write");
  s=S();s.AfterWrite=x=>x.Entries[0]=UserEraseSdkDouble.With(x.Entries[0],password:"CORRUPTED");Check(Execute(input,s).State=="unknown"&&s.Writes==1,"R13 deleting target but changing another user's password fails protected outside-catalogue readback");
  s=S();s.AfterWrite=x=>x.Entries[0]=UserEraseSdkDouble.With(x.Entries[0],replaceFinger:true);Check(Execute(input,s).State=="unknown"&&s.Writes==1,"R13 deleting target but changing another user's fingerprint remains unknown");
  s=S(fingerInput.Approval);s.AfterWrite=x=>x.Entries[0]=UserEraseSdkDouble.With(x.Entries[0],password:"CLEARED");Check(Execute(fingerInput,s).State=="unknown"&&s.Writes==1,"R13 fingerprint-only must preserve target password card metadata and enabled state");
  s=S(fingerInput.Approval);s.KeepFingerprint=true;Check(Execute(fingerInput,s).State=="unknown"&&s.Writes==1,"R13 ACK retaining one target fingerprint is not verified");
  s=S();s.KeepOrphan=true;Check(Execute(input,s).State=="unknown"&&s.Writes==1,"R13 absent user record alone cannot hide readable orphan fingerprint slots");
  s=S();s.AfterWrite=x=>x.Entries.Clear();Check(Execute(input,s).State=="unknown"&&s.Writes==1,"R13 over-deletion of unrelated user is never verified");
  s=S();s.BadIdentity=true;Check(Execute(input,s).State=="failed-before-write"&&s.Writes==0,"R13 exact installed serial product firmware checked before reads and deletion");
  s=S();s.AfterWrite=x=>x.BadIdentity=true;Check(Execute(input,s).State=="unknown"&&s.Writes==1,"R13 identity replacement after dispatch is unknown");
  s=S();s.EarlyEnd=true;Check(Execute(input,s).State=="failed-before-write"&&s.Writes==0,"R13 incomplete user enumeration is not target absence");
  s=S();s.DuplicateUser=true;Check(Execute(input,s).State=="failed-before-write"&&s.Writes==0,"R13 duplicate identifiers cannot satisfy protected inventory");
  s=S();s.FailCount=true;Check(Execute(input,s).State=="failed-before-write"&&s.Writes==0,"R13 unavailable native user count refuses deletion");
  s=S();s.FailCard=true;Check(Execute(input,s).State=="failed-before-write"&&s.Writes==0,"R13 missing protected card read is not an empty card");
  s=S();s.NullPassword=true;Check(Execute(input,s).State=="failed-before-write"&&s.Writes==0,"R13 missing SDK password is not an empty password");
  s=S();s.AbsenceError=-999;Check(Execute(input,s).State=="failed-before-write"&&s.Writes==0,"R13 arbitrary getter failure is never absence evidence");
  s=S();s.DirtyAbsent=true;Check(Execute(input,s).State=="failed-before-write"&&s.Writes==0,"R13 false getter with dirty output is unavailable not absent");
  s=S();Check(Execute(input with{Approval=a with{MaximumUsers=1}},s).State=="failed-before-write"&&s.Writes==0,"R13 oversized native user catalogue cannot be truncated or split");
  s=S();Check(Execute(input with{Approval=a with{MaximumTemplateCharacters=2}},s).State=="failed-before-write"&&s.Writes==0,"R13 individual native template size bounded before deletion");
  s=S();Check(Execute(input with{Approval=a with{MaximumTotalTemplateCharacters=5}},s).State=="failed-before-write"&&s.Writes==0,"R13 aggregate raw material budget bounded without hashing or serializing templates");
  s=S();s.FailTemplates=true;Check(Execute(input with{Approval=a with{RequiresReadAllTemplate=true}},s).State=="failed-before-write"&&s.Writes==0,"R13 approved cache refresh failure aborts rather than reading guessed stale templates");
  s=S();s.AfterWrite=x=>x.FailCard=true;Check(Execute(input,s).State=="unknown"&&s.Writes==1,"R13 post-dispatch read failure stays unknown");
  var catalogue=new UserEraseCatalogue(UserEraseSdkDouble.Initial(a));Check(JsonSerializer.Serialize(catalogue)=="{}"&&JsonSerializer.Serialize(catalogue.Entries[0])=="{}"&&JsonSerializer.Serialize(catalogue.Entries[0].Fingerprints[0])=="{}"&&!input.ToString().Contains("001",StringComparison.Ordinal),"R13 raw catalogue entries templates passwords and subject identifiers never appear in serializable inspection or ToString");
  Check(!new UserEraseOutput(fingerInput.OperationId,"user-deleted-verified").IsValidFor(fingerInput),"R13 user-success state cannot stand in for fingerprint-only deletion");
  var host=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(UserEraseTests).Assembly.Location;
  Check((await NativeProcessSupervisor.RunUserEraseAsync(host,assembly,input,5)).State=="user-deleted-verified","R13 actual bounded supervisor executes synthetic child production mapper and executor");
  foreach(var kind in new[]{"SYNTHETIC-HANG","SYNTHETIC-EXTRA","SYNTHETIC-FOREIGN","SYNTHETIC-WRONG-MODE","SYNTHETIC-EXIT"})
  {
   var i=fingerInput with{Approval=fingerInput.Approval with{NativeEffectReference=kind}};
   Check((await NativeProcessSupervisor.RunUserEraseAsync(host,assembly,i,kind=="SYNTHETIC-HANG"?1:5)).State=="unknown","R13 child "+kind+" cannot manufacture a safe result or permit replay");
  }
  using var stop=new CancellationTokenSource();stop.Cancel();Check((await NativeProcessSupervisor.RunUserEraseAsync(host,assembly,input,5,stop.Token)).State=="failed-before-write","R13 pre-cancel never launches a destructive child");
  Console.WriteLine("::notice title=User erase core contracts::"+count+" synthetic checks passed; core-only production mapper and supervisor subset; operational queue/authorization/recovery/UI covered separately, no SDK/device execution.");
 }
 internal static async Task ChildAsync(string text)
 {
  var i=JsonSerializer.Deserialize<UserEraseInput>(text)!;var marker=i.Approval.NativeEffectReference;
  if(marker=="SYNTHETIC-HANG"){await Task.Delay(TimeSpan.FromMinutes(1));return;}
  var result=Execute(i,new UserEraseSdkDouble(i.Approval));
  if(marker=="SYNTHETIC-EXTRA")Console.WriteLine("{\"OperationId\":\""+i.OperationId+"\",\"State\":\"user-deleted-verified\",\"Raw\":[]}");
  else Console.WriteLine(JsonSerializer.Serialize(marker=="SYNTHETIC-FOREIGN"?result with{OperationId=Guid.NewGuid()}:marker=="SYNTHETIC-WRONG-MODE"?result with{State="user-deleted-verified"}:result));
  if(marker=="SYNTHETIC-EXIT")Environment.ExitCode=1;
 }
}

// Public for C# dynamic binding to the exact production adapter, never a COM registration or device implementation.
public sealed class UserEraseSdkDouble(UserEraseApproval approval)
{
 public List<UserEraseEntry> Entries=Initial(approval).ToList();private int cursor;private UserEraseEntry? current;private UserEraseEntry? orphan;
 public int Reads,Writes,LastMachine,LastFlag,LastEnrollmentMachine;public string? LastPerson,LastApi;
 public bool ReturnFalse,ThrowWrite,KeepFingerprint,KeepOrphan,BadIdentity,EarlyEnd,DuplicateUser,FailCount,FailCard,NullPassword,DirtyAbsent,FailTemplates;
 public int AbsenceError=approval.FingerprintAbsentError;public Action<UserEraseSdkDouble>? BeforeSecond,AfterWrite;
 public static UserEraseEntry[] Initial(UserEraseApproval a)
 {
  var fingers=new UserEraseFingerprint?[10];fingers[0]=new("SYNTHETIC-A",10,a.UserFormat==ZkUserReadFormats.Ssr?1:0);fingers[9]=new("SYNTHETIC-B",10,0);
  var other=new UserEraseFingerprint?[10];other[3]=new("SYNTHETIC-C",10,a.UserFormat==ZkUserReadFormats.Ssr?1:0);
  return [new(new(a.UserFormat==ZkUserReadFormats.Ssr?"001":"1","Synthetic target","0007",0,true),"SYNTHETIC-PASSWORD-0",fingers),new(new(a.UserFormat==ZkUserReadFormats.Ssr?"1":"2","Synthetic admin","0008",3,true),"SYNTHETIC-PASSWORD-1",other)];
 }
 public static UserEraseEntry With(UserEraseEntry e,string? password=null,bool empty=false,bool replaceFinger=false)
 {
  var fingers=e.Fingerprints.ToArray();if(empty)Array.Clear(fingers);if(replaceFinger)fingers[3]=new("SYNTHETIC-REPLACED",10,0);return new(e.User,password??e.Password,fingers);
 }
 public bool GetSerialNumber(int machine,out string value){value=approval.Device.Serial;return true;}
 public bool GetProductCode(int machine,out string value){value=approval.Device.Product;return true;}
 public bool GetFirmwareVersion(int machine,out string value){value=BadIdentity?"OTHER":approval.Device.Firmware;return true;}
 public bool GetDeviceStatus(int machine,int code,ref int value){if(code!=2||FailCount)return false;value=Entries.Count;return true;}
 public bool ReadAllUserID(int machine){Reads++;cursor=0;if(Reads==2)BeforeSecond?.Invoke(this);return true;}
 private bool Next()
 {
  if(cursor>=(EarlyEnd?Math.Max(0,Entries.Count-1):Entries.Count))return false;current=Entries[DuplicateUser?0:cursor];cursor++;return true;
 }
 public bool SSR_GetAllUserInfo(int machine,out string person,out string name,out string password,out int privilege,out bool enabled)
 {
  var found=Next();person=current?.User.PersonIdentifier??"";name=current?.User.Name??"";password=NullPassword?null!:current?.Password??"";privilege=current?.User.Privilege??0;enabled=current?.User.Enabled??false;return found;
 }
 public bool GetAllUserInfo(int machine,ref int person,ref string name,ref string password,ref int privilege,ref bool enabled)
 {
  var found=Next();person=int.Parse(current?.User.PersonIdentifier??"0",System.Globalization.CultureInfo.InvariantCulture);name=current?.User.Name??"";password=NullPassword?null!:current?.Password??"";privilege=current?.User.Privilege??0;enabled=current?.User.Enabled??false;return found;
 }
 public bool GetStrCardNumber(out string value){value=current?.User.Card??"";return !FailCard;}
 public bool ReadAllTemplate(int machine)=>!FailTemplates;
 public bool GetUserTmpExStr(int machine,string person,int slot,out int flag,out string data,out int length)
 {
  var entry=Entries.SingleOrDefault(e=>e.User.PersonIdentifier==person)??(orphan?.User.PersonIdentifier==person?orphan:null);var finger=entry?.Fingerprints[slot];
  flag=finger?.Flag??0;data=finger?.Data??(DirtyAbsent?"DIRTY":"");length=finger?.NativeLength??0;return finger is not null;
 }
 public bool GetUserTmpStr(int machine,int person,int slot,ref string data,ref int length)
 {
  var result=GetUserTmpExStr(machine,person.ToString(System.Globalization.CultureInfo.InvariantCulture),slot,out _,out var d,out var n);data=d;length=n;return result;
 }
 public void GetLastError(ref int error)=>error=AbsenceError;
 public bool ClearAdministrators(int machine)
 {
  Writes++;LastMachine=machine;LastApi="administrators";Entries=Entries.Select(e=>new UserEraseEntry(e.User with{Privilege=0},e.Password,e.Fingerprints)).ToList();AfterWrite?.Invoke(this);if(ThrowWrite)throw new InvalidOperationException();return !ReturnFalse;
 }
 public bool SSR_DeleteEnrollDataExt(int machine,string person,int flag){LastApi="ssr";return Delete(machine,person,flag,0);}
 public bool DeleteEnrollData(int machine,int person,int enrollmentMachine,int flag){LastApi="legacy";return Delete(machine,person.ToString(System.Globalization.CultureInfo.InvariantCulture),flag,enrollmentMachine);}
 private bool Delete(int machine,string person,int flag,int enrollmentMachine)
 {
  Writes++;LastMachine=machine;LastPerson=person;LastFlag=flag;LastEnrollmentMachine=enrollmentMachine;
  var target=Entries.Single(e=>e.User.PersonIdentifier==person);
  if(flag==12){Entries.Remove(target);if(KeepOrphan)orphan=target;}
  else if(!KeepFingerprint)Entries[Entries.IndexOf(target)]=With(target,empty:true);
  AfterWrite?.Invoke(this);if(ThrowWrite)throw new InvalidOperationException("Synthetic command ambiguity.");return !ReturnFalse;
 }
}
