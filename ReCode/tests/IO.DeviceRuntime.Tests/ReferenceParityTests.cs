using IO.DeviceContracts;
internal static class ReferenceParityTests
{
 public static void Run()
 {
  int count=0;void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);Console.WriteLine("PASS reference-parity "+text);count++;}
  var session=new Session();var result=ZkInformationReader.Read("fixture",false,session);var x=result.Information!;
  Check(result.State=="succeeded"&&session.Codes.SequenceEqual(new[]{6,9,2,8,1,3,7,21,22})&&session.SerialReads==2,"capacity mapping exactly matches observed reference codes and serial is checked twice");
  Check(x.Records==106&&x.RecordCapacity==109&&x.Users==102&&x.UserCapacity==108&&x.Administrators==101&&x.Fingers==103&&x.FingerCapacity==107&&x.Faces==121&&x.FaceCapacity==122,"every count and capacity keeps its own field without the reference shared-ref-value leakage");
  session=new(){FailStatus=9};x=ZkInformationReader.Read("fixture",true,session).Information!;
  Check(x.Records==106&&x.RecordCapacity==null&&x.ClockWriteEnabledByConfiguration,"one failed capacity call stays unavailable without inheriting the preceding count");
  session=new(){NegativeStatus=6,ZeroStatus=2};x=ZkInformationReader.Read("fixture",false,session).Information!;
  Check(x.Records==null&&x.Users==0,"negative counter is unavailable while a genuinely reported zero is retained");
  session=new(){MissingProduct=true,BadClock=true};x=ZkInformationReader.Read("fixture",false,session).Information!;
  Check(x.Product==null&&x.Firmware=="fw"&&x.ClockLocal==null,"independent descriptor failures and invalid wall clocks never fabricate defaults or a vendor name");
  session=new(){ForeignBefore=true};result=ZkInformationReader.Read("fixture",false,session);
  Check(result.State=="identity-mismatch"&&result.Information==null&&session.Codes.Count==0,"foreign serial blocks all information reads before collecting a snapshot");
  session=new(){ForeignAfter=true};result=ZkInformationReader.Read("fixture",false,session);
  Check(result.State=="identity-mismatch"&&result.Information==null,"serial drift after descriptor collection cannot be used as unknown-write recovery evidence");
  session=new(){ThrowAfter=true};Check(ZkInformationReader.Read("fixture",false,session).State=="identity-mismatch","unavailable final identity is not successful recovery evidence");
  var snapshot=new ZkAdvancedSettings(ZkAdvancedSettings.Version,"fixture",1,2,3,4,5,6);
  var additional=new Additional();var settings=ZkAdditionalSettingsReader.Add(snapshot,additional);
  Check(settings.IsValid()&&settings.ReportedIp=="10.20.30.40"&&settings.AlgorithmVersionRaw=="10"&&settings.SleepTime==4&&additional.Calls==2,"GetAdvanced2 reads only reported IP and raw fixed algorithm option without changing six raw fields");
  additional=new(){FailIp=true};settings=ZkAdditionalSettingsReader.Add(snapshot,additional);
  Check(settings.ReportedIp==null&&settings.AlgorithmVersionRaw=="10","failed GetDeviceIP does not erase independent algorithm observation");
  additional=new(){Algorithm=null};settings=ZkAdditionalSettingsReader.Add(snapshot,additional);
  Check(settings.AlgorithmVersionRaw==null&&settings.ReportedIp!=null,"missing algorithm remains unavailable instead of the reference guessed default version nine");
  additional=new(){Algorithm="not-numeric"};settings=ZkAdditionalSettingsReader.Add(snapshot,additional);
  Check(settings.AlgorithmVersionRaw=="not-numeric"&&settings.IsValid(),"unknown algorithm text is preserved raw and never parsed into a supported biometric capability");
  additional=new(){Algorithm="\uD800"};settings=ZkAdditionalSettingsReader.Add(snapshot,additional);
  Check(settings.AlgorithmVersionRaw==null&&!(snapshot with{ReportedIp="bad\ntext"}).IsValid(),"malformed descriptor encoding and control characters cannot enter typed settings evidence");
  var pin=new ZkProbeInput(Guid.NewGuid(),"10.20.30.40",4370,1,"fixture",0);
  foreach(var operation in new[]{"DeleteAllData","DeleteAllRecords","DeleteAdmins","RemoveTimeRecords","RemoteEnroll","ModifyUsers","Set1_1","Set1_N","SetAlgorithm","SetCardReg","SetDLST","SetRepeatTime","SetSector","ReadKey","ChangeKey","clear-punches","sync-users","factory.reset","settings.write","door.open"})
   Check(!new ZkOperationInput(pin,operation,null,true,true,true,AllowIdleMinutesWrite:true).IsValid(),"legacy action cannot escape closed dispatch: "+operation);
  Console.WriteLine($"PASS {count} reference parity checks; static contract and synthetic sessions, no actual SDK or hardware acceptance");
 }
 private sealed class Additional:IZkAdditionalSettingsSession
 {
  public int Calls;public bool FailIp;public string? Algorithm="10";
  public string? ReportedIp(){Calls++;if(FailIp)throw new IOException();return "10.20.30.40";}
  public string? AlgorithmVersionRaw(){Calls++;return Algorithm;}
 }
 private sealed class Session:IZkInformationSession
 {
  public List<int> Codes=[];public int SerialReads;public int FailStatus=-1,NegativeStatus=-1,ZeroStatus=-1;public bool MissingProduct,BadClock,ForeignBefore,ForeignAfter,ThrowAfter;
  public string? Serial(){SerialReads++;if(SerialReads==2&&ThrowAfter)throw new IOException();return ForeignBefore||SerialReads==2&&ForeignAfter?"foreign":"fixture";}
  public string? Product(){if(MissingProduct)throw new IOException();return "product";}
  public string? Firmware()=>"fw";
  public string? Mac()=>null;
  public string? ClockLocal()=>BadClock?"2026-02-30 00:00:00":"2026-11-08 10:00:00";
  public int? Status(int code){Codes.Add(code);if(code==FailStatus)throw new IOException();return code==NegativeStatus?-1:code==ZeroStatus?0:100+code;}
 }
}
