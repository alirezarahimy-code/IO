using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

/// <summary>Dedicated small NTFS volume, protected root and EFS directories. Not a claim of secure erasure on SSD/backups.</summary>
public sealed record BiometricPhotoStaging(string Root,string StoragePolicyReference,long MaximumVolumeBytes=268435456,bool Enable=false)
{
 public override string ToString()=>"Biometric photo storage [private deployment paths redacted]";
}
public sealed record BiometricPhotoWorkspace(string Directory,Guid LeaseId,DateTimeOffset NotAfterUtc,string StoragePolicyReference,long MaximumVolumeBytes)
{
 public bool IsValid(string reference)=>LeaseId!=Guid.Empty&&Path.IsPathFullyQualified(Directory)&&Path.GetFileName(Directory)=="job-"+LeaseId.ToString("N")&&StoragePolicyReference==reference&&NotAfterUtc.Offset==TimeSpan.Zero&&MaximumVolumeBytes is >=33554432 and <=1073741824;
 public override string ToString()=>"Private photo workspace [path redacted]";
}
/// <summary>Small, baseline single-scan JPEG profile; no decoder, conversion, EXIF modification or unbounded dimensions.</summary>
public static class BiometricJpeg
{
 public static bool IsValid(ReadOnlySpan<byte> b,int maxBytes)
 {
  if(b.Length<16||b.Length>maxBytes||b[0]!=255||b[1]!=216)return false;
  int p=2;bool frame=false;
  while(p<b.Length)
  {
   if(b[p++]!=255)return false;while(p<b.Length&&b[p]==255)p++;
   if(p>=b.Length)return false;int marker=b[p++];
   if(marker is 0 or 216 or 217||marker is >=208 and <=215||p+2>b.Length)return false;
   int len=(b[p]<<8)|b[p+1];if(len<2||p+len>b.Length)return false;
   if(marker==192)
   {
    if(frame||len<11||b[p+2]!=8)return false;int h=(b[p+3]<<8)|b[p+4],w=(b[p+5]<<8)|b[p+6],components=b[p+7];
    if(h is <1 or >4096||w is <1 or >4096||(long)w*h>16777216||components is not(1 or 3)||len!=8+3*components)return false;
    frame=true;
   }
   else if(marker is >=193 and <=207 && marker is not(196 or 200 or 204))return false;
   if(marker==218)
   {
    if(!frame||len<6)return false;int components=b[p+2];if(components is not(1 or 3)||len!=6+2*components||b[p+len-3]!=0||b[p+len-2]!=63||b[p+len-1]!=0)return false;
    p+=len;
    while(p<b.Length)
    {
     if(b[p++]!=255)continue;if(p>=b.Length)return false;int next=b[p++];
     if(next==0||next is >=208 and <=215)continue;
     return next==217&&p==b.Length;
    }
    return false;
   }
   p+=len;
  }
  return false;
 }
}

