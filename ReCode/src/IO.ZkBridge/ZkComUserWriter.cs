using System.Globalization;
using IO.DeviceContracts;
namespace IO.ZkBridgeTransport;

/// <summary>Production SDK write mapper; SDK activation and lifetime remain owned by the single-use STA child.</summary>
public static class ZkComUserWriter
{
 public static ZkUserWriteOutput Run(object instance,ZkUserWriteInput input,out bool connected)
 {
  connected=false;
  if(!input.IsValid())return new("unsupported");
  try
  {
   var pin=input.Connection;dynamic sdk=instance;
   if(!(bool)sdk.SetCommPassword(pin.CommKey)||!(connected=ZkSdkConnection.Connect(instance,pin)))return new("failed-before-write");
   string serial;if(!(bool)sdk.GetSerialNumber(pin.MachineNumber,out serial)||serial?.Trim()!=pin.ExpectedSerial)return new("identity-mismatch");
   return ZkUserWriteExecutor.Execute(input,new ZkComUserWriteSession(instance,pin.MachineNumber,pin.ExpectedSerial,input.WriteFormat));
  }
  catch{return new("unknown");}
 }
}
public sealed class ZkComUserWriteSession(object instance,int machine,string expectedSerial,string format):IZkUserWriteSession
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

