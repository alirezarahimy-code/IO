using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using IO.DeviceContracts;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        bool operationMode=false;
        try
        {
            using var stdin=Console.OpenStandardInput();using var bytes=new MemoryStream();var buffer=new byte[1024];int count;
            while((count=stdin.Read(buffer,0,buffer.Length))>0){if(bytes.Length+count>8192)return Result("invalid-input");bytes.Write(buffer,0,count);}
            var json=new UTF8Encoding(false,true).GetString(bytes.ToArray());using var document=JsonDocument.Parse(json);
            if(document.RootElement.TryGetProperty("ReadUsers",out _))
            {
                var request=JsonSerializer.Deserialize<ZkUserReadInput>(json);
                if(request?.IsValid()!=true||!OperatingSystem.IsWindows()){UserFrame(new("end",Count:0,State:"unsupported"));return 0;}
                ReadUsersWindows(request);return 0;
            }
            operationMode=document.RootElement.TryGetProperty("Connection",out _);
            var operation=operationMode?JsonSerializer.Deserialize<ZkOperationInput>(json):null;
            var connection=operationMode?operation?.Connection:JsonSerializer.Deserialize<ZkProbeInput>(json);
            if(connection is null||!connection.IsValid()||(operationMode&&(operation is null||!operation.IsValid())))
                return operationMode?Output(new("unsupported")):Result("invalid-input");
            if(!OperatingSystem.IsWindows())return operationMode?Output(new("unsupported")):Result("unsupported");
            var result=ExecuteWindows(connection,operation?.Operation??"probe",operation?.ClockLocal,operation?.AllowClockSet??false);
            return operationMode?Output(result):Result(result.State);
        }
        catch{return operationMode?Output(new("unknown")):Result("sdk-unavailable");}
    }
    private static int Result(string state){Console.WriteLine(state);return 0;}
    private static int Output(ZkOperationOutput value){Console.WriteLine(JsonSerializer.Serialize(value));return 0;}

    [SupportedOSPlatform("windows")]
    private static ZkOperationOutput ExecuteWindows(ZkProbeInput input,string operation,string? clockLocal,bool allowClockSet)
    {
        object? instance=null;bool connected=false,writeStarted=false;
        try
        {
            var type=Type.GetTypeFromCLSID(new Guid("00853A19-BD51-419B-9269-2DABE57EB61F"),throwOnError:true)!;
            instance=Activator.CreateInstance(type)??throw new InvalidOperationException();dynamic sdk=instance;
            if(!(bool)sdk.SetCommPassword(input.CommKey))return new(operation=="probe"?"unreachable":"failed");
            connected=sdk.Connect_Net(input.Address,input.Port);
            if(!connected)return new(operation=="probe"?"unreachable":"failed");
            string serial;
            if(!(bool)sdk.GetSerialNumber(input.MachineNumber,out serial)||string.IsNullOrWhiteSpace(serial)||serial.Length>100||serial.Any(char.IsControl)||serial.Trim()!=input.ExpectedSerial)
                return new(operation=="probe"?"sdk-identity-mismatch":"identity-mismatch");
            if(operation=="probe")return new("sdk-serial-matched");
            if(operation=="information.read")return new("succeeded",ReadInformation(sdk,input.MachineNumber,serial.Trim(),allowClockSet));
            if(operation!="clock.set"||!allowClockSet||!ZkOperationInput.ValidClock(clockLocal))return new("unsupported");
            var time=DateTime.ParseExact(clockLocal!,"yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture);
            writeStarted=true;
            // Explicit wall-clock components only: never sync the Windows host's local time by assumption.
            return new((bool)sdk.SetDeviceTime2(input.MachineNumber,time.Year,time.Month,time.Day,time.Hour,time.Minute,time.Second)?"succeeded":"unknown");
        }
        catch{return new(operation=="probe"?"sdk-unavailable":writeStarted?"unknown":"failed");}
        finally
        {
            if(instance is not null){try{if(connected)((dynamic)instance).Disconnect();}catch{}try{if(Marshal.IsComObject(instance))Marshal.FinalReleaseComObject(instance);}catch{}}
        }
    }

    private static void UserFrame(ZkUserFrame frame){Console.WriteLine(JsonSerializer.Serialize(frame));Console.Out.Flush();}
    [SupportedOSPlatform("windows")]
    private static void ReadUsersWindows(ZkUserReadInput request)
    {
        object? instance=null;bool connected=false;long count=0;
        try
        {
            var pin=request.Connection;var type=Type.GetTypeFromCLSID(new Guid("00853A19-BD51-419B-9269-2DABE57EB61F"),true)!;
            instance=Activator.CreateInstance(type)!;dynamic sdk=instance;
            if(!(bool)sdk.SetCommPassword(pin.CommKey)||!(connected=(bool)sdk.Connect_Net(pin.Address,pin.Port))){UserFrame(new("end",Count:0,State:"failed"));return;}
            string serial;if(!(bool)sdk.GetSerialNumber(pin.MachineNumber,out serial)||serial?.Trim()!=pin.ExpectedSerial){UserFrame(new("end",Count:0,State:"identity-mismatch"));return;}
            // Only the explicitly approved SSR/TFT user contract is supported here.
            int before=0;if(!(bool)sdk.GetDeviceStatus(pin.MachineNumber,2,ref before)||before<0||!(bool)sdk.ReadAllUserID(pin.MachineNumber)){UserFrame(new("end",Count:0,State:"failed"));return;}
            UserFrame(new("start",Serial:pin.ExpectedSerial));
            string badge,name,password;int privilege;bool enabled;
            while((bool)sdk.SSR_GetAllUserInfo(pin.MachineNumber,out badge,out name,out password,out privilege,out enabled))
            {
                password=string.Empty; // Never put a device password in a frame, file, log or database.
                string card;string? availableCard=null;try{if((bool)sdk.GetStrCardNumber(out card))availableCard=card;}catch{}
                var user=new ZkUser(badge,name,availableCard,privilege,enabled);if(!user.IsValid()){UserFrame(new("end",Count:count,State:"incomplete"));return;}
                UserFrame(new("user",User:user));count++;
            }
            int after=0;var complete=(bool)sdk.GetDeviceStatus(pin.MachineNumber,2,ref after)&&before==after&&after==count;
            UserFrame(new("end",Count:count,State:complete?"succeeded":"incomplete"));
        }
        catch{UserFrame(new("end",Count:count,State:"failed"));}
        finally{if(instance is not null){try{if(connected)((dynamic)instance).Disconnect();}catch{}try{if(Marshal.IsComObject(instance))Marshal.FinalReleaseComObject(instance);}catch{}}}
    }

    private static ZkDeviceInformation ReadInformation(dynamic sdk,int machine,string serial,bool clockConfigured)
    {
        static string? Text(Func<string?> read,int max=120){try{var value=read();return !string.IsNullOrWhiteSpace(value)&&value.Length<=max&&!value.Any(char.IsControl)?value.Trim():null;}catch{return null;}}
        string? product=Text(()=>{string value;return (bool)sdk.GetProductCode(machine,out value)?value:null;});
        string? firmware=Text(()=>{string value="";return (bool)sdk.GetFirmwareVersion(machine,ref value)?value:null;});
        string? mac=Text(()=>{string value="";return (bool)sdk.GetDeviceMAC(machine,ref value)?value:null;},100);
        string? clock=Text(()=>{int y=0,m=0,d=0,h=0,n=0,s=0;return (bool)sdk.GetDeviceTime(machine,ref y,ref m,ref d,ref h,ref n,ref s)?new DateTime(y,m,d,h,n,s).ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture):null;});
        int? Status(int code){try{int value=0;return (bool)sdk.GetDeviceStatus(machine,code,ref value)&&value>=0?value:null;}catch{return null;}}
        // Counter codes are observed reference contracts; missing/failed calls remain null, never fabricated zero.
        return new(serial,product,firmware,mac,clock,Status(6),Status(9),Status(2),Status(8),Status(1),Status(3),Status(7),Status(21),Status(22),clockConfigured);
    }
}
