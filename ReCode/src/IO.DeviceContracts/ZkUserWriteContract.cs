using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

/// <summary>Ordinary attendance-only metadata, never passwords/templates/admin privileges or access groups.</summary>
public sealed record ZkUserWriteInput(ZkProbeInput Connection,bool WriteUser,bool AllowUserWrite,ZkUser? Expected,ZkUser Desired)
{
    public bool IsValid()=>Connection?.IsValid()==true&&WriteUser&&AllowUserWrite&&Valid(Desired)&&(Expected is null||(Valid(Expected)&&Expected.PersonIdentifier==Desired.PersonIdentifier));
    private static bool Valid(ZkUser? u)=>u?.IsValid()==true&&u.Privilege==0&&u.Name.Length is >0 and <=20&&u.PersonIdentifier.All(c=>c is >='0' and <='9')&&u.Card is not null&&u.Card.Length<=20&&u.Card.All(c=>c is >='0' and <='9');
    public override string ToString()=>"ZkUserWriteInput [private IPC; personal data and key redacted]";
}
public sealed record ZkUserWriteOutput(string State)
{
    public bool IsValid()=>State is "succeeded" or "unchanged" or "conflict" or "unsupported" or "identity-mismatch" or "failed-before-write" or "unknown";
}
/// <summary>Transient SDK memory only. Never persisted or emitted by the bridge.</summary>
public sealed record ZkUserWriteExisting(ZkUser User,[property:JsonIgnore]string Password)
{
    public override string ToString()=>"ZkUserWriteExisting [private SDK memory; redacted]";
}
public interface IZkUserWriteSession
{
    // Must fail rather than interpreting an incomplete enumeration as absence.
    ZkUserWriteExisting? ReadExact(string personIdentifier);
    bool SetCard(string card);
    bool Write(ZkUser desired,string preservedPassword);
}
public static class ZkUserWriteExecutor
{
    public static ZkUserWriteOutput Execute(ZkUserWriteInput input,IZkUserWriteSession session)
    {
        if(!input.IsValid())return new("unsupported");bool mutation=false;
        try
        {
            var current=session.ReadExact(input.Desired.PersonIdentifier);
            if(current?.User!=input.Expected)return new("conflict");
            if(current?.User==input.Desired)return new("unchanged");
            // No password comes from web/DB. Existing password stays only in this
            // native child's memory; new-user creation deliberately has no password.
            mutation=true;
            if(!session.SetCard(input.Desired.Card!)||!session.Write(input.Desired,current?.Password??""))return new("unknown");
            var observed=session.ReadExact(input.Desired.PersonIdentifier);
            return new(observed?.User==input.Desired&&observed.Password==(current?.Password??"")?"succeeded":"unknown");
        }
        catch{return new(mutation?"unknown":"failed-before-write");}
    }
}
