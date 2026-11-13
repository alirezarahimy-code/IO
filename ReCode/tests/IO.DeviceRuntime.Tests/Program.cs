using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using IO.DeviceContracts;

if (args is ["--attendance-read-contracts"]) { await AttendanceReadContractTests.RunAsync(); return; }

if (args is ["--reference-parity"]) { ReferenceParityTests.Run(); return; }

if (args is ["--idle-contracts"]) { await IdleTests.RunAsync(); return; }

if (args is ["--network-contracts"]) { await NetworkTests.RunAsync(); return; }

if (args is ["--restart-contracts"]) { await RestartTests.RunAsync(); return; }

if (args is ["--legacy-user-writes"]) { await LegacyUserWriteTests.RunAsync(); await VirtualDeviceLab.VerifyLegacyWritesAsync(); return; }

if (args is ["--user-read-contracts"]) { await UserReadContractTests.RunAsync(); return; }

if (args is ["--virtual-lab"])
{
    try { await VirtualDeviceLab.VerifyAsync(); }
    catch (Exception error)
    {
        // Keep CI failures inspectable through the GitHub annotations API even
        // when its external log-storage host is unavailable. Synthetic data only.
        var message = error.ToString().Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
        Console.Error.WriteLine("::error title=Virtual device laboratory failure::" + message);
        Environment.ExitCode = 1;
    }
    return;
}

