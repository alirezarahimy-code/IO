using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

public sealed record BiometricDeviceIdentity(string Serial,string Product,string Firmware)
{
    public bool IsValid()=>Text(Serial)&&Text(Product)&&Text(Firmware);
    private static bool Text(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=100&&!s.Any(char.IsControl);
    public override string ToString()=>"Biometric device identity [redacted]";
}
/// <summary>Exact pair/model/format/legal approval, never a browser-created capability. No format conversion or overwrite.</summary>
public sealed record BiometricTransferApproval(BiometricDeviceIdentity Source,BiometricDeviceIdentity Target,string Kind,int Slot,string FormatReference,string AuthorizationReference,string RetentionReference,string ExclusiveControlReference,int SourceAbsentError,int TargetAbsentError,int MaximumCharacters=65536,int MaximumNativeLength=65536,int FingerprintFlag=1,bool RequiresReadAllTemplate=false,bool AllowTransfer=false)
{
    public bool IsValid()=>AllowTransfer&&Source?.IsValid()==true&&Target?.IsValid()==true&&Source.Serial!=Target.Serial&&Kind is "fingerprint" or "face"&&
        (Kind=="fingerprint"?Slot is >=0 and <=9&&FingerprintFlag is 0 or 1:Slot==15&&FingerprintFlag==0)&&
        Text(FormatReference)&&Text(AuthorizationReference)&&Text(RetentionReference)&&Text(ExclusiveControlReference)&&
        SourceAbsentError is not 0 and not int.MinValue&&TargetAbsentError is not 0 and not int.MinValue&&MaximumCharacters is >=1 and <=1048576&&MaximumNativeLength is >=1 and <=1048576;
    private static bool Text(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=200&&!s.Any(char.IsControl);
    public override string ToString()=>"Exact biometric pair/format approval [policy references redacted]";
}
public sealed record BiometricTransferInput(ZkProbeInput Source,ZkProbeInput Target,Guid OperationId,string SourcePerson,string TargetPerson,BiometricTransferApproval Approval,bool TransferBiometric=true)
{
    public bool IsValid()=>TransferBiometric&&Source?.IsValid()==true&&Target?.IsValid()==true&&Source.DeviceId!=Target.DeviceId&&OperationId!=Guid.Empty&&Person(SourcePerson)&&Person(TargetPerson)&&Approval?.IsValid()==true&&Source.ExpectedSerial==Approval.Source.Serial&&Target.ExpectedSerial==Approval.Target.Serial;
    private static bool Person(string? s)=>s is{Length:>=1 and <=100}&&s.All(char.IsAsciiDigit);
    public override string ToString()=>"Biometric transfer IPC [subjects, credentials and policy redacted]";
}
/// <summary>Transient child memory only. Never serializable, logged, hashed into a receipt or persisted.</summary>
public sealed class BiometricMaterial(string data,int nativeLength,int flag)
{
    [JsonIgnore] public string Data {get;}=data;
    [JsonIgnore] public int NativeLength {get;}=nativeLength;
    [JsonIgnore] public int Flag {get;}=flag;
    public bool IsValid(BiometricTransferApproval approval)=>Data is{Length:>0}&&Data.Length<=approval.MaximumCharacters&&Data.All(c=>c is >=(char)33 and <=(char)126)&&NativeLength is >0&&NativeLength<=approval.MaximumNativeLength&&(approval.Kind=="fingerprint"?Flag==approval.FingerprintFlag:Flag==0);
    public bool Same(BiometricMaterial? other)=>other is not null&&Data==other.Data&&NativeLength==other.NativeLength&&Flag==other.Flag;
    public override string ToString()=>"Biometric material [never disclose]";
}
public sealed record BiometricSlot(string State,[property:JsonIgnore]BiometricMaterial? Material=null)
{
    public override string ToString()=>"Biometric slot observation [no material disclosed]";
}
public interface IBiometricTransferSession
{
    BiometricDeviceIdentity ReadIdentity();
    // Existing exact ordinary user only: no user creation, password/admin/door writes.
    bool OrdinaryUserExists(string person);
    BiometricSlot Read(string person,BiometricTransferApproval approval);
    bool Write(string person,BiometricTransferApproval approval,BiometricMaterial material);
}
public sealed record BiometricTransferOutput(Guid OperationId,string State)
{
    public bool IsValidFor(BiometricTransferInput input)=>OperationId==input.OperationId&&Known(State);
    public static bool Known(string state)=>state is "copied" or "unchanged" or "existing-slot-refused" or "source-empty" or "source-changed" or "presence-unavailable" or "identity-mismatch" or "user-unavailable" or "failed-before-write" or "unknown" or "unsupported";
    public override string ToString()=>"Biometric transfer result [metadata only]";
}
/// <summary>No conversion, broad template enumeration, overwrite/delete, enrollment or raw material in output.</summary>
public static class BiometricTransferExecutor
{
    public static BiometricTransferOutput Execute(BiometricTransferInput input,IBiometricTransferSession source,IBiometricTransferSession target)
    {
        BiometricTransferOutput Result(string state)=>new(input.OperationId,state);
        if(!input.IsValid())return Result("unsupported");bool dispatched=false;
        try
        {
            var a=input.Approval;
            if(source.ReadIdentity()!=a.Source||target.ReadIdentity()!=a.Target)return Result("identity-mismatch");
            if(!source.OrdinaryUserExists(input.SourcePerson)||!target.OrdinaryUserExists(input.TargetPerson))return Result("user-unavailable");
            var original=source.Read(input.SourcePerson,a);
            if(original.State=="absent"&&original.Material is null)return Result("source-empty");
            if(original.State!="present"||original.Material?.IsValid(a)!=true)return Result("presence-unavailable");
            var current=target.Read(input.TargetPerson,a);
            if(current.State=="present")return Result(original.Material.Same(current.Material)?"unchanged":"existing-slot-refused");
            if(current.State!="absent"||current.Material is not null)return Result("presence-unavailable");
            // Re-observe just before dispatch. This is not a device-side CAS; installation must guarantee exclusive control.
            var fresh=source.Read(input.SourcePerson,a);if(fresh.State!="present"||!original.Material.Same(fresh.Material))return Result("source-changed");
            var empty=target.Read(input.TargetPerson,a);if(empty.State!="absent"||empty.Material is not null)return Result(empty.State=="present"?"existing-slot-refused":"presence-unavailable");
            dispatched=true;
            if(!target.Write(input.TargetPerson,a,original.Material))return Result("unknown");
            var observed=target.Read(input.TargetPerson,a);
            return Result(observed.State=="present"&&original.Material.Same(observed.Material)?"copied":"unknown");
        }
        catch{return Result(dispatched?"unknown":"failed-before-write");}
    }
}
