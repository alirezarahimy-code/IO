using System.Text.Json;
using IO.DeviceContracts;
internal static class AllDataEraseTests
{
 internal static AllDataEraseApproval Approval(string format=ZkUserReadFormats.Ssr)
 {
  var u=UserEraseTests.Approval(format,UserEraseApproval.User);
  return new("all-data",u,new(u.Device,format==ZkUserReadFormats.Ssr?ZkAttendanceReadFormats.Ssr:ZkAttendanceReadFormats.Legacy,"SYNTHETIC-LEGAL","SYNTHETIC-RETENTION","SYNTHETIC-ALL-CHANNEL-STOP","SYNTHETIC-NON-DESTRUCTIVE-RESET",CaptureStoppedDuringMaintenance:true,ResetClearsPendingWrites:true,AllowErase:true),"SYNTHETIC-ONLY-USERS-PASSWORDS-CARDS-TEN-FINGERS-LOGS-NOT-RESET",true,true,true);
 }
 internal static ZkAttendanceRaw[] Rows(AllDataEraseApproval a)
 {
  var r=new ZkAttendanceRaw(a.Inventory.UserFormat==ZkUserReadFormats.Ssr?"001":"1",1,0,2026,11,8,7,0,0,0,a.Attendance.ReadFormat==ZkAttendanceReadFormats.Legacy?7:null,a.Attendance.ReadFormat==ZkAttendanceReadFormats.Legacy?9:null);return [r,r];
 }
 internal static string Digest(AllDataEraseApproval a,IEnumerable<ZkAttendanceRaw> rows){var d=new AttendanceArchiveDigest(a.Attendance.MaximumRecords);foreach(var row in rows)d.Add(row,a.Attendance.ReadFormat);return d.Finish();}
 internal static AllDataEraseInput Input(string format=ZkUserReadFormats.Ssr)
 {
  var a=Approval(format);var users=UserEraseSdkDouble.Initial(a.Inventory).Select(e=>e.User);return new(new(Guid.NewGuid(),"192.0.2.45",4370,7,a.Inventory.Device.Serial,0),Guid.NewGuid(),a,2,UserMetadataCommitment.Create(users),2,Digest(a,Rows(a)));
 }
 internal static AllDataEraseOutput Run(AllDataEraseInput i,AllDataSdkDouble sdk)=>AllDataEraseExecutor.Execute(i,new ZkComAllDataEraseSession(sdk,i.Connection));
 internal static async Task RunAsync()
 {
  int count=0;void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);count++;Console.WriteLine("PASS "+message);}
  foreach(var format in new[]{ZkUserReadFormats.Ssr,ZkUserReadFormats.Legacy})
  {
   var i=Input(format);AllDataSdkDouble S()=>new(i.Approval);var s=S();
   Check(i.IsValid()&&Run(i,s).State=="all-data-cleared-verified"&&s.Writes==1&&s.LastFlag==5&&s.LastMachine==7&&s.Reads==3&&s.LogScans==3&&s.Reconnections==1,"R15 "+format+" fixed flag5, two full archived scans, single command, original-endpoint reconnect, empty readback and orphan probes");
   s=S();Check(Run(i with{InspectOnly=true},s).State=="observed"&&s.Writes==0&&s.Reconnections==0,"R15 "+format+" recovery reads both archives without dispatch or credential fallback");
   s=S();Check(Run(i with{MetadataDigest=new string('A',64)},s).State=="failed-before-write"&&s.Writes==0,"R15 stale same-count metadata cannot clear");
   s=S();Check(Run(i with{AttendanceDigest=Digest(i.Approval,Rows(i.Approval).Take(1))},s).State=="failed-before-write"&&s.Writes==0,"R15 duplicate attendance multiplicity must match durable archive");
   s=S();s.BeforeSecond=x=>x.Entries[0]=UserEraseSdkDouble.With(x.Entries[0],password:"SYNTHETIC-CHANGED");Check(Run(i,s).State=="archive-mismatch"&&s.Writes==0,"R15 same-metadata password mutation before dispatch is rejected");
   s=S();s.ChangeLogs=true;Check(Run(i,s).State=="failed-before-write"&&s.Writes==0,"R15 same-count attendance replacement before dispatch rejects deletion");
   s=S();s.ReturnFalse=true;Check(Run(i,s).State=="unknown"&&s.Writes==1&&s.Reconnections==0,"R15 false after command is unknown and never retried");
   s=S();s.ThrowWrite=true;Check(Run(i,s).State=="unknown"&&s.Writes==1,"R15 exception after command remains unknown");
   s=S();s.KeepUsers=true;Check(Run(i,s).State=="unknown"&&s.Writes==1,"R15 ACK retaining any user or administrator is not success");
   s=S();s.KeepLogs=true;Check(Run(i,s).State=="unknown","R15 ACK retaining logs is not success");
   s=S();s.KeepOrphans=true;Check(Run(i,s).State=="unknown","R15 no users but readable orphan fingerprint is unknown");
   s=S();s.ReconnectOk=false;Check(Run(i,s).State=="unknown"&&s.Reconnections==1&&s.Writes==1,"R15 lost original endpoint or communication key never leads to fallback or factory reset");
   s=S();s.FailCount=true;Check(Run(i,s).State=="failed-before-write"&&s.Writes==0,"R15 missing user count is not empty archive");
   s=S();s.FailLogCount=true;Check(Run(i,s).State=="failed-before-write"&&s.Writes==0,"R15 unavailable log count cannot authorize all-data clear");
   s=S();s.IncompleteLogs=true;Check(Run(i,s).State=="failed-before-write"&&s.Writes==0,"R15 partial native enumeration cannot authorize all-data clear");
   s=S();s.FailCard=true;Check(Run(i,s).State=="failed-before-write"&&s.Writes==0,"R15 incomplete protected preflight never broad deletes");
   s=S();s.DirtyAbsent=true;Check(Run(i,s).State=="failed-before-write"&&s.Writes==0,"R15 dirty fingerprint absence fails before command");
   s=S();s.Entries=[];s.Logs=[];Check(Run(i with{ArchiveCount=0,MetadataDigest=UserMetadataCommitment.Create([]),AttendanceCount=0,AttendanceDigest=Digest(i.Approval,[])},s).State=="already-no-profile-data"&&s.Writes==0,"R15 empty declared profile is no-op, not proof about hidden stores or factory defaults");
  }
  var input=Input();
  foreach(var a in new[]{input.Approval with{AllowClearAllData=false},input.Approval with{NoOtherStores=false},input.Approval with{PreserveEndpointAndCredential=false},input.Approval with{Family="factory-reset"},input.Approval with{Inventory=input.Approval.Inventory with{FingerprintOnlyAttendanceProfile=false}},input.Approval with{Attendance=input.Approval.Attendance with{Device=input.Approval.Attendance.Device with{Firmware="OTHER"}}}})
   Check(!a.IsValid(),"R15 exact effect/profile and independent approval required; reset and mismatched installation rejected");
  Check(!new AllDataEraseOutput(input.OperationId,"deleted-verified").IsValidFor(input)&&!new AllDataEraseOutput(input.OperationId,"administrators-cleared-verified").IsValidFor(input)&&!new AllDataEraseOutput(input.OperationId,"observed").IsValidFor(input),"R15 wrong-family success or inspection cannot masquerade as all-data mutation");
  var host=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(AllDataEraseTests).Assembly.Location;
  Check((await NativeProcessSupervisor.RunAllDataEraseAsync(host,assembly,input,5)).State=="all-data-cleared-verified","R15 production process supervisor and production mapper in synthetic child");
  foreach(var marker in new[]{"EXTRA","FOREIGN","HANG"})Check((await NativeProcessSupervisor.RunAllDataEraseAsync(host,assembly,input with{Approval=input.Approval with{EffectReference=marker}},marker=="HANG"?1:5)).State=="unknown","R15 corrupt/foreign/deadline child cannot manufacture success: "+marker);
  using var stop=new CancellationTokenSource();stop.Cancel();Check((await NativeProcessSupervisor.RunAllDataEraseAsync(host,assembly,input,5,stop.Token)).State=="failed-before-write","R15 pre-cancellation cannot dispatch child");
  Console.WriteLine("::notice title=All-data clear contracts::"+count+" synthetic checks passed; production flag5 mapper and supervisor, no SDK or real all-data deletion or factory reset.");
 }
 internal static async Task ChildAsync(string text)
 {
  var i=JsonSerializer.Deserialize<AllDataEraseInput>(text)!;var marker=i.Approval.EffectReference;
  if(marker=="HANG"){await Task.Delay(TimeSpan.FromMinutes(1));return;}
  var output=Run(i,new(i.Approval));
  if(marker=="EXTRA")Console.WriteLine("{\"OperationId\":\""+i.OperationId+"\",\"State\":\"all-data-cleared-verified\",\"Raw\":[]}");
  else Console.WriteLine(JsonSerializer.Serialize(marker=="FOREIGN"?output with{OperationId=Guid.NewGuid()}:output));
 }
}
public sealed class AllDataSdkDouble(AllDataEraseApproval a):UserEraseSdkDouble(a.Inventory)
{
 public ZkAttendanceRaw[] Logs=AllDataEraseTests.Rows(a);private int logCursor;private UserEraseEntry[] orphans=[];
 public int LogScans,Reconnections;public bool ChangeLogs,KeepUsers,KeepLogs,KeepOrphans,ReconnectOk=true,FailLogCount,IncompleteLogs;
 public new bool GetDeviceStatus(int machine,int code,ref int value){if(code!=6)return base.GetDeviceStatus(machine,code,ref value);value=Logs.Length;return !FailLogCount;}
 public bool ReadAllGLogData(int machine){LogScans++;logCursor=0;if(ChangeLogs&&LogScans==2)Logs=[Logs[0] with{VerifyMode=9},..Logs.Skip(1)];return true;}
 private ZkAttendanceRaw? Next()=>logCursor<(IncompleteLogs?Logs.Length-1:Logs.Length)?Logs[logCursor++]:null;
 public bool SSR_GetGeneralLogData(int machine,out string badge,out int verify,out int check,out int y,out int m,out int d,out int h,out int n,out int s,ref int work)
 {var row=Next();badge=row?.PersonIdentifier??"";verify=row?.VerifyMode??0;check=row?.CheckType??0;y=row?.Year??0;m=row?.Month??0;d=row?.Day??0;h=row?.Hour??0;n=row?.Minute??0;s=row?.Second??0;work=row?.WorkCode??0;return row!=null;}
 public bool GetGeneralLogData(int machine,ref int terminal,ref int badge,ref int enrollment,ref int verify,ref int check,ref int y,ref int m,ref int d,ref int h,ref int n)
 {var row=Next();if(row is null)return false;terminal=row.LegacyTerminalNumber!.Value;enrollment=row.LegacyEnrollmentMachineNumber!.Value;badge=int.Parse(row.PersonIdentifier!,System.Globalization.CultureInfo.InvariantCulture);verify=row.VerifyMode;check=row.CheckType;y=row.Year;m=row.Month;d=row.Day;h=row.Hour;n=row.Minute;return true;}
 public bool ClearData(int machine,int flag){Writes++;LastFlag=flag;LastMachine=machine;if(KeepOrphans)orphans=Entries.ToArray();if(!KeepUsers)Entries=[];if(!KeepLogs)Logs=[];if(ThrowWrite)throw new InvalidOperationException();return !ReturnFalse;}
 public void Disconnect(){}
 public bool SetCommPassword(int key)=>true;
 public bool Connect_Net(string address,int port){Reconnections++;return ReconnectOk;}
 public new bool GetUserTmpExStr(int machine,string person,int slot,out int flag,out string data,out int length)
 {var f=orphans.SingleOrDefault(e=>e.User.PersonIdentifier==person)?.Fingerprints[slot];if(f is null)return base.GetUserTmpExStr(machine,person,slot,out flag,out data,out length);flag=f.Flag;data=f.Data;length=f.NativeLength;return true;}
 public new bool GetUserTmpStr(int machine,int person,int slot,ref string data,ref int length){var ok=GetUserTmpExStr(machine,person.ToString(System.Globalization.CultureInfo.InvariantCulture),slot,out _,out var d,out var n);data=d;length=n;return ok;}
}