if (args is not ["--verify"])
{
    // This executable is a test-only child fixture, never deployed as the SDK bridge.
    var text=await Console.In.ReadToEndAsync();using var document=JsonDocument.Parse(text);
    var testPinElement = document.RootElement.TryGetProperty("Connection", out var testConnection) ? testConnection : document.RootElement;
    var testPin = JsonSerializer.Deserialize<ZkProbeInput>(testPinElement.GetRawText())!;
    if (testPin.ExpectedSerial.StartsWith("virtual-lab-", StringComparison.Ordinal))
    { await VirtualDeviceLab.RelayAsync(text, testPin); return; }
    if(document.RootElement.TryGetProperty("ReadAttendance",out _))
    {
        var attendance=JsonSerializer.Deserialize<ZkAttendanceReadInput>(text)!;var serial=attendance.Connection.ExpectedSerial;
        void Frame(ZkAttendanceFrame frame)=>Console.WriteLine(JsonSerializer.Serialize(frame));
        Frame(new("start",Serial:serial=="logs-wrong-pin"?"wrong":serial,ReadFormat:serial=="logs-missing-format"?null:attendance.ReadFormat));
        if(serial=="logs-hang"){await Task.Delay(TimeSpan.FromMinutes(1));return;}
        var total=serial=="logs-ok"?103:1;
        for(int i=0;i<total;i++)Frame(new("record",Record:attendance.ReadFormat==ZkAttendanceReadFormats.Legacy?new(i.ToString(System.Globalization.CultureInfo.InvariantCulture),1,4,2026,2,1,8,0,0,0,1,2):new(i.ToString("D5"),int.MaxValue,4,2026,2,i==1?30:1,8,0,0,int.MinValue)));
        if(serial=="logs-extra")Console.WriteLine("{\"Type\":\"record\",\"Password\":\"forbidden\"}");
        if(serial!="logs-truncated")Frame(new("end",Count:serial=="logs-count-mismatch"?total+1:total,State:"succeeded"));return;
    }
    if(document.RootElement.TryGetProperty("WriteLegacyUser",out _))
    {
        var write=JsonSerializer.Deserialize<ZkLegacyUserWriteInput>(text)!.AsRequest();
        if(write.Connection.ExpectedSerial=="legacy-write-hang")await Task.Delay(TimeSpan.FromMinutes(1));
        else Console.WriteLine(JsonSerializer.Serialize(new ZkUserWriteOutput(write.IsValid()?"succeeded":"unsupported")));
        return;
    }
    if(document.RootElement.TryGetProperty("WriteUser",out _))
    {
        var write=JsonSerializer.Deserialize<ZkUserWriteInput>(text)!;
        if(write.Connection.ExpectedSerial=="write-hang")await Task.Delay(TimeSpan.FromMinutes(1));
        else if(write.Connection.ExpectedSerial=="write-extra")Console.WriteLine("{\"State\":\"succeeded\",\"Password\":\"forbidden\"}");
        else Console.WriteLine(JsonSerializer.Serialize(new ZkUserWriteOutput("succeeded")));
        return;
    }
    if(document.RootElement.TryGetProperty("ReadUsers",out _))
    {
        var request=JsonSerializer.Deserialize<ZkUserReadInput>(text)!;var serial=request.Connection.ExpectedSerial;
        void Frame(ZkUserFrame frame)=>Console.WriteLine(JsonSerializer.Serialize(frame));
        Frame(new("start",Serial:serial=="users-wrong-pin"?"wrong":serial,ReadFormat:serial=="users-missing-format"?null:request.ReadFormat));
        if(serial=="users-hang"){await Task.Delay(TimeSpan.FromMinutes(1));return;}
        var total=serial=="users-ok"?103:1;
        for(int i=0;i<total;i++)Frame(new("user",User:new(request.ReadFormat==ZkUserReadFormats.Legacy?i.ToString(System.Globalization.CultureInfo.InvariantCulture):i.ToString("D5"),"<img src=x>",null,0,true)));
        if(serial=="users-extra")Console.WriteLine("{\"Type\":\"user\",\"Password\":\"must-not-pass\"}");
        if(serial!="users-truncated")Frame(new("end",Count:serial=="users-count-mismatch"?total+1:total,State:"succeeded"));
        return;
    }
    if(document.RootElement.TryGetProperty("Connection",out _))
    {
        var operation=JsonSerializer.Deserialize<ZkOperationInput>(text)!;
        if(operation.Operation is "network.ip.set" or "network.inspect")
        {
            if(operation.Connection.ExpectedSerial=="network-hang"){await Task.Delay(TimeSpan.FromMinutes(1));return;}
            Console.WriteLine(JsonSerializer.Serialize(ZkNetworkExecutor.Execute(operation,new NetworkTests.Session(operation.Connection.Address))));return;
        }
        if(operation.Operation=="settings.idle.set")
        {
            if(operation.Connection.ExpectedSerial=="idle-hang"){await Task.Delay(TimeSpan.FromMinutes(1));return;}
            Console.WriteLine(JsonSerializer.Serialize(ZkIdleSettingExecutor.Execute(operation,new IdleTests.Session())));return;
        }
        if(operation.Connection.ExpectedSerial=="restart-hang"){await Task.Delay(TimeSpan.FromMinutes(1));return;}
        if(operation.Operation=="settings.read")
        {
            var settings=new ZkAdvancedSettings(ZkAdvancedSettings.Version,operation.Connection.ExpectedSerial,0,null,int.MinValue,int.MaxValue,2,-1);
            if(operation.Connection.ExpectedSerial=="settings-hang"){await Task.Delay(TimeSpan.FromMinutes(1));return;}
            if(operation.Connection.ExpectedSerial=="settings-wrong-pin")settings=settings with{Serial="different"};
            if(operation.Connection.ExpectedSerial=="settings-missing")Console.WriteLine("{\"State\":\"succeeded\",\"Settings\":{\"Contract\":\"zk-device-info-v1\",\"Serial\":\"settings-missing\"}}");
            else Console.WriteLine(JsonSerializer.Serialize(new ZkOperationOutput("succeeded",Settings:settings)));
            return;
        }
        var information=new ZkDeviceInformation(operation.Connection.ExpectedSerial,"fixture-product",null,null,"2026-11-08 10:00:00",0,null,10,100,null,null,null,null,null,operation.AllowClockSet);
        if(operation.Connection.ExpectedSerial=="wrong-serial")information=information with{Serial="other-device"};
        if(operation.Connection.ExpectedSerial=="extra-field")Console.WriteLine("{\"State\":\"failed\",\"Secret\":\"must-not-pass\"}");
        else Console.WriteLine(JsonSerializer.Serialize(new ZkOperationOutput("succeeded",operation.Operation=="information.read"?information:null)));
        return;
    }
    var fixture=JsonSerializer.Deserialize<ZkProbeInput>(text)!;
    switch(fixture.ExpectedSerial)
    {
        case "ok": Console.WriteLine("sdk-serial-matched");break;
        case "unknown": Console.WriteLine("invented-success");break;
        case "environment": Console.WriteLine(Environment.GetEnvironmentVariable("ConnectionStrings__IO") is null && Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS") is null ? "sdk-serial-matched" : "invalid-input");break;
        case "big": Console.WriteLine(new string('X',16384));break;
        case "hang":
            await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(),"IO-BridgeFixture-"+fixture.DeviceId+".pid"),Environment.ProcessId.ToString());
            await Task.Delay(TimeSpan.FromMinutes(1));break;
    }
    return;
}
var count=0;
void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);Console.WriteLine("PASS "+name);count++;}
var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));
var fixtureAssembly=System.Reflection.Assembly.GetExecutingAssembly().Location;
var input=new ZkProbeInput(Guid.NewGuid(),"127.0.0.1",4370,1,"ok",123456789);
Check(input.IsValid()&&!input.ToString().Contains("123456789",StringComparison.Ordinal),"private native IPC contract validates pins and redacts communication key from text");
Check(!(input with{Port=0}).IsValid(),"invalid native probe port rejected");
Check(!(input with{ExpectedSerial=""}).IsValid()&&!(input with{Address="device.example"}).IsValid(),"native probe requires explicit serial and literal address rather than guessing identity or resolving DNS");
Check(await NativeProcessSupervisor.RunAsync(host,fixtureAssembly,input,2)=="sdk-serial-matched","native supervisor accepts only a bounded allowlisted child result");
Check(await NativeProcessSupervisor.RunAsync(host,fixtureAssembly,input with{ExpectedSerial="unknown"},2)=="bridge-failed","native supervisor refuses invented child success codes");
Environment.SetEnvironmentVariable("ConnectionStrings__IO","synthetic-private-connection");
try{Check(await NativeProcessSupervisor.RunAsync(host,fixtureAssembly,input with{ExpectedSerial="environment"},2)=="sdk-serial-matched","native child does not inherit database configuration");}finally{Environment.SetEnvironmentVariable("ConnectionStrings__IO",null);}
Check(await NativeProcessSupervisor.RunAsync(host,fixtureAssembly,input with{ExpectedSerial="big"},2)!="sdk-serial-matched","native supervisor caps child output and never stores an arbitrary SDK exception payload");
var pidFile=Path.Combine(Path.GetTempPath(),"IO-BridgeFixture-"+input.DeviceId+".pid");
try
{
    var watch=Stopwatch.StartNew();
    Check(await NativeProcessSupervisor.RunAsync(host,fixtureAssembly,input with{ExpectedSerial="hang"},2)=="timed-out"&&watch.Elapsed<TimeSpan.FromSeconds(8),"native hung process is bounded by an external timeout, not a blocked SDK thread");
    if(!File.Exists(pidFile))throw new InvalidOperationException("Child fixture did not start before deadline.");
    var pid=int.Parse(await File.ReadAllTextAsync(pidFile));bool stopped;
    try{using var child=Process.GetProcessById(pid);stopped=child.WaitForExit(3000);}catch(ArgumentException){stopped=true;}
    Check(stopped,"native timed-out child process is actually terminated");
}
finally{if(File.Exists(pidFile))File.Delete(pidFile);}
using var cancelled=new CancellationTokenSource();cancelled.Cancel();
Check(await NativeProcessSupervisor.RunAsync(host,fixtureAssembly,input,2,cancelled.Token)=="interrupted","cancelled native dispatch never launches a new device request");
Check(await NativeProcessSupervisor.RunAsync("relative-dotnet",fixtureAssembly,input,2)=="bridge-failed","native supervisor refuses relative executable paths");
var file=System.Text.Encoding.UTF8.GetBytes("00012\t2026-11-08 08:30:10\t1\t0\t1\n00012\t2026-11-08 08:30:10\t1\t0\t1\n");
var preview=PersonnelUsbPreview.Parse(file,"gregorian",TimeZoneInfo.Utc,1);
Check(preview.Lines.Count==2&&preview.Lines[0].PersonIdentifier=="00012"&&preview.Lines[0].OccurredAtUtc==new DateTimeOffset(2026,11,8,8,30,10,TimeSpan.Zero),"USB reference preview preserves leading-zero identity and explicit Gregorian UTC interpretation");
Check(preview.Lines[1].Status=="duplicate-in-file-requires-review"&&preview.Lines[1].SourceLine==preview.Lines[0].SourceLine,"USB repeated identical rows are retained for review rather than silently discarded as device-native duplicates");
Check(!JsonSerializer.Serialize(preview).Contains("SourceLine",StringComparison.Ordinal)&&!preview.Lines[0].ToString().Contains("00012",StringComparison.Ordinal),"USB raw row is excluded from default JSON and personal data is redacted from text rendering");
var persian=PersonnelUsbPreview.Parse(System.Text.Encoding.UTF8.GetBytes("12\t1405-08-17 08:30:10\t1\t0\t1"),"persian",TimeZoneInfo.Utc,1);
Check(persian.Lines[0].OccurredAtUtc==new DateTimeOffset(2026,11,8,8,30,10,TimeSpan.Zero),"USB Persian calendar is explicitly selected and converted without legacy year guessing");
Check(PersonnelUsbPreview.Parse(file,"gregorian",TimeZoneInfo.Utc,2).Lines.All(x=>x.Status=="machine-mismatch"),"USB preview refuses to attribute rows to a different declared machine");
var dst=TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
var ambiguity=PersonnelUsbPreview.Parse(System.Text.Encoding.UTF8.GetBytes("12\t2026-11-01 01:30:00\t1\t0\t1\n12\t2026-03-08 02:30:00\t1\t0\t1"),"gregorian",dst,1);
Check(ambiguity.Lines[0].Status=="ambiguous-local-time"&&ambiguity.Lines[1].Status=="invalid-local-time"&&ambiguity.Lines.All(x=>x.OccurredAtUtc is null),"USB DST overlaps and gaps are quarantined instead of guessing a UTC time");
var wrongLayout=PersonnelUsbPreview.Parse(System.Text.Encoding.UTF8.GetBytes("12\t2026-11-08 08:30:10\t1\t0\t1\t0"),"gregorian",TimeZoneInfo.Utc,1);
Check(wrongLayout.Lines[0].Status=="layout-mismatch","USB parser never guesses that another five/six-column vendor layout is the reference format");
bool invalidEncoding=false;try{PersonnelUsbPreview.Parse(new byte[]{255},"gregorian",TimeZoneInfo.Utc,1);}catch(ArgumentException){invalidEncoding=true;}
Check(invalidEncoding,"USB invalid UTF-8 is rejected rather than silently replacing source bytes");
var operationInput=new ZkOperationInput(input with{ExpectedSerial="information-ok"},"information.read",null,false);
var informationResult=await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,operationInput,2);
Check(informationResult.State=="succeeded"&&informationResult.Information?.Records==0&&informationResult.Information.RecordCapacity is null,"S06 typed IPC distinguishes a reported zero from an unsupported capacity");
Check((await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,operationInput with{Connection=input with{ExpectedSerial="wrong-serial"}},2)).State=="bridge-failed","S06 supervisor refuses an information response for a different serial pin");
Check((await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,operationInput with{Connection=input with{ExpectedSerial="extra-field"}},2)).State=="bridge-failed","S06 supervisor rejects unknown output fields instead of persisting arbitrary SDK data");
Check(!(operationInput with{Operation="door.open"}).IsValid()&&!(operationInput with{Operation="clock.set",ClockLocal="2026-11-08 10:00:00"}).IsValid(),"S06 operation allowlist excludes door controls and clock writes without configuration opt-in");
var clockInput=operationInput with{Operation="clock.set",ClockLocal="2026-11-08 10:00:00",AllowClockSet=true};
Check((await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,clockInput,2)).State=="succeeded"&&!(clockInput with{ClockLocal="2026-02-30 10:00:00"}).IsValid(),"S06 clock IPC accepts explicit valid wall-clock seconds and rejects invalid calendar dates");
Check(!new ZkOperationOutput("succeeded",informationResult.Information! with{Users=-1}).IsValidFor(operationInput)&&!clockInput.ToString().Contains("123456789",StringComparison.Ordinal),"S06 invalid negative counters are rejected and operation text never reveals communication key");
var usersInput=new ZkUserReadInput(input with{ExpectedSerial="users-ok"},true,true);
var received=new List<ZkUser>();var batches=new List<int>();
async Task Accept(IReadOnlyList<ZkUser> users,CancellationToken token){token.ThrowIfCancellationRequested();received.AddRange(users);batches.Add(users.Count);await Task.CompletedTask;}
var usersResult=await NativeUserReadSupervisor.RunAsync(host,fixtureAssembly,usersInput,5,Accept);
Check(usersResult.State=="succeeded"&&usersResult.Count==103&&received.Count==103&&batches.SequenceEqual(new[]{50,50,3})&&received[0].PersonIdentifier=="00000", "S07 user stream applies backpressure in bounded batches without a product user-count ceiling or losing leading zeros");
Check((await NativeUserReadSupervisor.RunAsync(host,fixtureAssembly,usersInput with{Connection=input with{ExpectedSerial="users-wrong-pin"}},2,Accept)).State=="bridge-failed", "S07 no user frame is accepted before an exact serial-pinned header");
Check((await NativeUserReadSupervisor.RunAsync(host,fixtureAssembly,usersInput with{Connection=input with{ExpectedSerial="users-truncated"}},2,Accept)).State=="bridge-failed", "S07 truncated output is never a complete user snapshot");
Check((await NativeUserReadSupervisor.RunAsync(host,fixtureAssembly,usersInput with{Connection=input with{ExpectedSerial="users-count-mismatch"}},2,Accept)).State=="bridge-failed", "S07 terminal row count must match the exact stream");
Check((await NativeUserReadSupervisor.RunAsync(host,fixtureAssembly,usersInput with{Connection=input with{ExpectedSerial="users-extra"}},2,Accept)).State=="bridge-failed", "S07 unknown frame fields including passwords are rejected rather than stored");
Check((await NativeUserReadSupervisor.RunAsync(host,fixtureAssembly,usersInput with{AllowUserRead=false},2,Accept)).State=="unsupported", "S07 SSR user reading requires explicit per-endpoint installation opt-in");
Check((await NativeUserReadSupervisor.RunAsync(host,fixtureAssembly,usersInput with{Connection=input with{ExpectedSerial="users-hang"}},1,Accept)).State=="timed-out", "S07 a hung enumeration has an external process deadline");
Check(!new ZkUser(" 01","n",null,0,true).IsValid()&&!new ZkUser("01","n",null,9,true).IsValid()&&!usersInput.ToString().Contains("123456789",StringComparison.Ordinal)&&!received[0].ToString().Contains("00000",StringComparison.Ordinal), "S07 malformed identifiers and privilege values fail closed and personal data is redacted from text");
var rowBudgetLine="1\t2026-11-08 08:00:00\t1\t0\t1";
var withinBudget=string.Join('\n',Enumerable.Repeat(rowBudgetLine,10000));
bool BudgetRejected(string value){try{PersonnelUsbPreview.Parse(System.Text.Encoding.UTF8.GetBytes(value),"gregorian",TimeZoneInfo.Utc,1);return false;}catch(ArgumentException){return true;}}
Check(PersonnelUsbPreview.Parse(System.Text.Encoding.UTF8.GetBytes(withinBudget),"gregorian",TimeZoneInfo.Utc,1).Lines.Count==10000&&PersonnelUsbPreview.Parse(System.Text.Encoding.UTF8.GetBytes(withinBudget+"\n"),"gregorian",TimeZoneInfo.Utc,1).Lines.Count==10000&&BudgetRejected(withinBudget+"\n"+rowBudgetLine)&&BudgetRejected(withinBudget+"\n"+rowBudgetLine+"\n"),"S12 the 10000-row preview budget is independent of whether the last line has a terminator");
var minZone=TimeZoneInfo.CreateCustomTimeZone("fixture-plus-fourteen",TimeSpan.FromHours(14),"fixture","fixture");
var maxZone=TimeZoneInfo.CreateCustomTimeZone("fixture-minus-twelve",TimeSpan.FromHours(-12),"fixture","fixture");
var early=PersonnelUsbPreview.Parse(System.Text.Encoding.UTF8.GetBytes("1\t0001-01-01 00:00:00\t1\t0\t1"),"gregorian",minZone,1).Lines[0];
var late=PersonnelUsbPreview.Parse(System.Text.Encoding.UTF8.GetBytes("1\t9999-12-31 23:59:59\t1\t0\t1"),"gregorian",maxZone,1).Lines[0];
Check(early.Status=="invalid-time"&&late.Status=="invalid-time"&&early.OccurredAtUtc is null&&late.OccurredAtUtc is null,"S12 UTC underflow and overflow stay quarantined instead of silently clamping source time to date limits");
var originalUser=new ZkUser("0001","Old","123",0,true);
var writeInput=new ZkUserWriteInput(input,true,true,originalUser,originalUser with{Name="New",Card="456"});
var session=new UserWriteFixture(new(originalUser,"existing-private-password"));
Check(ZkUserWriteExecutor.Execute(writeInput,session).State=="succeeded"&&session.Current?.Password=="existing-private-password"&&session.Writes==1,"S08 ordinary metadata write preserves the existing password only in native memory and verifies read-back");
session=new(new(originalUser,"private"));
Check(ZkUserWriteExecutor.Execute(writeInput with{Desired=originalUser},session).State=="unchanged"&&session.Writes==0,"S08 unchanged ordinary data never triggers an SDK mutation");
session=new(new(originalUser with{Name="External change"},"private"));
Check(ZkUserWriteExecutor.Execute(writeInput,session).State=="conflict"&&session.Writes==0,"S08 a target changed since the approved snapshot is not silently overwritten");
session=new(null);
Check(ZkUserWriteExecutor.Execute(writeInput with{Expected=null},session).State=="succeeded"&&session.Current?.Password=="","S08 explicit absent-user creation uses no password rather than inventing one");
session=new(new(originalUser,"private")){FailWrite=true};
Check(ZkUserWriteExecutor.Execute(writeInput,session).State=="unknown"&&session.Writes==1,"S08 even a false SDK write response is unknown and is never automatically retried");
session=new(new(originalUser,"private")){ChangeReadback=true};
Check(ZkUserWriteExecutor.Execute(writeInput,session).State=="unknown","S08 a different read-back including card normalization is not labelled success");
Check(!(writeInput with{Desired=originalUser with{Privilege=3}}).IsValid()&&!(writeInput with{Expected=originalUser with{Privilege=1}}).IsValid()&&!(writeInput with{Desired=originalUser with{Name=new string('x',21)}}).IsValid()&&!(writeInput with{AllowUserWrite=false}).IsValid(),"S08 admin edits name truncation and unapproved endpoint writes fail closed");
Check(!JsonSerializer.Serialize(new ZkUserWriteExisting(originalUser,"sensitive-password")).Contains("sensitive-password",StringComparison.Ordinal)&&!writeInput.ToString().Contains("123456789",StringComparison.Ordinal),"S08 existing passwords never enter default JSON or request text rendering");
Check((await NativeProcessSupervisor.RunUserWriteAsync(host,fixtureAssembly,writeInput with{Connection=input with{ExpectedSerial="write-extra"}},2)).State=="unknown","S08 untrusted extra SDK output fields cannot create a successful write receipt");
Check((await NativeProcessSupervisor.RunUserWriteAsync(host,fixtureAssembly,writeInput with{Connection=input with{ExpectedSerial="write-hang"}},1)).State=="unknown","S08 external timeout kills a write child and retains uncertainty without retry");
var attendanceInput=new ZkAttendanceReadInput(input with{ExpectedSerial="logs-ok"},true,true);
var logRows=new List<ZkAttendanceRaw>();var logBatches=new List<int>();
async Task AcceptLogs(IReadOnlyList<ZkAttendanceRaw> rows,CancellationToken token){token.ThrowIfCancellationRequested();logRows.AddRange(rows);logBatches.Add(rows.Count);await Task.CompletedTask;}
var logs=await NativeAttendanceReadSupervisor.RunAsync(host,fixtureAssembly,attendanceInput,5,AcceptLogs);
Check(logs.State=="succeeded"&&logs.Count==103&&logBatches.SequenceEqual(new[]{50,50,3})&&logRows[0].PersonIdentifier=="00000"&&logRows[1].Day==30&&logRows[0].WorkCode==int.MinValue,"S09 bounded SDK frames preserve exact identifiers invalid dates and full signed raw codes without a total-record ceiling");
Check((await NativeAttendanceReadSupervisor.RunAsync(host,fixtureAssembly,attendanceInput with{Connection=input with{ExpectedSerial="logs-wrong-pin"}},2,AcceptLogs)).State=="bridge-failed","S09 no attendance tuple is accepted before exact serial pinning");
Check((await NativeAttendanceReadSupervisor.RunAsync(host,fixtureAssembly,attendanceInput with{Connection=input with{ExpectedSerial="logs-truncated"}},2,AcceptLogs)).State=="bridge-failed","S09 truncated SDK output cannot be a complete extraction");
Check((await NativeAttendanceReadSupervisor.RunAsync(host,fixtureAssembly,attendanceInput with{Connection=input with{ExpectedSerial="logs-count-mismatch"}},2,AcceptLogs)).State=="bridge-failed","S09 complete SDK streams require an exact terminal record count");
Check((await NativeAttendanceReadSupervisor.RunAsync(host,fixtureAssembly,attendanceInput with{Connection=input with{ExpectedSerial="logs-extra"}},2,AcceptLogs)).State=="bridge-failed","S09 unknown fields including secret-bearing payloads are refused at the private IPC boundary");
Check((await NativeAttendanceReadSupervisor.RunAsync(host,fixtureAssembly,attendanceInput with{AllowAttendanceRead=false},2,AcceptLogs)).State=="unsupported","S09 extraction requires explicit per-endpoint Gregorian SSR installation approval");
Check((await NativeAttendanceReadSupervisor.RunAsync(host,fixtureAssembly,attendanceInput with{Connection=input with{ExpectedSerial="logs-hang"}},1,AcceptLogs)).State=="timed-out","S09 hung SDK reads have an external process deadline with no automatic replay");
var invalidLog=logRows[1];var validLog=invalidLog with{Day=1};
bool missingSdkFields=false;try{JsonSerializer.Deserialize<ZkAttendanceRaw>("{\"PersonIdentifier\":\"001\"}");}catch(JsonException){missingSdkFields=true;}
Check(JsonSerializer.Deserialize<ZkAttendanceRaw>(validLog.CanonicalJson())==validLog&&validLog.CanonicalJson().Contains("\\u0030",StringComparison.Ordinal)&&missingSdkFields&&!(validLog with{PersonIdentifier="\uD800"}).IsBounded()&&invalidLog.Interpret(TimeZoneInfo.Utc).Status=="invalid-time"&&validLog.Interpret(TimeZoneInfo.Utc).Utc==new DateTimeOffset(2026,2,1,8,0,0,TimeSpan.Zero)&&!(validLog with{PersonIdentifier=new string('x',101)}).IsBounded(),"S09 interpretation never repairs invalid Gregorian dates and structural payload size is bounded");
Check((validLog with{Year=2026,Month=11,Day=1,Hour=1,Minute=30}).Interpret(dst).Status=="ambiguous-local-time"&&(validLog with{Year=2026,Month=3,Day=8,Hour=2,Minute=30}).Interpret(dst).Status=="invalid-local-time","S09 ambiguous and nonexistent site-local SDK times are quarantined without host-zone guessing");
Check((validLog with{PersonIdentifier=" 001"}).Interpret(TimeZoneInfo.Utc).Status=="invalid-identity"&&(validLog with{Year=1,Month=1,Day=1,Hour=0}).Interpret(minZone).Status=="invalid-time"&&!validLog.ToString().Contains(validLog.PersonIdentifier!,StringComparison.Ordinal)&&!attendanceInput.ToString().Contains("123456789",StringComparison.Ordinal),"S09 identity whitespace UTC range overflow and personal-data text rendering fail safely");
var settingsInput=operationInput with{Operation="settings.read",AllowAdvancedSettingsRead=true};
var settingsResult=await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,settingsInput,2);
Check(settingsResult.State=="succeeded"&&settingsResult.Settings is{OneToOne:0,OneToN:null,RepeatTime:int.MinValue,SleepTime:int.MaxValue,RegCard:2,MifareSector:-1},"S11 advanced settings IPC retains unavailable zero and exact signed raw values without inventing units or boolean settings");
Check((await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,settingsInput with{AllowAdvancedSettingsRead=false},2)).State=="unsupported"&&!(settingsInput with{Operation="settings.write"}).IsValid()&&!(settingsInput with{Operation="RemoteEnroll"}).IsValid(),"S11 advanced settings require explicit read opt-in and never admit settings writes or enrollment");
Check((await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,settingsInput with{Connection=input with{ExpectedSerial="settings-wrong-pin"}},2)).State=="bridge-failed"&&(await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,settingsInput with{Connection=input with{ExpectedSerial="settings-missing"}},2)).State=="bridge-failed","S11 settings IPC rejects a different device and missing nullable fields instead of fabricating unavailable values");
Check((await NativeProcessSupervisor.RunOperationAsync(host,fixtureAssembly,settingsInput with{Connection=input with{ExpectedSerial="settings-hang"}},1)).State=="timed-out","S11 settings child reads are externally bounded and timeout never becomes a retried write");
var settingsSession=new SettingsFixture();var observedSettings=ZkAdvancedSettingsReader.Read("fixture",settingsSession);
Check(settingsSession.Codes.SequenceEqual(new[]{25,23,8,4,30,62})&&observedSettings is{OneToOne:0,OneToN:null,RepeatTime:int.MinValue,SleepTime:null,RegCard:2,MifareSector:-1},"S11 closed read executor calls only six observed GetDeviceInfo codes and retains independent call failure rather than zero defaults");
Check(!new ZkOperationOutput("succeeded",informationResult.Information,observedSettings).IsValidFor(settingsInput)&&!(observedSettings with{Contract="guess"}).IsValid()&&!(observedSettings with{Serial="\uD800"}).IsValid(),"S11 settings snapshots reject mixed payload kinds unknown contract versions and lossy serial encoding");
Console.WriteLine($"PASS {count} portable native-boundary and reference-file parser checks; no real device or SDK acceptance implied");

internal sealed class UserWriteFixture(ZkUserWriteExisting? initial):IZkUserWriteSession
{
    public ZkUserWriteExisting? Current=initial;public int Writes;public bool FailWrite;public bool ChangeReadback;
    public ZkUserWriteExisting? ReadExact(string id)=>Writes>0&&ChangeReadback?Current! with{User=Current!.User with{Card="different"}}:Current;
    public bool SetCard(string card)=>true;
    public bool Write(ZkUser user,string password){Writes++;if(FailWrite)return false;Current=new(user,password);return true;}
}

internal sealed class SettingsFixture:IZkAdvancedSettingsReadSession
{
    public List<int> Codes=new();
    public int? ReadDeviceInfo(int code){Codes.Add(code);return code switch{25=>0,23=>null,8=>int.MinValue,4=>throw new InvalidOperationException(),30=>2,62=>-1,_=>throw new InvalidOperationException()};}
}