/// <summary>Parent owns root lock; child owns .child.lock. Unknown/young/active orphan blocks new staging, never silently discarded.</summary>
[SupportedOSPlatform("windows")]
public sealed class BiometricPhotoLease:IDisposable
{
 private readonly FileStream rootLock;
 public BiometricPhotoWorkspace Workspace {get;}
 public bool Cleaned {get;private set;}
 private BiometricPhotoLease(FileStream rootLock,BiometricPhotoWorkspace workspace){this.rootLock=rootLock;Workspace=workspace;}
 public static BiometricPhotoLease Create(BiometricPhotoStaging config,int timeoutSeconds)
 {
  if(!config.Enable||timeoutSeconds is <1 or >60||!Text(config.StoragePolicyReference)||!Path.IsPathFullyQualified(config.Root))throw new InvalidOperationException("Explicit protected photo staging required.");
  var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(config.Root));ValidateStorage(root,config.MaximumVolumeBytes);
  if(File.Exists(Path.Combine(root,".owner.lock"))&&(File.GetAttributes(Path.Combine(root,".owner.lock"))&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("No redirected staging lock.");
  var owner=new FileStream(Path.Combine(root,".owner.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  try
  {
   foreach(var file in System.IO.Directory.EnumerateFiles(root))if(Path.GetFileName(file)!=".owner.lock")throw new InvalidOperationException("Unrecognized photo staging content; administrator review required.");
   foreach(var dir in System.IO.Directory.EnumerateDirectories(root))
   {
    if(!ValidDirectoryName(dir)||(File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Unrecognized photo staging directory.");
    var marker=Path.Combine(dir,"lease.json");
    ValidatePrivate(dir);
    if(!File.Exists(marker)||(File.GetAttributes(marker)&FileAttributes.ReparsePoint)!=0||new FileInfo(marker).Length>4096)throw new InvalidOperationException("Incomplete photo staging; administrator cleanup required.");
    var old=System.Text.Json.JsonSerializer.Deserialize<BiometricPhotoWorkspace>(File.ReadAllText(marker));
    if(old is null||old.Directory!=dir||!old.IsValid(config.StoragePolicyReference)||old.NotAfterUtc.AddMinutes(2)>DateTimeOffset.UtcNow)throw new InvalidOperationException("Photo staging still leased or requires review.");
    ValidateLock(dir);
    using(var child=new FileStream(Path.Combine(dir,".child.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){}
    DeletePrivate(dir);
   }
   var id=Guid.NewGuid();var path=Path.Combine(root,"job-"+id.ToString("N"));System.IO.Directory.CreateDirectory(path);Protect(path);File.Encrypt(path);
   if((File.GetAttributes(path)&FileAttributes.Encrypted)==0)throw new InvalidOperationException("EFS photo staging required.");
   var workspace=new BiometricPhotoWorkspace(path,id,DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds+15),config.StoragePolicyReference,config.MaximumVolumeBytes);
   using(var marker=new FileStream(Path.Combine(path,"lease.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
   {marker.Write(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(workspace));marker.Flush(true);}
   return new(owner,workspace);
  }
  catch{owner.Dispose();throw;}
 }
 public static FileStream OpenChild(BiometricPhotoWorkspace w,string reference)
 {
  if(!w.IsValid(reference)||DateTimeOffset.UtcNow>=w.NotAfterUtc||w.NotAfterUtc>DateTimeOffset.UtcNow.AddSeconds(80))throw new InvalidOperationException("Expired photo workspace.");
  ValidateStorage(Path.GetDirectoryName(w.Directory)!,w.MaximumVolumeBytes);ValidatePrivate(w.Directory);
  if((File.GetAttributes(w.Directory)&FileAttributes.Encrypted)==0)throw new InvalidOperationException("Encrypted private workspace required.");
  ValidateLock(w.Directory);
  return new(Path.Combine(w.Directory,".child.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
 }
 public static void ValidateStorage(string root,long maxVolumeBytes)
 {
  if(!System.IO.Directory.Exists(root)||maxVolumeBytes is <33554432 or >1073741824||Path.TrimEndingDirectorySeparator(root)==Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!))throw new InvalidOperationException("Existing private directory on a dedicated bounded volume required.");
  var drive=new DriveInfo(Path.GetPathRoot(root)!);
  if(!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(root)!),Path.TrimEndingDirectorySeparator(drive.Name),StringComparison.OrdinalIgnoreCase)||!drive.IsReady||drive.DriveFormat!="NTFS"||drive.TotalSize>maxVolumeBytes||drive.TotalSize<33554432||Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!.Equals(drive.Name,StringComparison.OrdinalIgnoreCase)||drive.AvailableFreeSpace<4194304)throw new InvalidOperationException("Dedicated small non-system NTFS volume with capacity required.");
  ValidatePrivate(root);
 }
 private static void Protect(string dir)
 {
  var sid=WindowsIdentity.GetCurrent().User??throw new InvalidOperationException();var security=new DirectorySecurity();security.SetOwner(sid);security.SetAccessRuleProtection(true,false);
  security.AddAccessRule(new(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
  security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
  new DirectoryInfo(dir).SetAccessControl(security);
 }
 private static void ValidatePrivate(string dir)
 {
  if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("No redirected photo staging.");
  var me=WindowsIdentity.GetCurrent().User??throw new InvalidOperationException();var security=new DirectoryInfo(dir).GetAccessControl();
  if(!security.AreAccessRulesProtected)throw new InvalidOperationException("Protected non-inheriting photo root ACL required.");
  if(security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner||!owner.Equals(me))throw new InvalidOperationException("Photo root must be owned by service identity.");
  foreach(FileSystemAccessRule rule in security.GetAccessRules(true,true,typeof(SecurityIdentifier)))
   if(rule.AccessControlType==AccessControlType.Allow&&!rule.IdentityReference.Equals(me)&&!rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)))throw new InvalidOperationException("Photo storage ACL must allow only service identity and SYSTEM.");
 }
 private static void ValidateLock(string dir){var path=Path.Combine(dir,".child.lock");if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("No redirected child lock.");}
 private static bool ValidDirectoryName(string path)=>Path.GetFileName(path).StartsWith("job-",StringComparison.Ordinal)&&Guid.TryParseExact(Path.GetFileName(path)[4..],"N",out _);
 private static bool Text(string s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=200&&!s.Any(char.IsControl);
 private static void DeletePrivate(string dir)
 {
  ValidatePrivate(dir);
  if(System.IO.Directory.EnumerateDirectories(dir).Any())throw new InvalidOperationException("Unexpected nested photo content; no recursive purge.");
  var files=System.IO.Directory.EnumerateFiles(dir).Take(5).ToArray();
  if(files.Length>4)throw new InvalidOperationException("Unexpected photo file count requires review.");
  foreach(var path in files)
  {
   var name=Path.GetFileName(path);
   if(name is not("lease.json" or ".child.lock")&&!(name.EndsWith(".jpg",StringComparison.Ordinal)&&name.Length is >=5 and <=24&&name[..^4].All(char.IsAsciiDigit)))throw new InvalidOperationException("Unexpected photo content requires review.");
   if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("No redirected photo file.");
  }
  foreach(var path in files)File.Delete(path);
  System.IO.Directory.Delete(dir);
 }
 public void Dispose()
 {
  try{ValidateLock(Workspace.Directory);using(var child=new FileStream(Path.Combine(Workspace.Directory,".child.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){}DeletePrivate(Workspace.Directory);Cleaned=true;}
  catch{Cleaned=false;}finally{rootLock.Dispose();}
 }
}

public interface IBiometricPhotoSdk
{
 bool Download(string fixedName,string privateDirectory);
 bool Upload(string privatePath);
 int LastError();
}
/// <summary>Only fixed decimal .jpg names, EFS inherited files, bounded content; production callers live inside the isolated bridge.</summary>
[SupportedOSPlatform("windows")]
public sealed class BiometricPhotoFiles(BiometricPhotoWorkspace workspace,bool source,IBiometricPhotoSdk sdk):IDisposable
{
 private readonly List<byte[]> buffers=[];
 private string PathFor(string person)
 {
  if(DateTimeOffset.UtcNow>=workspace.NotAfterUtc||person.Length is <1 or >20||!person.All(char.IsAsciiDigit))throw new InvalidOperationException("Invalid or expired fixed photo identity.");
  var path=Path.Combine(workspace.Directory,person+".jpg");
  if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("No redirected photo file.");
  return path;
 }
 public BiometricSlot Read(string person,BiometricTransferApproval approval)
 {
  if(approval.Kind!="photo"||!approval.IsValid())return new("unavailable");
  var path=PathFor(person);if(File.Exists(path))File.Delete(path);
  bool found=sdk.Download(person+".jpg",workspace.Directory+Path.DirectorySeparatorChar);int error=found?int.MinValue:sdk.LastError();
  if(!found)return !File.Exists(path)&&error==(source?approval.SourceAbsentError:approval.TargetAbsentError)?new("absent"):new("unavailable");
  if(!File.Exists(path)||(File.GetAttributes(path)&(FileAttributes.ReparsePoint|FileAttributes.Encrypted))!=FileAttributes.Encrypted||new FileInfo(path).Length>approval.MaximumNativeLength)return new("unavailable");
  byte[] bytes;
  using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))
  {
   if(file.Length is <16||file.Length>approval.MaximumNativeLength)return new("unavailable");
   bytes=new byte[(int)file.Length];buffers.Add(bytes);file.ReadExactly(bytes);
   if(file.ReadByte()!=-1)return new("unavailable");
  }
  var material=new BiometricMaterial("",bytes.Length,0,bytes);
  return material.IsValid(approval)?new("present",material):new("unavailable");
 }
 public bool Write(string person,BiometricTransferApproval approval,BiometricMaterial material)
 {
  if(source||approval.Kind!="photo"||!material.IsValid(approval))return false;
  var path=PathFor(person);if(File.Exists(path))File.Delete(path);
  using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,8192,FileOptions.WriteThrough)){file.Write(material.Photo!);file.Flush(true);}
  if((File.GetAttributes(path)&FileAttributes.Encrypted)==0)throw new InvalidOperationException("Encrypted photo file required.");
  return sdk.Upload(path);
 }
 public void Dispose(){foreach(var buffer in buffers)System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);buffers.Clear();}
}
