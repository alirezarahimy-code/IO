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
            if(document.RootElement.TryGetProperty("ReadAttendance",out _))
            {
                var attendance=JsonSerializer.Deserialize<ZkAttendanceReadInput>(json);
                if(attendance?.IsValid()!=true||!OperatingSystem.IsWindows()){AttendanceFrame(new("end",Count:0,State:"unsupported"));return 0;}
                ReadAttendanceWindows(attendance);return 0;
            }
            if(document.RootElement.TryGetProperty("WriteLegacyUser",out _))
            {
                var request=JsonSerializer.Deserialize<ZkLegacyUserWriteInput>(json)?.AsRequest();
                var legacyResult=request?.IsValid()!=true||!OperatingSystem.IsWindows()?new ZkUserWriteOutput("unsupported"):WriteUserWindows(request);
                Console.WriteLine(JsonSerializer.Serialize(legacyResult));return 0;
            }
            if(document.RootElement.TryGetProperty("WriteUser",out _))
            {
                var request=JsonSerializer.Deserialize<ZkUserWriteInput>(json);
                var writeResult=request?.IsValid()!=true||request.WriteFormat!=ZkUserReadFormats.Ssr||!OperatingSystem.IsWindows()?new ZkUserWriteOutput("unsupported"):WriteUserWindows(request);
                Console.WriteLine(JsonSerializer.Serialize(writeResult));return 0;
            }
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
            var result=ExecuteWindows(connection,operation?.Operation??"probe",operation?.ClockLocal,operation?.AllowClockSet??false,operation?.AllowAdvancedSettingsRead??false,operation?.AllowRestart??false,operation);
            return operationMode?Output(result):Result(result.State);
        }
        catch{return operationMode?Output(new("unknown")):Result("sdk-unavailable");}
    }
    private static int Result(string state){Console.WriteLine(state);return 0;}
    private static int Output(ZkOperationOutput value){Console.WriteLine(JsonSerializer.Serialize(value));return 0;}

    [SupportedOSPlatform("windows")]
    private static ZkOperationOutput ExecuteWindows(ZkProbeInput input,string operation,string? clockLocal,bool allowClockSet,bool allowAdvancedSettingsRead,bool allowRestart,ZkOperationInput? request)
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
            if(operation=="information.read")return ZkInformationReader.Read(serial.Trim(),allowClockSet,new InformationSession(instance,input.MachineNumber));
            if(operation=="settings.read")
            {
                if(!allowAdvancedSettingsRead)return new("unsupported");
                var settings=ZkAdvancedSettingsReader.Read(serial.Trim(),new AdvancedSettingsSession(instance,input.MachineNumber));
                var descriptor=new IdleSettingSession(instance,input.MachineNumber);
                settings=ZkAdditionalSettingsReader.Add(settings with{Product=descriptor.Product(),Firmware=descriptor.Firmware()},new AdditionalSettingsSession(instance,input.MachineNumber));
                if(request?.FunctionKeyReadApproval is {} keyApproval){settings=ZkFunctionKeyReader.Add(settings,keyApproval,new FunctionKeySession(instance))!;if(settings is null)return new("unsupported");}
                string after;if(!(bool)sdk.GetSerialNumber(input.MachineNumber,out after)||after?.Trim()!=input.ExpectedSerial)return new("identity-mismatch");
                return new("succeeded",Settings:settings);
            }
            if(operation=="settings.idle.set")return ZkIdleSettingExecutor.Execute(request!,new IdleSettingSession(instance,input.MachineNumber));
            if(operation is "network.ip.set" or "network.inspect")return ZkNetworkExecutor.Execute(request!,new NetworkSession(instance,input.MachineNumber));
            if(operation=="device.restart")return ZkRestartExecutor.Execute(allowRestart,new RestartSession(instance,input.MachineNumber));
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

    private sealed class IdleSettingSession(object instance,int machine):IZkIdleSettingSession
    {
        public string? Product(){try{string text="";return (bool)((dynamic)instance).GetProductCode(machine,out text)&&ZkIdlePlan.Text(text,120)?text:null;}catch{return null;}}
        public string? Firmware(){try{string text="";return (bool)((dynamic)instance).GetFirmwareVersion(machine,ref text)&&ZkIdlePlan.Text(text,120)?text:null;}catch{return null;}}
        public int? ReadIdleMinutes(){int value=0;return (bool)((dynamic)instance).GetDeviceInfo(machine,4,ref value)?value:null;}
        public bool SetIdleMinutes(int minutes)=>(bool)((dynamic)instance).SetDeviceInfo(machine,4,minutes);
    }

    private sealed class NetworkSession(object instance,int machine):IZkNetworkSession
    {
        public string? ReadIp(){string ip="";return (bool)((dynamic)instance).GetDeviceIP(machine,ref ip)?ip:null;}
        public bool SetIp(string address)=>(bool)((dynamic)instance).SetDeviceIP(machine,address);
        public void Refresh()=>((dynamic)instance).RefreshData(machine);
    }

    private sealed class RestartSession(object instance,int machine):IZkRestartSession
    {
        public bool Restart()=> (bool)((dynamic)instance).RestartDevice(machine);
    }

    // SSR_GetShortkey has no machine parameter in the observed COM interface.
    // This is the one isolated, connected and serial-pinned SDK instance.
    private sealed class FunctionKeySession(object instance):IZkFunctionKeyReadSession
    {
        public ZkFunctionKeyValue? Read(int index)
        {
            int function=0,code=0,autoChange=0;string name="",times="";
            return (bool)((dynamic)instance).SSR_GetShortkey(index,ref function,ref code,ref name,ref autoChange,ref times)
                ?new(index,true,function,code,name,autoChange,times):null;
        }
    }

    private sealed class AdditionalSettingsSession(object instance,int machine):IZkAdditionalSettingsSession
    {
        public string? ReportedIp(){string value="";return (bool)((dynamic)instance).GetDeviceIP(machine,ref value)?value:null;}
        public string? AlgorithmVersionRaw(){string value;return (bool)((dynamic)instance).GetSysOption(machine,"~ZKFPVersion",out value)?value:null;}
        public string? ReportedSubnetMask(){string value;return (bool)((dynamic)instance).GetSysOption(machine,"NetMask",out value)?value:null;}
        public string? ReportedGateway(){string value;return (bool)((dynamic)instance).GetSysOption(machine,"GATEIPAddress",out value)?value:null;}
    }

    private sealed class AdvancedSettingsSession(object instance,int machine):IZkAdvancedSettingsReadSession
    {
        public int? ReadDeviceInfo(int code)
        {
            int value=0;return (bool)((dynamic)instance).GetDeviceInfo(machine,code,ref value)?value:null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static ZkUserWriteOutput WriteUserWindows(ZkUserWriteInput input)
    {
        object? instance=null;bool connected=false;
        try
        {
            var pin=input.Connection;instance=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00853A19-BD51-419B-9269-2DABE57EB61F"),true)!)!;dynamic sdk=instance;
            if(!(bool)sdk.SetCommPassword(pin.CommKey)||!(connected=(bool)sdk.Connect_Net(pin.Address,pin.Port)))return new("failed-before-write");
            string serial;if(!(bool)sdk.GetSerialNumber(pin.MachineNumber,out serial)||serial?.Trim()!=pin.ExpectedSerial)return new("identity-mismatch");
            return ZkUserWriteExecutor.Execute(input,new UserWriteSession(instance,pin.MachineNumber,pin.ExpectedSerial,input.WriteFormat));
        }
        catch{return new("unknown");}
        finally{if(instance is not null){try{if(connected)((dynamic)instance).Disconnect();}catch{}try{if(Marshal.IsComObject(instance))Marshal.FinalReleaseComObject(instance);}catch{}}}
    }
    private sealed class UserWriteSession(object instance,int machine,string expectedSerial,string format):IZkUserWriteSession
    {
        public ZkUserWriteExisting? ReadExact(string personIdentifier)
        {
            dynamic sdk=instance;int before=0;
            if(!(bool)sdk.GetDeviceStatus(machine,2,ref before)||before<0||!(bool)sdk.ReadAllUserID(machine))throw new InvalidOperationException();
            string beforeSerial;if(!(bool)sdk.GetSerialNumber(machine,out beforeSerial)||beforeSerial?.Trim()!=expectedSerial)throw new InvalidOperationException();
            ZkUserWriteExisting? found=null;long count=0;
            while(true)
            {
                string badge,name="",password="";int privilege=0;bool enabled=false;
                try
                {
                    if(format==ZkUserReadFormats.Ssr)
                    {if(!(bool)sdk.SSR_GetAllUserInfo(machine,out badge,out name,out password,out privilege,out enabled))break;}
                    else if(format==ZkUserReadFormats.Legacy)
                    {int numeric=0;if(!(bool)sdk.GetAllUserInfo(machine,ref numeric,ref name,ref password,ref privilege,ref enabled))break;badge=ZkUserReadFormats.LegacyIdentity(numeric);}
                    else throw new InvalidOperationException();
                    count++;
                    if(badge==personIdentifier)
                    {
                        if(found is not null)throw new InvalidOperationException();
                        string card;if(!(bool)sdk.GetStrCardNumber(out card))throw new InvalidOperationException();
                        found=new(new(badge,name,card,privilege,enabled),password);
                    }
                }
                finally{password=string.Empty;}
            }
            int after=0;string afterSerial;
            if(!(bool)sdk.GetDeviceStatus(machine,2,ref after)||after!=before||count!=after||!(bool)sdk.GetSerialNumber(machine,out afterSerial)||afterSerial?.Trim()!=expectedSerial)throw new InvalidOperationException();return found;
        }
        public bool SetCard(string card)=>(bool)((dynamic)instance).SetStrCardNumber(card);
        public bool Write(ZkUser user,string password)=>format switch
        {
            ZkUserReadFormats.Ssr=>(bool)((dynamic)instance).SSR_SetUserInfo(machine,user.PersonIdentifier,user.Name,password,0,user.Enabled),
            ZkUserReadFormats.Legacy=>(bool)((dynamic)instance).SetUserInfo(machine,int.Parse(user.PersonIdentifier,NumberStyles.None,CultureInfo.InvariantCulture),user.Name,password,0,user.Enabled),
            _=>throw new InvalidOperationException()
        };
        // No AccGroup setter, access-control, batch-disable, template, photo or delete API.
    }

    private static void AttendanceFrame(ZkAttendanceFrame frame){Console.WriteLine(JsonSerializer.Serialize(frame));Console.Out.Flush();}
    [SupportedOSPlatform("windows")]
    private static void ReadAttendanceWindows(ZkAttendanceReadInput request)
    {
        object? instance=null;bool connected=false;long count=0;
        try
        {
            var pin=request.Connection;instance=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00853A19-BD51-419B-9269-2DABE57EB61F"),true)!)!;dynamic sdk=instance;
            if(!(bool)sdk.SetCommPassword(pin.CommKey)||!(connected=(bool)sdk.Connect_Net(pin.Address,pin.Port))){AttendanceFrame(new("end",Count:0,State:"failed"));return;}
            string serial;if(!(bool)sdk.GetSerialNumber(pin.MachineNumber,out serial)||serial?.Trim()!=pin.ExpectedSerial){AttendanceFrame(new("end",Count:0,State:"identity-mismatch"));return;}
            ZkAttendanceReadExecutor.Execute(request,new AttendanceReadSession(instance,pin.MachineNumber),AttendanceFrame);
        }
        catch{AttendanceFrame(new("end",Count:count,State:"failed"));}
        finally{if(instance is not null){try{if(connected)((dynamic)instance).Disconnect();}catch{}try{if(Marshal.IsComObject(instance))Marshal.FinalReleaseComObject(instance);}catch{}}}
    }

    private sealed class AttendanceReadSession(object instance,int machine):IZkAttendanceReadSession
    {
        public string? Serial(){string serial;return (bool)((dynamic)instance).GetSerialNumber(machine,out serial)?serial?.Trim():null;}
        public int? RecordCount(){int value=0;return (bool)((dynamic)instance).GetDeviceStatus(machine,6,ref value)?value:null;}
        public bool Begin()=>(bool)((dynamic)instance).ReadAllGLogData(machine);
        public ZkAttendanceRaw? Next(string format)
        {
            dynamic sdk=instance;
            if(format==ZkAttendanceReadFormats.Ssr)
            {
                string badge;int verify,check,y,m,d,h,n,s,work=0;
                if(!(bool)sdk.SSR_GetGeneralLogData(machine,out badge,out verify,out check,out y,out m,out d,out h,out n,out s,ref work))return null;
                return new(badge,verify,check,y,m,d,h,n,s,work);
            }
            if(format==ZkAttendanceReadFormats.Legacy)
            {
                // IZKEM GetGeneralLogData has two separate machine outputs. The
                // reference aliases them; preserve BOTH instead of losing evidence.
                int terminal=0,badge=0,enrollmentMachine=0,verify=0,check=0,y=0,m=0,d=0,h=0,n=0;
                if(!(bool)sdk.GetGeneralLogData(machine,ref terminal,ref badge,ref enrollmentMachine,ref verify,ref check,ref y,ref m,ref d,ref h,ref n))return null;
                // Legacy exposes minute precision, no seconds/work-code outputs.
                return new(badge.ToString(System.Globalization.CultureInfo.InvariantCulture),verify,check,y,m,d,h,n,0,0,terminal,enrollmentMachine);
            }
            throw new InvalidOperationException();
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
            ZkUserReadExecutor.Execute(request,new UserReadSession(instance,pin.MachineNumber),UserFrame);
        }
        catch{UserFrame(new("end",Count:count,State:"failed"));}
        finally{if(instance is not null){try{if(connected)((dynamic)instance).Disconnect();}catch{}try{if(Marshal.IsComObject(instance))Marshal.FinalReleaseComObject(instance);}catch{}}}
    }

    private sealed class UserReadSession(object instance,int machine):IZkUserReadSession
    {
        public string? Serial(){string serial;return (bool)((dynamic)instance).GetSerialNumber(machine,out serial)?serial?.Trim():null;}
        public int? UserCount(){int value=0;return (bool)((dynamic)instance).GetDeviceStatus(machine,2,ref value)?value:null;}
        public bool Begin()=>(bool)((dynamic)instance).ReadAllUserID(machine);
        public ZkUser? Next(string format)
        {
            dynamic sdk=instance;string badge,name="",password="";int privilege=0;bool enabled=false;
            try
            {
                if(format==ZkUserReadFormats.Ssr)
                {if(!(bool)sdk.SSR_GetAllUserInfo(machine,out badge,out name,out password,out privilege,out enabled))return null;}
                else if(format==ZkUserReadFormats.Legacy)
                {int numeric=0;if(!(bool)sdk.GetAllUserInfo(machine,ref numeric,ref name,ref password,ref privilege,ref enabled))return null;badge=ZkUserReadFormats.LegacyIdentity(numeric);}
                else throw new InvalidOperationException();
                string card;string? available=null;try{if((bool)sdk.GetStrCardNumber(out card))available=card;}catch{}
                return new(badge,name,available,privilege,enabled);
            }
            finally{password=string.Empty;} // Password never leaves this native method.
        }
    }

    private sealed class InformationSession(object instance,int machine):IZkInformationSession
    {
        public string? Serial(){string value;return (bool)((dynamic)instance).GetSerialNumber(machine,out value)?value?.Trim():null;}
        public string? Product(){string value;return (bool)((dynamic)instance).GetProductCode(machine,out value)?value:null;}
        public string? Firmware(){string value="";return (bool)((dynamic)instance).GetFirmwareVersion(machine,ref value)?value:null;}
        public string? Mac(){string value="";return (bool)((dynamic)instance).GetDeviceMAC(machine,ref value)?value:null;}
        public string? ClockLocal(){int y=0,m=0,d=0,h=0,n=0,s=0;return (bool)((dynamic)instance).GetDeviceTime(machine,ref y,ref m,ref d,ref h,ref n,ref s)?new DateTime(y,m,d,h,n,s).ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture):null;}
        public int? Status(int code){int value=0;return (bool)((dynamic)instance).GetDeviceStatus(machine,code,ref value)?value:null;}
    }
}
