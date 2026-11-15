using System.Text.Json;
using IO.DeviceContracts;
using IO.ZkBridgeTransport;

internal static class LocalUserWriteTests
{
 internal static void Run()
 {
  int count=0;void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);count++;Console.WriteLine("PASS "+message);}
  foreach(var kind in new[]{"usb","serial"})
  foreach(var format in new[]{ZkUserReadFormats.Ssr,ZkUserReadFormats.Legacy})
  {
   var pin=LocalTransportTests.Input(kind,kind=="usb"?0:17,kind=="usb"?0:57600);
   using var lease=ZkTransportLease.Acquire([pin]);
   var state=LocalExchangeState.Initial(format);var original=state.Users[0];var admin=JsonSerializer.Serialize(state.Users[1]);var desired=original.User with{Name="SYNTHETIC-UPDATED",Card="987"};
   var request=new ZkUserWriteInput(pin,true,true,original.User,desired,format);var sdk=new LocalExchangeSdkDouble(state);
   var result=ZkComUserWriter.Run(sdk,request,out var connected);
   Check(connected&&result.State=="succeeded"&&state.Writes==1&&sdk.WriteFormats.SequenceEqual([format])&&sdk.Connections.SequenceEqual([kind=="usb"?"usb:7":"serial:17:7:57600"]),$"local {kind}/{format} actual production writer uses exact SDK connector and format without fallback");
   Check(state.Users[0].User==desired&&state.Users[0].Password==original.Password&&state.Users[0].Group==original.Group&&state.Users[0].Fingerprints.SequenceEqual(original.Fingerprints)&&JsonSerializer.Serialize(state.Users[1])==admin,$"local {kind}/{format} mapper preserves password group fingerprints and untouched administrator");
   Check(!JsonSerializer.Serialize(result).Contains("synthetic-password",StringComparison.Ordinal)&&!request.ToString().Contains("987",StringComparison.Ordinal),$"local {kind}/{format} result and diagnostic representation exclude protected payload");
   result=ZkComUserWriter.Run(sdk,request with{Expected=desired},out _);
   Check(result.State=="unchanged"&&state.Writes==1&&sdk.CardSets==1,$"local {kind}/{format} fresh unchanged intent avoids all mutating calls");
   result=ZkComUserWriter.Run(sdk,request,out _);
   Check(result.State=="conflict"&&state.Writes==1&&sdk.CardSets==1,$"local {kind}/{format} stale expected state fails before mutation");
   foreach(var fault in new[]{"null-password","partial-scan","duplicate","wrong-serial"})
   {
    state=LocalExchangeState.Initial(format);
    switch(fault)
    {
     case "null-password":state.Users[0]=state.Users[0] with{Password=null};break;
     case "partial-scan":state.PartialEnumeration=true;break;
     case "duplicate":state.Users.Add(state.Users[0]);break;
     case "wrong-serial":state.Serial="SYNTHETIC-WRONG";break;
    }
    sdk=new(state);result=ZkComUserWriter.Run(sdk,request,out _);
    Check(result.State==(fault=="wrong-serial"?"identity-mismatch":"failed-before-write")&&state.Writes==0&&sdk.CardSets==0,$"local {kind}/{format} {fault} cannot become an absent-user or password-clearing write");
   }
   state=LocalExchangeState.Initial(format);sdk=new(state);
   var added=new ZkUser("3","SYNTHETIC-NEW","333",0,true);
   result=ZkComUserWriter.Run(sdk,request with{Expected=null,Desired=added},out _);
   Check(result.State=="succeeded"&&state.Users.Single(x=>x.User.PersonIdentifier=="3").Password==""&&JsonSerializer.Serialize(state.Users[0])==JsonSerializer.Serialize(original),$"local {kind}/{format} new ordinary user has deliberate empty password without changing an existing record");
  }
  Console.WriteLine($"::notice title=Local user write production mapper::{count} USB/serial SSR/legacy synthetic SDK mapper checks passed; no physical SDK driver or device.");
 }
}
