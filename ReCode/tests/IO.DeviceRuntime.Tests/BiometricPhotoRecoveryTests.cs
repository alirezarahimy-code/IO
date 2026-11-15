using System.Text.Json;
using IO.DeviceContracts;
internal static class BiometricPhotoRecoveryTests
{
 internal static byte[] Jpeg()=>Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAACAAIDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDiKKKK9g8s/9k=");
 internal static async Task RunAsync(BiometricTransferInput input,string host,string assembly,Action<bool,string> check)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Photo acceptance requires Windows and actual EFS.");
  var a=input.Approval;var observe=input with{InspectOnly=true};var material=new BiometricMaterial("SYNTHETIC",9,1);
  foreach(var (slot,state) in new[]{(new BiometricSlot("present",material),"observed-equal"),(new BiometricSlot("present",new("OTHER",5,1)),"observed-different"),(new BiometricSlot("absent"),"target-absent"),(new BiometricSlot("unavailable"),"inspection-failed")})
  {
   var source=new ReadOnlySession(a.Source,new("present",material));var target=new ReadOnlySession(a.Target,slot);
   check(BiometricTransferExecutor.Observe(observe,source,target).State==state&&source.Writes+target.Writes==0,"read-only unknown observation "+state+" never invokes a setter");
  }
  var readonlySource=new ReadOnlySession(a.Source,new("present",material));var readonlyTarget=new ReadOnlySession(a.Target,new("absent"));
  check(BiometricTransferExecutor.Execute(observe,readonlySource,readonlyTarget).State=="unsupported"&&readonlyTarget.Writes==0,"inspection cannot enter ordinary transfer executor");
  check(BiometricTransferExecutor.Observe(input,readonlySource,readonlyTarget).State=="inspection-failed","write intent cannot silently become a recovery observation");
  check((await NativeProcessSupervisor.RunBiometricTransferAsync(host,assembly,observe,5)).State=="observed-equal","distinct read-only wire returns correlated observation, not transfer completion");
  check((await NativeProcessSupervisor.RunBiometricTransferAsync(host,assembly,observe with{Approval=a with{FormatReference="SYNTHETIC-EXTRA"}},5)).State=="inspection-failed","copy success from inspection child is rejected");
  check((await NativeProcessSupervisor.RunBiometricTransferAsync(host,assembly,observe with{Approval=a with{FormatReference="SYNTHETIC-HANG"}},1)).State=="inspection-failed","hung observation child is terminated without creating unknown write");
  check(!new BiometricRecoveryApproval(a,"SYNTHETIC-RESET",10).IsValid()&&new BiometricRecoveryApproval(a,"SYNTHETIC-RESET",10,true,true).IsValid(),"non-destructive physical recovery procedure is separate and default-off");
  var jpeg=Jpeg();var photo=a with{Kind="photo",Slot=0,FingerprintFlag=0,PhotoHandlingReference="SYNTHETIC-STORAGE"};
  check(photo.IsValid()&&! (photo with{RequiresReadAllTemplate=true}).IsValid()&&!(photo with{PhotoHandlingReference=null}).IsValid(),"photo approval requires separate handling reference and never broad template reads");
  check(BiometricJpeg.IsValid(jpeg,1024)&&!BiometricJpeg.IsValid(jpeg,jpeg.Length-1)&&!BiometricJpeg.IsValid(jpeg[..^1],1024)&&!BiometricJpeg.IsValid([..jpeg,0],1024),"synthetic baseline JPEG accepts bounded frame and rejects truncation, trailing bytes and oversize");
  var invalid=jpeg.ToArray();for(int n=0;n<invalid.Length-8;n++)if(invalid[n]==255&&invalid[n+1]==192){invalid[n+5]=255;invalid[n+6]=255;break;}
  check(!BiometricJpeg.IsValid(invalid,1024),"oversized JPEG dimensions fail before any SDK upload");
  invalid=jpeg.ToArray();for(int n=0;n<invalid.Length-1;n++)if(invalid[n]==255&&invalid[n+1]==192){invalid[n+1]=194;break;}
  check(!BiometricJpeg.IsValid(invalid,1024),"unapproved progressive JPEG profile is not converted or uploaded");
  var pm=new BiometricMaterial("",jpeg.Length,0,jpeg);check(pm.IsValid(photo)&&!JsonSerializer.Serialize(pm).Contains(Convert.ToBase64String(jpeg),StringComparison.Ordinal),"photo bytes cannot accidentally serialize into metadata receipt");
  var root=Environment.GetEnvironmentVariable("IO_SYNTHETIC_PHOTO_ROOT")??throw new InvalidOperationException("Explicit disposable encrypted CI volume is required, not a temp fallback.");
  var config=new BiometricPhotoStaging(root,"SYNTHETIC-STORAGE",268435456,true);
  bool rejected=false;try{BiometricPhotoLease.Create(config with{Enable=false},5);}catch(InvalidOperationException){rejected=true;}check(rejected,"encrypted staging remains default-off even with provisioned volume");
  var lease=BiometricPhotoLease.Create(config,5);byte[]? transient;
  using(var child=BiometricPhotoLease.OpenChild(lease.Workspace,"SYNTHETIC-STORAGE"))
  {
   var sdk=new FileSdk(jpeg);using(var files=new BiometricPhotoFiles(lease.Workspace,false,sdk))
   {
    var slot=files.Read("0002",photo);transient=slot.Material?.Photo;
    check(slot.State=="present"&&sdk.Name=="0002.jpg"&&transient?.SequenceEqual(jpeg)==true,"production file adapter reads fixed leading-zero JPG via fake SDK on actual EFS");
    check(files.Write("0002",photo,pm)&&sdk.Uploads==1,"production photo upload requires actual encrypted file and bounded JPEG");
    sdk.BadBytes=true;check(files.Read("0002",photo).State=="unavailable","malformed photo bytes downloaded onto EFS never become transferable material");sdk.BadBytes=false;
    sdk.Decrypt=true;check(files.Read("0002",photo).State=="unavailable","unencrypted SDK output is refused even on the private volume");sdk.Decrypt=false;
    sdk.Found=false;check(files.Read("0002",photo).State=="absent","only exact approved SDK absence with no file is absence");
    sdk.Partial=true;check(files.Read("0002",photo).State=="unavailable","false SDK download with partial file cannot fabricate absence");
    rejected=false;try{files.Read("../0002",photo);}catch(InvalidOperationException){rejected=true;}check(rejected,"production photo path rejects traversal before invoking SDK");
   }
   check(transient is not null&&transient.All(x=>x==0),"child-owned transient JPEG read buffer is zeroed at session disposal");
   lease.Dispose();check(!lease.Cleaned&&Directory.Exists(lease.Workspace.Directory),"cleanup refuses to purge workspace while child lock is active");
  }
  rejected=false;try{BiometricPhotoLease.Create(config,5);}catch(InvalidOperationException){rejected=true;}check(rejected,"young orphan blocks new staging instead of silently deleting retained material");
  var old=lease.Workspace with{NotAfterUtc=DateTimeOffset.UtcNow.AddMinutes(-3)};File.WriteAllText(Path.Combine(old.Directory,"lease.json"),JsonSerializer.Serialize(old));
  var replacement=BiometricPhotoLease.Create(config,5);check(!Directory.Exists(old.Directory),"expired unlocked orphan is removed before admitting another photo job");replacement.Dispose();check(replacement.Cleaned,"normal encrypted workspace cleanup removes metadata and photo files");
  var pi=input with{Approval=photo};
  check((await NativeProcessSupervisor.RunBiometricTransferAsync(host,assembly,pi,5,photoStaging:config)).State=="copied"&&!Directory.EnumerateDirectories(root).Any(),"production supervisor provisions EFS staging and cleans it after synthetic child exit");
  check((await NativeProcessSupervisor.RunBiometricTransferAsync(host,assembly,pi with{Approval=photo with{FormatReference="SYNTHETIC-HANG"}},1,photoStaging:config)).State=="unknown"&&!Directory.EnumerateDirectories(root).Any(),"supervisor kills timed-out photo child before deleting private staging, never retries write");
 }
 private sealed class ReadOnlySession(BiometricDeviceIdentity identity,BiometricSlot slot):IBiometricTransferSession
 {
  public int Writes;
  public BiometricDeviceIdentity ReadIdentity()=>identity;
  public bool OrdinaryUserExists(string person)=>true;
  public BiometricSlot Read(string person,BiometricTransferApproval approval)=>slot;
  public bool Write(string person,BiometricTransferApproval approval,BiometricMaterial material){Writes++;throw new InvalidOperationException("No setter permitted.");}
 }
 [System.Runtime.Versioning.SupportedOSPlatform("windows")]
 private sealed class FileSdk(byte[] jpeg):IBiometricPhotoSdk
 {
  public bool Found=true,Partial,BadBytes,Decrypt;public int Uploads;public string? Name;
  public bool Download(string name,string directory){Name=name;var path=Path.Combine(directory,name);if(Found||Partial){File.WriteAllBytes(path,BadBytes?[1,2,3]:jpeg);if(Decrypt)DecryptFile(path);}return Found;}
  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static void DecryptFile(string path)=>File.Decrypt(path);
  public bool Upload(string path){Uploads++;return File.ReadAllBytes(path).SequenceEqual(jpeg);}
  public int LastError()=>-404;
 }
}
