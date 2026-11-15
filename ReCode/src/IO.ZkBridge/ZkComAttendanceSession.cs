using IO.DeviceContracts;
internal sealed class ZkComAttendanceSession(object instance,int machine):IAttendanceRangeEraseSession
    {
        public BiometricDeviceIdentity Identity()
        {
            dynamic sdk=instance;string serial,product,firmware;
            if(!(bool)sdk.GetSerialNumber(machine,out serial)||!(bool)sdk.GetProductCode(machine,out product)||!(bool)sdk.GetFirmwareVersion(machine,out firmware))throw new InvalidOperationException();
            return new(serial.Trim(),product.Trim(),firmware.Trim());
        }
        public bool DeleteAttendanceRange(string fromLocalInclusive,string throughLocalInclusive)=>(bool)((dynamic)instance).DeleteAttlogBetweenTheDate(machine,fromLocalInclusive,throughLocalInclusive);
        public bool ClearAttendance()=>(bool)((dynamic)instance).ClearData(machine,1);
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

