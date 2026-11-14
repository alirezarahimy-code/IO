using IO.DeviceContracts;

// Exercises the linked PRODUCTION bridge mapper, not another IZkProfileSession implementation.
// The object standing in for COM is synthetic. No Windows COM activation or firmware is executed.
internal static class DhcpComProfileTests
{
    public static int Run()
    {
        var count=0;
        void Check(bool ok,string name)
        {
            if(!ok)throw new InvalidOperationException(name);
            Console.WriteLine("PASS profile DHCP production mapper "+name);count++;
        }
        foreach(var enabled in new[]{"0","1"})
        {
            var p=ProfileSettingTests.Plan("dhcp") with{Expected=new("dhcp",enabled=="1"?"0":"1"),Desired=new("dhcp",enabled)};
            var com=new ComDouble(p);var session=new ZkComProfileSession(com,37);
            var input=ProfileSettingTests.Input(p);input=input with{Connection=input.Connection with{MachineNumber=37}};
            Check(session.ReadProfile("dhcp")==p.Expected&&com.Calls.SequenceEqual(["GetSysOption:37:DHCP"]),enabled+" reads exact case-sensitive option and actual machine number");
            com.Calls.Clear();var result=ZkProfileExecutor.Execute(input,session);
            Check(result.State=="unknown"&&com.Writes==1&&com.Value==enabled,enabled+" single production dispatch remains unknown even after positive ACK");
            Check(com.Calls.Count(x=>x=="SetSysOption:37:DHCP:"+enabled)==1&&com.Calls.All(x=>!x.StartsWith("Refresh",StringComparison.Ordinal)),enabled+" exact raw value with no refresh restart disable or fallback");
            var inspect=input with{Operation="settings.profile.inspect",AllowProfileWrite=false};
            com.Calls.Clear();result=ZkProfileExecutor.Execute(inspect,session);
            Check(result.IsValidFor(inspect)&&result.Profile!.Value==p.Desired&&com.Writes==1&&!com.Calls.Any(x=>x.StartsWith("Set",StringComparison.Ordinal)),enabled+" later serial-pinned read-only inspection preserves exact observation");
            com=new(p){Ack=false};result=ZkProfileExecutor.Execute(input,new ZkComProfileSession(com,37));
            Check(result.State=="unknown"&&com.Writes==1&&com.Value==enabled,enabled+" false COM return after storing value does not retry");
            com=new(p){ThrowAfterWrite=true};result=ZkProfileExecutor.Execute(input,new ZkComProfileSession(com,37));
            Check(result.State=="unknown"&&com.Writes==1,enabled+" COM exception after dispatch does not retry");
            foreach(var reported in new string?[]{null,"true","false","01"," 1","1 ","2"})
            {
                com=new(p){Value=reported};result=ZkProfileExecutor.Execute(input,new ZkComProfileSession(com,37));
                Check(result.State=="failed"&&com.Writes==0,enabled+" unavailable or noncanonical preflight rejected: "+(reported??"unreported"));
            }
            com=new(p){ForeignIdentity=true};result=ZkProfileExecutor.Execute(input,new ZkComProfileSession(com,37));
            Check(result.State=="identity-mismatch"&&com.Writes==0,enabled+" wrong serial prevents option write");
        }
        var fake=new ComDouble(ProfileSettingTests.Plan("dhcp"));var adapter=new ZkComProfileSession(fake,37);
        Check(!adapter.SetProfile(new("dhcp","true"))&&!adapter.SetProfile(new("DHCP","1"))&&!adapter.SetProfile(new("DHCPOn","1"))&&fake.Writes==0,"invalid values and arbitrary alternative option names cannot reach COM");
        return count;
    }

    public sealed class ComDouble(ZkProfilePlan plan)
    {
        public List<string> Calls {get;}=[];
        public string? Value=plan.Expected.Raw;
        public int Writes;
        public bool Ack=true,ThrowAfterWrite,ForeignIdentity;
        public bool GetSerialNumber(int machine,out string text){Calls.Add("GetSerialNumber:"+machine);text=ForeignIdentity?"foreign":plan.Serial;return true;}
        public bool GetProductCode(int machine,out string text){Calls.Add("GetProductCode:"+machine);text=plan.Product;return true;}
        public bool GetFirmwareVersion(int machine,ref string text){Calls.Add("GetFirmwareVersion:"+machine);text=plan.Firmware;return true;}
        public bool GetSysOption(int machine,string option,out string text)
        {
            Calls.Add($"GetSysOption:{machine}:{option}");
            if(option!="DHCP")throw new InvalidOperationException("Unexpected option");
            text=Value??"";return Value is not null;
        }
        public void RefreshData(int machine){Calls.Add("RefreshData:"+machine);throw new InvalidOperationException("Unapproved implicit refresh");}
        public bool SetSysOption(int machine,string option,string text)
        {
            Calls.Add($"SetSysOption:{machine}:{option}:{text}");Writes++;
            if(option!="DHCP"||text is not("0" or "1"))throw new InvalidOperationException("Unexpected write");
            Value=text;if(ThrowAfterWrite)throw new IOException("Synthetic lost ACK");return Ack;
        }
    }
}
