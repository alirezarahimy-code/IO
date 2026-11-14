using System.Text;
using IO.DeviceContracts;

internal static class GatewayEnrollmentTests
{
    public static async Task RunAsync()
    {
        var count=0;void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);count++;Console.WriteLine("PASS "+name);}
        async Task<bool> Reject<E>(Func<Task> work)where E:Exception{try{await work();return false;}catch(E){return true;}}
        var gateway=Guid.Parse("11111111-1111-1111-1111-111111111111");var device=Guid.Parse("22222222-2222-2222-2222-222222222222");
        var source=Guid.Parse("33333333-3333-3333-3333-333333333333");var transfer=Guid.Parse("44444444-4444-4444-4444-444444444444");
        var key=Enumerable.Range(0,32).Select(x=>(byte)x).ToArray();var binding=new PushGatewayBinding(gateway,device,source,"SYNTHETIC-SERIAL","key-1",TimeZoneInfo.Utc,key);
        var clock=new Clock(new(2026,11,8,0,0,0,TimeSpan.Zero));var body=Encoding.UTF8.GetBytes("001\t2026-11-08 08:00:00\t0\t15\t000\t0\t0\n");
        PushGatewayEnvelope Envelope(Guid? id=null,DateTimeOffset? sent=null,string? stamp=null,byte[]? bytes=null)
        {var t=id??transfer;var at=sent??clock.GetUtcNow();var b=bytes??body;return new(t,at,stamp,b,AuthenticatedPushGateway.Sign(binding,t,at,stamp,b));}
        var store=new Store();Task<PushGatewayRetention> Receive(PushGatewayEnvelope e,PushGatewayBinding? b=null)=>AuthenticatedPushGateway.ReceiveAsync(b??binding,e,store,clock);
        Check(Envelope().Signature=="AFE5C76730601E7791C77E1CE41CD93FE49A327A1EE81376E3E2D866A17E526E","gateway canonical HMAC matches an independently computed Python binary-wire vector");
        key[0]=255;Check(Envelope().Signature=="AFE5C76730601E7791C77E1CE41CD93FE49A327A1EE81376E3E2D866A17E526E","gateway binding copies provisioned key buffers");
        Check(await Reject<UnauthorizedAccessException>(()=>Receive(Envelope() with{Signature=new string('0',64)})),"gateway unsigned SN-only request has no commit or receipt");
        Check(await Reject<UnauthorizedAccessException>(()=>Receive(Envelope() with{Body=Encoding.UTF8.GetBytes("changed")}))&&
            await Reject<UnauthorizedAccessException>(()=>Receive(Envelope() with{Stamp="changed"}))&&
            await Reject<UnauthorizedAccessException>(()=>Receive(Envelope() with{TransferId=Guid.NewGuid()})),"gateway MAC binds exact body opaque cursor and transfer id");
        var otherKey=Enumerable.Repeat((byte)9,32).ToArray();
        foreach(var changed in new[]{new PushGatewayBinding(Guid.NewGuid(),device,source,"SYNTHETIC-SERIAL","key-1",TimeZoneInfo.Utc,otherKey),
            new PushGatewayBinding(gateway,Guid.NewGuid(),source,"SYNTHETIC-SERIAL","key-1",TimeZoneInfo.Utc,Enumerable.Range(0,32).Select(x=>(byte)x).ToArray()),
            new PushGatewayBinding(gateway,device,Guid.NewGuid(),"SYNTHETIC-SERIAL","key-1",TimeZoneInfo.Utc,Enumerable.Range(0,32).Select(x=>(byte)x).ToArray()),
            new PushGatewayBinding(gateway,device,source,"OTHER-SERIAL","key-1",TimeZoneInfo.Utc,Enumerable.Range(0,32).Select(x=>(byte)x).ToArray()),
            new PushGatewayBinding(gateway,device,source,"SYNTHETIC-SERIAL","key-2",TimeZoneInfo.Utc,Enumerable.Range(0,32).Select(x=>(byte)x).ToArray())})
            Check(await Reject<UnauthorizedAccessException>(()=>Receive(Envelope(),changed)),"gateway wrong key or bound gateway device source serial key version fails closed");
        Check(await Reject<UnauthorizedAccessException>(()=>Receive(Envelope(sent:clock.GetUtcNow().AddSeconds(-121))))&&
            await Reject<UnauthorizedAccessException>(()=>Receive(Envelope(sent:clock.GetUtcNow().AddSeconds(31)))),"gateway rejects stale and future authenticated messages before storage");
        Check(store.Calls==0,"gateway invalid authentication never invokes the durable store");
        var receipt=await Receive(Envelope());
        Check(receipt.GatewayAuthenticated&&!receipt.DeviceIdentityVerified&&receipt.State=="gateway-retained-device-unverified"&&receipt.RetainedRows==1,
            "gateway retention explicitly authenticates the gateway not native device identity or vendor processed ACK");
        Check((await Receive(Envelope(sent:clock.GetUtcNow().AddSeconds(1)))).ReceiptId==receipt.ReceiptId&&store.Rows.Count==1,
            "gateway exact transfer retry with a freshly signed timestamp reuses one simulated durable identity");
        Check(await Reject<InvalidOperationException>(()=>Receive(Envelope(stamp:"changed"))),"gateway signed changed-transfer retry conflicts rather than silently replacing retained evidence");
        await Receive(Envelope(id:Guid.NewGuid()));Check(store.Rows.Count==2,"gateway distinct transfer ids preserve identical bodies rather than pretending to identify physical punches");
        store.Revoked=true;Check(await Reject<UnauthorizedAccessException>(()=>Receive(Envelope())),"gateway store rechecks revoked grant or key even on a valid exact retry");store.Revoked=false;
        store.Fail=true;Check(await Reject<IOException>(()=>Receive(Envelope(id:Guid.NewGuid()))),"gateway commit failure produces no post-commit receipt");store.Fail=false;
        store.WrongReceipt=true;Check(await Reject<InvalidOperationException>(()=>Receive(Envelope(id:Guid.NewGuid()))),"gateway mismatched commit proof is not exposed as a successful receipt");store.WrongReceipt=false;
        store.WrongFingerprint=true;Check(await Reject<InvalidOperationException>(()=>Receive(Envelope(id:Guid.NewGuid()))),"gateway commit receipt must pin stable key-version zone and request fingerprint as well as body hash");store.WrongFingerprint=false;
        using(var stopped=new CancellationTokenSource())
        {
            stopped.Cancel();var calls=store.Calls;
            Check(await Reject<OperationCanceledException>(()=>AuthenticatedPushGateway.ReceiveAsync(binding,Envelope(id:Guid.NewGuid()),store,clock,stopped.Token))&&store.Calls==calls,
                "gateway pre-cancelled reception never enters the commit store");
        }
        var malformed=Encoding.UTF8.GetBytes("bad-line\n");Check((await Receive(Envelope(id:Guid.NewGuid(),bytes:malformed))).RetainedRows==1,
            "gateway signed malformed wire rows remain retained quarantine not successful attendance processing");
        Check(await Reject<ArgumentException>(()=>Receive(Envelope(id:Guid.NewGuid(),bytes:[255]))),"gateway authenticated invalid UTF8 is rejected without a fabricated receipt");
        var heldBytes=(byte[])body.Clone();store.Hold=new(TaskCreationOptions.RunContinuationsAsynchronously);var held=Receive(Envelope(id:Guid.NewGuid(),bytes:heldBytes));
        Check(!held.IsCompleted,"gateway response waits for the commit boundary");heldBytes[0]=90;store.Hold.SetResult();await held;
        Check(store.LastBatch!.Original.Span[0]==(byte)'0',"gateway authentication and commit share frozen original bytes despite caller mutation");store.Hold=null;
        Check(!Envelope().ToString().Contains("001",StringComparison.Ordinal)&&!binding.ToString().Contains("SYNTHETIC-SERIAL",StringComparison.Ordinal),"gateway default rendering redacts identities signatures and payloads");

        var op=Guid.NewGuid();var approved=new RemoteEnrollApproval("SIM-SERIAL","SIM-MODEL","SIM-FW",1,7,"synthetic-model-only","synthetic-authorization-only","synthetic-retention-only",true);
        RemoteEnrollWorkflow Workflow(RemoteEnrollApproval? approval=null)=>new(op,"000123",approval??approved);
        var session=new EnrollSession();var blocked=Workflow(approved with{AllowEnrollment=false});
        Check(blocked.Start(session)=="blocked-policy-or-model"&&session.Reads==0&&session.Starts==0,"RemoteEnroll defaults to blocked without explicit installed-model and organizational authorization");
        Check(Workflow(approved with{RetentionAndDeletionPolicyReference=""}).Start(session)=="blocked-policy-or-model", "RemoteEnroll missing retention/deletion policy reference prevents dispatch");
        var pending=Workflow();Check(pending.Cancel(session)=="cancelled-before-dispatch"&&pending.Start(session)=="cancelled-before-dispatch"&&session.Starts==0,
            "RemoteEnroll pending cancellation actually prevents any enrollment dispatch");
        var mismatch=new EnrollSession{Identity=new("other","SIM-MODEL","SIM-FW")};Check(Workflow().Start(mismatch)=="identity-mismatch"&&mismatch.Starts==0,"RemoteEnroll live serial pin mismatch cannot enroll a subject");
        Check(Workflow().Start(new EnrollSession{Present=null})=="presence-unavailable"&&Workflow().Start(new EnrollSession{Present=true})=="existing-slot-refused",
            "RemoteEnroll unavailable slot is not absence and existing templates cannot be overwritten");
        var workflow=Workflow();Check(workflow.Start(session)=="awaiting-subject"&&session.LastPerson=="000123"&&session.LastFlag==1,
            "RemoteEnroll StartEnrollEx ACK preserves leading-zero identity and means awaiting subject not completed");
        workflow.Start(session);Check(session.Starts==1,"RemoteEnroll repeated start on one operation never dispatches twice");
        var done=new RemoteEnrollCompletion(op,"000123",1,7,64);
        Check(workflow.ObserveCompletion(done with{OperationId=Guid.NewGuid()},session)=="awaiting-subject"&&
            workflow.ObserveCompletion(done with{PersonIdentifier="123"},session)=="awaiting-subject"&&
            workflow.ObserveCompletion(done with{FingerIndex=2},session)=="awaiting-subject","RemoteEnroll foreign operation target and finger callbacks cannot complete another intent");
        session.Present=true;Check(workflow.ObserveCompletion(done,session)=="completed","RemoteEnroll correlated model-success callback needs a fresh pinned non-exporting slot-presence check");
        Check(workflow.Cancel(session)=="completed"&&session.Cancels==0,"RemoteEnroll terminal completion is not rewritten by a later cancel");
        var unverified=Workflow();var empty=new EnrollSession();unverified.Start(empty);Check(unverified.ObserveCompletion(done,empty)=="unknown","RemoteEnroll callback without independently present slot remains unknown");
        var wrongCode=Workflow();var c=new EnrollSession();wrongCode.Start(c);c.Present=true;Check(wrongCode.ObserveCompletion(done with{ActionCode=0},c)=="unknown","RemoteEnroll does not assume a generic success action code");
        var cancelled=Workflow();var cancelling=new EnrollSession();cancelled.Start(cancelling);
        Check(cancelled.Cancel(cancelling)=="unknown"&&cancelling.Cancels==1&&cancelled.Cancel(cancelling)=="unknown"&&cancelling.Cancels==1&&
            cancelled.ObserveCompletion(done,cancelling)=="unknown","RemoteEnroll cancel after dispatch stays unknown even with true SDK ACK and late completion cannot overwrite it");
        var expired=Workflow();var waiting=new EnrollSession();expired.Start(waiting);Check(expired.DeadlineExpired()=="unknown"&&expired.Start(waiting)=="unknown"&&waiting.Starts==1,"RemoteEnroll deadline leaves unknown and forbids blind retry");
        var negative=Workflow();var falseStart=new EnrollSession{StartResult=false};Check(negative.Start(falseStart)=="unknown"&&falseStart.Starts==1,"RemoteEnroll false start return is not proof that enrollment never began");
        var throwing=Workflow();Check(throwing.Start(new EnrollSession{ThrowStart=true})=="unknown","RemoteEnroll exception after dispatch preserves uncertainty");
        Check(!workflow.ToString().Contains("000123",StringComparison.Ordinal)&&!done.ToString().Contains("000123",StringComparison.Ordinal),"RemoteEnroll state and callback default rendering redact subject and never contain template bytes");
        var approvalWire=new RemoteEnrollApproval("SYNTHETIC-SERIAL","SYNTHETIC-PRODUCT","SYNTHETIC-FIRMWARE",1,0,"SYNTHETIC-model-only","SYNTHETIC-authority-only","SYNTHETIC-retention-only",true,-404,65535);
        var wire=new RemoteEnrollInput(new(Guid.NewGuid(),"192.0.2.31",4370,1,approvalWire.Serial,0),Guid.NewGuid(),"001",approvalWire,1);
        var executable=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));var relay=typeof(GatewayEnrollmentTests).Assembly.Location;
        Check(wire.IsValid()&&!(wire with{Approval=approvalWire with{EmptySlotErrorCode=null}}).IsValid()&&!(wire with{Approval=approvalWire with{EventMask=0}}).IsValid(),"Enrollment wire requires explicitly approved absence semantics and event mask");
        Check((await NativeProcessSupervisor.RunEnrollAsync(executable,relay,wire,6)).State=="completed","Enrollment supervisor accepts exact correlated synthetic child completion");
        Check((await NativeProcessSupervisor.RunEnrollAsync(executable,relay,wire with{Approval=approvalWire with{ModelEvidence="SYNTHETIC-extra"}},6)).State=="unknown","Enrollment supervisor rejects child template export or extra properties as unknown");
        using(var cancelledToken=new CancellationTokenSource()){cancelledToken.Cancel();Check((await NativeProcessSupervisor.RunEnrollAsync(executable,relay,wire,6,cancelledToken.Token)).State=="cancelled-before-dispatch","Enrollment pre-cancelled supervisor never launches a child");}
        var killTimer=System.Diagnostics.Stopwatch.StartNew();
        Check((await NativeProcessSupervisor.RunEnrollAsync(executable,relay,wire with{Approval=approvalWire with{ModelEvidence="SYNTHETIC-hang"}},6)).State=="unknown"&&killTimer.Elapsed<TimeSpan.FromSeconds(20),"Enrollment hard process deadline terminates hanging synthetic child and never advertises safe retry");
        Console.WriteLine($"PASS {count} synthetic gateway/enrollment checks; no HTTP SQL COM device or real biometric policy execution; synthetic child supervisor exercised");
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")=="true")
            Console.WriteLine($"::notice title=Synthetic gateway and enrollment checks::{count} checks passed; gateway protocol and enrollment lifecycle and synthetic child supervisor only, no HTTP SQL COM native-device authentication or real biometrics.");
    }
    private sealed class Clock(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}
    private sealed class Store:IPushGatewayCommitStore
    {
        public int Calls;public bool Revoked,Fail,WrongReceipt,WrongFingerprint;public TaskCompletionSource? Hold;public ZkPushAttendanceBatch? LastBatch;
        public Dictionary<(Guid,Guid,Guid),(string Stable,PushGatewayCommit Receipt)> Rows=new();
        public async Task<PushGatewayCommit> CommitAsync(PushGatewayBinding b,Guid id,ZkPushAttendanceBatch batch,CancellationToken ct)
        {
            Calls++;LastBatch=batch;if(Revoked)throw new UnauthorizedAccessException();if(Fail)throw new IOException("synthetic commit failure");
            if(Hold is not null)await Hold.Task.WaitAsync(ct);
            var key=(b.GatewayId,b.DeviceId,id);var stable=System.Text.Json.JsonSerializer.Serialize(new{b.SourceId,b.Serial,b.KeyVersion,b.Zone.Id,batch.Stamp,batch.PayloadHash,Original=Convert.ToBase64String(batch.Original.Span)});
            if(Rows.TryGetValue(key,out var old)){if(old.Stable!=stable)throw new InvalidOperationException("changed transfer");return old.Receipt;}
            var receipt=new PushGatewayCommit(Guid.NewGuid(),b.GatewayId,b.DeviceId,b.SourceId,id,batch.PayloadHash,batch.Rows.Count,AuthenticatedPushGateway.TransferFingerprint(b,id,batch));
            Rows.Add(key,(stable,receipt));return WrongReceipt?receipt with{DeviceId=Guid.NewGuid()}:WrongFingerprint?receipt with{RequestFingerprint=new string('0',64)}:receipt;
        }
    }
    private sealed class EnrollSession:IRemoteEnrollSession
    {
        public RemoteEnrollIdentity Identity=new("SIM-SERIAL","SIM-MODEL","SIM-FW");public bool? Present=false;public bool StartResult=true,ThrowStart;
        public int Reads,Starts,Cancels,LastFlag;public string? LastPerson;
        public RemoteEnrollIdentity ReadIdentity(){Reads++;return Identity;}
        public bool? SlotPresent(string person,int index)=>Present;
        public bool StartEnrollEx(string person,int index,int flag){Starts++;LastPerson=person;LastFlag=flag;if(ThrowStart)throw new IOException("synthetic native error");return StartResult;}
        public bool CancelOperation(){Cancels++;return true;}
    }
}
