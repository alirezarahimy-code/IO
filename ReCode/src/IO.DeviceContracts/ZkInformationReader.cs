namespace IO.DeviceContracts;

/// <summary>Read-only reference-derived capacity mapping. No shared ref-value fallback,
/// vendor claim, mutation or atomic-snapshot promise.</summary>
public interface IZkInformationSession
{
    string? Serial();
    string? Product();
    string? Firmware();
    string? Mac();
    string? ClockLocal();
    int? Status(int code);
}
public static class ZkInformationReader
{
    public static ZkOperationOutput Read(string expectedSerial,bool clockConfigured,IZkInformationSession session)
    {
        string? ReadText(Func<string?> read,int max){try{var value=read();return value is not null&&ZkIdlePlan.Text(value,max)?value:null;}catch{return null;}}
        int? Status(int code){try{var n=session.Status(code);return n is >=0?n:null;}catch{return null;}}
        if(!ZkIdlePlan.Text(expectedSerial,100)||ReadText(session.Serial,100)!=expectedSerial)return new("identity-mismatch");
        var product=ReadText(session.Product,120);var firmware=ReadText(session.Firmware,120);var mac=ReadText(session.Mac,100);var clock=ReadText(session.ClockLocal,19);
        if(!ZkOperationInput.ValidClock(clock))clock=null;
        var information=new ZkDeviceInformation(expectedSerial,product,firmware,mac,clock,Status(6),Status(9),Status(2),Status(8),Status(1),Status(3),Status(7),Status(21),Status(22),clockConfigured);
        if(ReadText(session.Serial,100)!=expectedSerial)return new("identity-mismatch");
        return new("succeeded",Information:information);
    }
}
