namespace IO.DeviceContracts;

/// <summary>Four fixed, read-only GetAdvanced2 / Personnel.GetSettings observations. Neither a generic option API
/// nor authority to change network/biometric algorithms. Raw strings, not compatibility.</summary>
public interface IZkAdditionalSettingsSession
{
    string? ReportedIp();
    string? AlgorithmVersionRaw();
    string? ReportedSubnetMask();
    string? ReportedGateway();
}
public static class ZkAdditionalSettingsReader
{
    public static ZkAdvancedSettings Add(ZkAdvancedSettings snapshot,IZkAdditionalSettingsSession session)
    {
        static string? Read(Func<string?> call){try{var value=call();return ZkIdlePlan.Text(value,100)?value:null;}catch{return null;}}
        return snapshot with{ReportedIp=Read(session.ReportedIp),AlgorithmVersionRaw=Read(session.AlgorithmVersionRaw),ReportedSubnetMask=Read(session.ReportedSubnetMask),ReportedGateway=Read(session.ReportedGateway)};
    }
}
