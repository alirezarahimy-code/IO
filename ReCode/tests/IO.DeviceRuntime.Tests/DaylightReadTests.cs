using System.Runtime.InteropServices;
using System.Text.Json;
using IO.DeviceContracts;
internal static class DaylightReadTests
{
    public static async Task RunAsync()
    {
        int count=0;void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);Console.WriteLine("PASS daylight-read "+name);count++;}
        var approval=new ZkDaylightReadApproval("daylight-product","daylight-fw","synthetic-daylight-read");
        var pin=new ZkProbeInput(Guid.NewGuid(),"10.20.30.40",4370,1,"daylight-fixture",0);
        var input=new ZkOperationInput(pin,"settings.read",null,false,true,DaylightReadApproval:approval);
        var snapshot=new ZkAdvancedSettings(ZkAdvancedSettings.Version,pin.ExpectedSerial,1,2,3,4,5,6,approval.Product,approval.Firmware);
        Check(input.IsValid()&&(input with{DaylightReadApproval=null}).IsValid(),"read opt-in keeps old snapshots valid and default has no daylight request");
        Check(!(input with{AllowAdvancedSettingsRead=false}).IsValid()&&!(input with{Operation="device.restart",AllowRestart=true}).IsValid()&&!(input with{Operation="clock.set",AllowClockSet=true,ClockLocal="2026-11-08 10:00:00"}).IsValid(),"approval cannot escape read into clock or restart dispatch");
        Check(new[]{approval with{Product=" product"},approval with{Firmware="\uD800"},approval with{EvidenceReference=""}}.All(x=>!x.IsValid()),"exact descriptors and bounded reference required");
        var session=new Session();var observed=ZkDaylightReader.Add(snapshot,approval,session)!;
        Check(session.Calls==1&&observed.IsValid()&&observed.SleepTime==4&&observed.Daylight?.Matches(approval)==true,"one getter preserves six existing values and binds read approval");
        Check(observed.Daylight!.Value==session.Value&&observed.Daylight.Value is {Support:int.MinValue,BeginTime:" raw;begin ",EndTime:"<img src=x>"},"signed support and opaque strings preserved without date timezone or boolean coercion");
        session=new();Check(ZkDaylightReader.Add(snapshot with{Product=null},approval,session) is null&&ZkDaylightReader.Add(snapshot with{Firmware="other"},approval,session) is null&&session.Calls==0,"missing or different live model calls no getter");
        session=new(){Value=null};observed=ZkDaylightReader.Add(snapshot,approval,session)!;
        Check(observed.Daylight is {Reported:false,Value:null}&&observed.IsValid(),"SDK false yields unavailable and never initialized zeroes");
        session=new(){Throw=true};Check(ZkDaylightReader.Add(snapshot,approval,session)!.Daylight is {Reported:false,Value:null},"native exception is unavailable without suppressing other settings");
        foreach(var value in new[]{new ZkDaylightValue(0,null!,""),new(0,"\uD800",""),new(0,"","line\nbreak"),new(0,new string('x',129),"")})
            Check(ZkDaylightReader.Add(snapshot,approval,new Session{Value=value})!.Daylight is {Reported:false,Value:null},"malformed native string unavailable without trimming or truncating "+count);
        Check(new ZkDaylightValue(0,"", " ").IsValid()&&new ZkDaylightValue(2,new string('ژ',128),new string('ژ',128)).IsValid(),"empty whitespace maximum Unicode and unknown support remain raw");
        observed=ZkDaylightReader.Add(snapshot,approval,new Session())!;var output=new ZkOperationOutput("succeeded",Settings:observed);
        Check(output.IsValidFor(input)&&!output.IsValidFor(input with{DaylightReadApproval=null})&&!(output with{State="failed"}).IsValidFor(input),"unsolicited or mixed failure evidence rejected");
        Check(!(output with{Settings=observed with{Firmware="other"}}).IsValidFor(input)&&!output.IsValidFor(input with{DaylightReadApproval=approval with{EvidenceReference="other"}}),"child output pins exact firmware and approval reference");
        Check(!new ZkOperationOutput("succeeded",Settings:snapshot).IsValidFor(input)&&!(observed with{Daylight=observed.Daylight! with{Reported=false}}).IsValid(),"missing requested observation and contradictory availability cannot certify read");
        bool missing=false;try{JsonSerializer.Deserialize<ZkDaylightValue>("{\"Support\":0,\"BeginTime\":\"\"}");}catch(JsonException){missing=true;}
        Check(missing,"incomplete JSON cannot fabricate missing raw end time");
        var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(DaylightReadTests).Assembly.Location;
        Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input,5)).IsValidFor(input),"real child roundtrip preserves typed raw daylight without SDK execution");
        Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="daylight-missing"}},5)).State=="bridge-failed","old child missing requested daylight fails closed");
        Check((await NativeProcessSupervisor.RunOperationAsync(host,assembly,input with{Connection=pin with{ExpectedSerial="settings-hang"}},1)).State=="timed-out","hung read terminates at external process deadline");
        var combined=input with{Connection=pin with{ExpectedSerial="function-large"},DaylightReadApproval=approval with{Product="function-product",Firmware="function-fw"},FunctionKeyReadApproval=new("function-product","function-fw",Enumerable.Range(0,8).ToArray(),"synthetic-combined-keys")};
        var combinedOutput=await NativeProcessSupervisor.RunOperationAsync(host,assembly,combined,5);
        Check(combinedOutput.IsValidFor(combined)&&combinedOutput.Settings?.FunctionKeys?.Items.Length==8&&combinedOutput.Settings.Daylight?.Value?.BeginTime.Length==128&&JsonSerializer.Serialize(combinedOutput).Length>8192,"maximum Unicode daylight and eight key tuples coexist in the same bounded real child snapshot without truncation");
        Console.WriteLine($"PASS {count} daylight read checks; synthetic sessions and real child, NOT hardware acceptance");
    }
    internal sealed class Session:IZkDaylightReadSession
    {
        public int Calls;public bool Throw;public ZkDaylightValue? Value=new(int.MinValue," raw;begin ","<img src=x>");
        public ZkDaylightValue? ReadDaylight(){Calls++;if(Throw)throw new IOException();return Value;}
    }
}
