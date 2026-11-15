using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using IO.DeviceContracts;

/// <summary>Templates stay within one disposable STA process. Photo uses only bounded private EFS staging; no metadata write, delete, enroll or broad batch setter.</summary>
[SupportedOSPlatform("windows")]
internal static class BiometricTransferAdapter
{
    internal static BiometricTransferOutput Run(BiometricTransferInput input)
    {
        try
        {
            if(!input.IsValid())return new(input.OperationId,"unsupported");
            using var photo=input.Approval.Kind=="photo"?BiometricPhotoLease.OpenChild(input.PhotoWorkspace!,input.Approval.PhotoHandlingReference!):null;
            using var source=new Session(input.Source,true,input.PhotoWorkspace);
            using var target=new Session(input.Target,false,input.PhotoWorkspace);
            return input.InspectOnly?BiometricTransferExecutor.Observe(input,source,target):BiometricTransferExecutor.Execute(input,source,target);
        }
        catch{return new(input.OperationId,input.InspectOnly?"inspection-failed":"failed-before-write");} // Executor owns every mutation; construction only connects.
    }
    private sealed class Session:IBiometricTransferSession,IDisposable
    {
        private object? instance;
        private readonly ZkProbeInput connection;
        private readonly bool source;
        private readonly BiometricPhotoWorkspace? workspace;
        internal Session(ZkProbeInput connection,bool source,BiometricPhotoWorkspace? workspace)
        {
            this.connection=connection;this.source=source;this.workspace=workspace;
            try
            {
                instance=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00853A19-BD51-419B-9269-2DABE57EB61F"),true)!)!;
                dynamic sdk=instance;
                if(!(bool)sdk.SetCommPassword(connection.CommKey)||!(bool)sdk.Connect_Net(connection.Address,connection.Port))throw new InvalidOperationException();
            }
            catch{Dispose();throw;}
        }
        public BiometricDeviceIdentity ReadIdentity()
        {
            dynamic sdk=instance!;string serial,product,firmware;
            if(!(bool)sdk.GetSerialNumber(connection.MachineNumber,out serial)||!(bool)sdk.GetProductCode(connection.MachineNumber,out product)||!(bool)sdk.GetFirmwareVersion(connection.MachineNumber,out firmware))throw new InvalidOperationException();
            return new(serial.Trim(),product.Trim(),firmware.Trim());
        }
        public bool OrdinaryUserExists(string person)
        {
            dynamic sdk=instance!;
            // Enumeration compares the returned BSTR exactly; never casts a badge to Int32/Int64 or aliases 001 to 1.
            if(!(bool)sdk.ReadAllUserID(connection.MachineNumber))return false;
            for(var n=0;n<100000;n++)
            {
                string badge,name,password;int privilege;bool enabled;
                var found=(bool)sdk.SSR_GetAllUserInfo(connection.MachineNumber,out badge,out name,out password,out privilege,out enabled);
                password=null!; // SDK necessarily materializes this transiently. Not emitted or persisted; managed erasure cannot be promised.
                if(!found)return false;
                if(string.Equals(badge,person,StringComparison.Ordinal))return privilege==0&&enabled;
            }
            return false;
        }
        public BiometricSlot Read(string person,BiometricTransferApproval a)
        {
            if(a.Kind=="photo")return ReadPhoto(person,a);
            dynamic sdk=instance!;int machine=connection.MachineNumber;
            if(a.RequiresReadAllTemplate&&!(bool)sdk.ReadAllTemplate(machine))return new("unavailable");
            string data="";int length=0,flag=0;bool found;
            if(a.Kind=="fingerprint")found=(bool)sdk.GetUserTmpExStr(machine,person,a.Slot,out flag,out data,out length);
            else if(a.Kind=="face")found=(bool)sdk.GetUserFaceStr(machine,person,a.Slot,ref data,ref length);
            else return new("unavailable");
            if(found)
            {
                var material=new BiometricMaterial(data,length,flag);
                return material.IsValid(a)?new("present",material):new("unavailable");
            }
            if(!string.IsNullOrEmpty(data)||length!=0)return new("unavailable");
            int error=int.MinValue;sdk.GetLastError(ref error);
            return error==(source?a.SourceAbsentError:a.TargetAbsentError)?new("absent"):new("unavailable");
        }
        public bool Write(string person,BiometricTransferApproval a,BiometricMaterial material)
        {
            if(source||!material.IsValid(a))return false;
            if(a.Kind=="photo")return WritePhoto(person,a,material);
            dynamic sdk=instance!;
            return a.Kind switch
            {
                "fingerprint"=>(bool)sdk.SetUserTmpExStr(connection.MachineNumber,person,a.Slot,material.Flag,material.Data),
                "face"=>(bool)sdk.SetUserFaceStr(connection.MachineNumber,person,a.Slot,material.Data,material.NativeLength),
                _=>false
            };
        }
        private BiometricPhotoFiles? photos;
        private BiometricSlot ReadPhoto(string person,BiometricTransferApproval a)=>(photos??=new(workspace!,source,new PhotoSdk(instance!,connection.MachineNumber))).Read(person,a);
        private bool WritePhoto(string person,BiometricTransferApproval a,BiometricMaterial m)=>(photos??=new(workspace!,source,new PhotoSdk(instance!,connection.MachineNumber))).Write(person,a,m);
        private sealed class PhotoSdk(object sdk,int machine):IBiometricPhotoSdk
        {
            public bool Download(string name,string directory)=>(bool)((dynamic)sdk).ReadFile(machine,name,directory);
            public bool Upload(string path)=>(bool)((dynamic)sdk).SendFile(machine,path);
            public int LastError(){int code=int.MinValue;((dynamic)sdk).GetLastError(ref code);return code;}
        }
        public void Dispose()
        {
            photos?.Dispose();
            if(instance is null)return;
            try{((dynamic)instance).Disconnect();}catch{}
            try{if(Marshal.IsComObject(instance))Marshal.FinalReleaseComObject(instance);}catch{}
            instance=null;
        }
    }
}
