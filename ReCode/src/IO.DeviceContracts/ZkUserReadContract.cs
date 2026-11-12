namespace IO.DeviceContracts;

/// <summary>Private IPC only; never log this request.</summary>
public sealed record ZkUserReadInput(ZkProbeInput Connection,bool ReadUsers,bool AllowUserRead)
{
    public bool IsValid()=>Connection is not null&&Connection.IsValid()&&ReadUsers&&AllowUserRead;
    public override string ToString()=>"ZkUserReadInput [private IPC; values redacted]";
}
public sealed record ZkUser(string PersonIdentifier,string Name,string? Card,int Privilege,bool Enabled)
{
    public bool IsValid()=>!string.IsNullOrWhiteSpace(PersonIdentifier)&&PersonIdentifier.Length<=40&&PersonIdentifier==PersonIdentifier.Trim()&&!PersonIdentifier.Any(char.IsControl)&&
        Name is not null&&Name.Length<=200&&!Name.Any(char.IsControl)&&(Card is null||(Card.Length<=40&&!Card.Any(char.IsControl)))&&Privilege is >=0 and <=3;
    public override string ToString()=>"ZkUser [personal data redacted; no passwords or biometric templates]";
}
public sealed record ZkUserFrame(string Type,string? Serial=null,ZkUser? User=null,long? Count=null,string? State=null);
