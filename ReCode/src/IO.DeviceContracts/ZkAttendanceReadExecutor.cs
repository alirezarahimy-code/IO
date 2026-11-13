using System.Globalization;
namespace IO.DeviceContracts;

public static class ZkAttendanceReadFormats
{
    public const string Ssr="ssr-v1",Legacy="legacy-int32-minute-v1";
    public static bool IsKnown(string? value)=>value is Ssr or Legacy;
    public static bool Matches(ZkAttendanceRaw row,string format)=>row.IsBounded()&&(format switch
    {
        Ssr=>row.LegacyTerminalNumber is null&&row.LegacyEnrollmentMachineNumber is null,
        Legacy=>row.LegacyTerminalNumber is not null&&row.LegacyEnrollmentMachineNumber is not null&&row.Second==0&&row.WorkCode==0&&
            int.TryParse(row.PersonIdentifier,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var number)&&row.PersonIdentifier==number.ToString(CultureInfo.InvariantCulture),
        _=>false
    });
    public static string ExternalPrefix(string format)=>format switch{Ssr=>"zk-ssr-v1:",Legacy=>"zk-legacy-int32-minute-v1:",_=>throw new ArgumentException("Unknown attendance format.")};
}
public interface IZkAttendanceReadSession
{
    string? Serial();
    int? RecordCount();
    bool Begin();
    ZkAttendanceRaw? Next(string format);
}
/// <summary>Reference ReadAllGLogData + explicit SSR/GetGeneralLogData branch.
/// No guessed fallback, clear, disable, or native date filtering.</summary>
public static class ZkAttendanceReadExecutor
{
    public static void Execute(ZkAttendanceReadInput input,IZkAttendanceReadSession session,Action<ZkAttendanceFrame> emit)
    {
        long count=0;
        void End(string state)=>emit(new("end",Count:count,State:state));
        if(!input.IsValid()){End("unsupported");return;}
        try
        {
            if(session.Serial()!=input.Connection.ExpectedSerial){End("identity-mismatch");return;}
            var before=session.RecordCount();if(before is null or <0||!session.Begin()){End("failed");return;}
            emit(new("start",Serial:input.Connection.ExpectedSerial,ReadFormat:input.ReadFormat));
            while(session.Next(input.ReadFormat)is{} row)
            {
                if(!ZkAttendanceReadFormats.Matches(row,input.ReadFormat)){End("incomplete");return;}
                emit(new("record",Record:row));count++;
            }
            var after=session.RecordCount();
            End(session.Serial()!=input.Connection.ExpectedSerial?"failed":before==after&&after==count?"succeeded":"incomplete");
        }
        catch{End("failed");}
    }
}
