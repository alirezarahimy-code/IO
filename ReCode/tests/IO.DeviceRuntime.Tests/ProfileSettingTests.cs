using System.Runtime.InteropServices;
using System.Text.Json;
using IO.DeviceContracts;
internal static class ProfileSettingTests
{
    internal static readonly ZkProfileEndpoint Endpoint=new("10.20.30.40",4370);
    internal static ZkProfileValue Old(string p)=>p switch{"daylight"=>new(p,"0","03010100","10010200","1","2","0"),"algorithm"=>new(p,"9"),"mask"=>new(p,"255.255.255.0"),"gateway"=>new(p,"10.20.30.1"),"dhcp"=>new(p,"0"),"mac"=>new(p,"02:00:00:00:00:01"),"port"=>new(p,"4370"),_=>throw new ArgumentException()};
    internal static ZkProfileValue New(string p)=>p switch{"daylight"=>new(p,"1","03020100","10020200","0","2","1"),"algorithm"=>new(p,"10"),"mask"=>new(p,"255.255.0.0"),"gateway"=>new(p,"10.20.30.2"),"dhcp"=>new(p,"1"),"mac"=>new(p,"02:00:00:00:00:02"),"port"=>new(p,"4371"),_=>throw new ArgumentException()};
    internal static ZkProfilePlan Plan(string p)=>new(ZkProfilePlan.Version,Guid.NewGuid(),"profile-fixture","profile-product","profile-fw","UTC",Old(p),New(p),"synthetic-effect","synthetic-profile-contract",Endpoint,p=="port"?Endpoint with{Port=4371}:Endpoint);
    internal static ZkProfileWriteApproval Approval(ZkProfilePlan p)=>new(p.Product,p.Firmware,p.TimeZoneId,p.Parameter,[p.Expected,p.Desired],p.Effect,p.ApprovalReference,p.Original==p.Recovery?[p.Original]:[p.Original,p.Recovery],EffectsReviewed:true,TemplatesPreserved:true,AnnualInterpretation:"synthetic-annual-date-mode-not-model-evidence");
    internal static ZkOperationInput Input(ZkProfilePlan p)=>new(new(Guid.NewGuid(),p.Original.Address,p.Original.Port,1,p.Serial,0),"settings.profile.set",null,false,true,ProfilePlan:p,AllowProfileWrite:true,ProfileApproval:Approval(p));
    public static async Task RunAsync()
    {
        int count=0;void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);Console.WriteLine("PASS profile "+name);count++;}
        var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var assembly=typeof(ProfileSettingTests).Assembly.Location;
        foreach(var parameter in ZkProfileParameters.All)
        {
            var p=Plan(parameter);var input=Input(p);var session=new Session(p);
            Check(p.IsValid()&&input.IsValid()&&Approval(p).Allows(p)&&JsonSerializer.Deserialize<ZkProfilePlan>(JsonSerializer.Serialize(p),new JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow})==p,parameter+" exact complete approved states endpoint and zone validate");
            Check(!(input with{AllowProfileWrite=false}).IsValid()&&!(input with{ProfileApproval=Approval(p) with{EffectsReviewed=false}}).IsValid(),parameter+" default-off flag and unreviewed effects cannot dispatch");
            Check(!Approval(p).Allows(p with{TimeZoneId="other"})&&!Approval(p).Allows(p with{ApprovalReference="other"})&&!Approval(p).Allows(p with{Effect="other"}),parameter+" installed zone reference and effect cannot be substituted");
            var result=ZkProfileExecutor.Execute(input,session);
            Check(session.Writes==1&&session.Value==p.Desired&&result.State==(ZkProfileParameters.Network(parameter)?"unknown":"succeeded")&&(result.State!="succeeded"||result.IsValidFor(input)),parameter+" one dispatch exact readback or network unknown not ACK success");
            session=new(p){SerialValue="foreign"};Check(ZkProfileExecutor.Execute(input,session).State=="identity-mismatch"&&session.Writes==0,parameter+" foreign serial cannot read or mutate");
            session=new(p){FirmwareValue="drift"};Check(ZkProfileExecutor.Execute(input,session).State=="identity-mismatch"&&session.Writes==0,parameter+" firmware drift cannot mutate");
            session=new(p){Value=p.Desired};Check(ZkProfileExecutor.Execute(input,session).State=="failed"&&session.Writes==0,parameter+" changed complete old value prevents overwrite");
            session=new(p){Ack=false};Check(ZkProfileExecutor.Execute(input,session).State=="unknown"&&session.Writes==1&&session.Value==p.Desired,parameter+" false ACK after mutation is unknown and not repeated");
            session=new(p){ThrowAfter=true};Check(ZkProfileExecutor.Execute(input,session).State=="unknown"&&session.Writes==1,parameter+" exception after dispatch stays unknown once");
            session=new(p){FailReadAfter=true};Check(ZkProfileExecutor.Execute(input,session).State=="unknown"&&session.Writes==1,parameter+" unavailable readback never turns into success");
            var inspect=input with{Operation="settings.profile.inspect",AllowProfileWrite=false,ProfileObserveRecovery=true,Connection=input.Connection with{Address=p.Recovery.Address,Port=p.Recovery.Port}};
            session=new(p){Value=p.Desired};result=ZkProfileExecutor.Execute(inspect,session);
            Check(inspect.IsValid()&&result.IsValidFor(inspect)&&session.Writes==0&&result.Profile!.Connected==p.Recovery,parameter+" fresh recovery inspection is read-only and binds exact endpoint and state");
            var child=await NativeProcessSupervisor.RunOperationAsync(host,assembly,input,5);
            Check(child.State==(ZkProfileParameters.Network(parameter)?"unknown":"succeeded"),parameter+" actual synthetic child executes typed request without native SDK");
            child=await NativeProcessSupervisor.RunOperationAsync(host,assembly,inspect,5);Check(child.IsValidFor(inspect),parameter+" actual isolated inspection preserves complete typed observation");
        }
        var daylight=Plan("daylight");Check(!(daylight with{Desired=daylight.Desired with{Mode="1"}}).IsValid()&&!(daylight with{Desired=daylight.Desired with{CurrentMode="0"}}).IsValid(),"DLST full native side effects cannot be omitted or reset behind preview");
        Check(!(Approval(daylight) with{AnnualInterpretation=null}).IsValid()&&!(Approval(Plan("algorithm")) with{TemplatesPreserved=false}).IsValid(),"annual interpretation and algorithm template-preservation evidence required");
        Check(!ZkProfileParameters.Annual("02300100")&&!ZkProfileParameters.Annual("03012400")&&!ZkProfileParameters.Annual("03-01 01:00")&&ZkProfileParameters.Annual("02290100"),"eight-digit actual calendar validation without accepting manual string format or guessing year policy");
        Check(!new ZkProfileValue("mask","255.0.255.0").IsValid()&&!new ZkProfileValue("mac","01:00:00:00:00:01").IsValid()&&!new ZkProfileValue("port","04370").IsValid()&&!new ZkProfileValue("dhcp","true").IsValid()&&!new ZkProfileValue("door","1").IsValid(),"noncontiguous masks multicast MAC noncanonical port boolean coercion and arbitrary options rejected");
        var snapshot=new ZkAdvancedSettings(ZkAdvancedSettings.Version,"profile-fixture",1,2,3,4,5,6,"profile-product","profile-fw");var plans=ZkProfileParameters.All.Select(Plan).ToArray();var readings=plans.Select(p=>new ZkProfileReadApproval(p.Product,p.Firmware,p.Parameter,"synthetic-read")).ToArray();
        var multi=new MultiSession();var observed=ZkProfileExecutor.AddReadings(snapshot,readings,multi)!;
        Check(observed.IsValid()&&observed.Profiles?.Length==7&&multi.Reads==7&&multi.Writes==0,"all seven closed model-approved observations retained without writes");
        Check(ZkProfileExecutor.AddReadings(snapshot with{Product="other"},readings,new MultiSession()) is null,"read model mismatch returns no certified profile snapshot");
        var readInput=new ZkOperationInput(Input(daylight).Connection,"settings.read",null,false,true,ProfileReadApprovals:readings);
        Check(new ZkOperationOutput("succeeded",Settings:observed).IsValidFor(readInput)&&!new ZkOperationOutput("succeeded",Settings:observed).IsValidFor(readInput with{ProfileReadApprovals=null}),"unsolicited profile reads and approval omission cannot pass typed child boundary");
        multi=new(){Unavailable=true};observed=ZkProfileExecutor.AddReadings(snapshot,readings,multi)!;Check(observed.Profiles!.All(x=>!x.Reported&&x.Value is null),"unavailable native options stay unavailable without invented values");
        Console.WriteLine($"PASS {count} profile-setting contract and actual child checks; synthetic only, no hardware acceptance");
    }
    internal sealed class Session(ZkProfilePlan plan):IZkProfileSession
    {
        public string SerialValue=plan.Serial,FirmwareValue=plan.Firmware;public int Writes;public bool Ack=true,ThrowAfter,FailReadAfter;public ZkProfileValue? Value=plan.Expected;
        public string? Serial()=>SerialValue;public string? Product()=>plan.Product;public string? Firmware()=>FirmwareValue;
        public ZkProfileValue? ReadProfile(string p)=>FailReadAfter&&Writes>0?null:Value;
        public bool SetProfile(ZkProfileValue v){Writes++;Value=v;if(ThrowAfter)throw new IOException();return Ack;}
        public void RefreshProfile(){}
    }
    internal sealed class MultiSession:IZkProfileSession
    {
        public int Reads,Writes;public bool Unavailable;public string? Serial()=>"profile-fixture";public string? Product()=>"profile-product";public string? Firmware()=>"profile-fw";
        public ZkProfileValue? ReadProfile(string p){Reads++;return Unavailable?null:Old(p);}public bool SetProfile(ZkProfileValue v){Writes++;return true;}public void RefreshProfile(){}
    }
}
