using System.Globalization;
using IO.DeviceContracts;

// Shared with portable tests as linked production source. Only the injected COM object is substituted.
// No arbitrary option API is exposed: parameter names come from the seven closed contracts.
internal sealed class ZkComProfileSession(object instance,int machine):IZkProfileSession
    {
        public string? Serial(){string value;return (bool)((dynamic)instance).GetSerialNumber(machine,out value)?value?.Trim():null;}
        public string? Product(){try{string text="";return (bool)((dynamic)instance).GetProductCode(machine,out text)&&ZkIdlePlan.Text(text,120)?text:null;}catch{return null;}}
        public string? Firmware(){try{string text="";return (bool)((dynamic)instance).GetFirmwareVersion(machine,ref text)&&ZkIdlePlan.Text(text,120)?text:null;}catch{return null;}}
        private string? Option(string key){string value;return (bool)((dynamic)instance).GetSysOption(machine,key,out value)?value:null;}
        public ZkProfileValue? ReadProfile(string parameter)
        {
            if(parameter=="daylight")
            {
                int support=0;string begin="",end="";if(!(bool)((dynamic)instance).GetDaylight(machine,ref support,ref begin,ref end))return null;
                return new(parameter,support.ToString(CultureInfo.InvariantCulture),begin,end,Option("DLSTMode"),Option("CurTimeMode"),Option("DaylightSavingTimeOn"));
            }
            if(parameter=="mac"){string value="";return (bool)((dynamic)instance).GetDeviceMAC(machine,ref value)?new(parameter,value):null;}
            var raw=Option(ZkProfileParameters.Option(parameter));return raw is null?null:new(parameter,raw);
        }
        public bool SetProfile(ZkProfileValue value)
        {
            if(!value.IsValid())return false;
            if(value.Parameter=="daylight")return (bool)((dynamic)instance).SetDaylight(machine,int.Parse(value.Raw,CultureInfo.InvariantCulture),value.Begin!,value.End!);
            if(value.Parameter=="mac")return (bool)((dynamic)instance).SetDeviceMAC(machine,value.Raw);
            return (bool)((dynamic)instance).SetSysOption(machine,ZkProfileParameters.Option(value.Parameter),value.Raw);
        }
        public void RefreshProfile()=>((dynamic)instance).RefreshData(machine);
    }

