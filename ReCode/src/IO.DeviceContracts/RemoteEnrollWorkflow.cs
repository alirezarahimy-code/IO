namespace IO.DeviceContracts;

/// <summary>Explicit installed-model/policy approval supplied by a FUTURE trusted deployment caller.
/// Mere text references do not establish organizational authority. No real-device adapter is wired here.</summary>
public sealed record RemoteEnrollApproval(string Serial,string Product,string Firmware,int FingerIndex,int SuccessActionCode,
    string ModelEvidence,string AuthorizationReference,string RetentionAndDeletionPolicyReference,bool AllowEnrollment=false)
{
    public bool IsValid()=>AllowEnrollment&&FingerIndex is >=0 and <=9&&
        new[]{Serial,Product,Firmware,ModelEvidence,AuthorizationReference,RetentionAndDeletionPolicyReference}.All(x=>PushGatewayBinding.Text(x,200));
    public override string ToString()=>"Remote enrollment approval [subject, identity and policy references redacted]";
}
public sealed record RemoteEnrollIdentity(string Serial,string Product,string Firmware);
public sealed record RemoteEnrollCompletion(Guid OperationId,string PersonIdentifier,int FingerIndex,int ActionCode,int TemplateLength)
{
    public override string ToString()=>"Remote enrollment callback [subject redacted; no template bytes]";
}
public interface IRemoteEnrollSession
{
    RemoteEnrollIdentity ReadIdentity();
    // Only presence, never fingerprint bytes. null means unavailable, NOT empty. No overwrite authorization.
    bool? SlotPresent(string person,int fingerIndex);
    bool StartEnrollEx(string person,int fingerIndex,int flag);
    bool CancelOperation();
}

/// <summary>Single-dispatch lifecycle for a future exclusive per-device child session.
/// Current use is synthetic doubles only; no web/worker/COM route instantiates this class.
/// ACK != completed; cancel-after-dispatch != cancellation proof; uncertain work is never retried.</summary>
public sealed class RemoteEnrollWorkflow
{
    public const int ReferenceStartFlag=1;
    private readonly object gate=new();
    private readonly Guid operationId;
    private readonly string person;
    private readonly RemoteEnrollApproval approval;
    private string state="pending";
    public string State {get{lock(gate)return state;}}
    public RemoteEnrollWorkflow(Guid operationId,string person,RemoteEnrollApproval approval)
    {
        if(operationId==Guid.Empty||person is null||person.Length is 0 or >100||!person.All(char.IsAsciiDigit)||approval is null)
            throw new ArgumentException("Explicit operation, exact numeric string identity and approval required.");
        this.operationId=operationId;this.person=person;this.approval=approval;
    }
    public string Start(IRemoteEnrollSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock(gate)
        {
            if(state!="pending")return state;
            if(!approval.IsValid())return state="blocked-policy-or-model";
            try
            {
                if(!Pinned(session.ReadIdentity()))return state="identity-mismatch";
                var exists=session.SlotPresent(person,approval.FingerIndex);
                if(exists is null)return state="presence-unavailable";
                if(exists==true)return state="existing-slot-refused";
            }
            catch{return state="failed-before-dispatch";}
            state="dispatching";
            try{state=session.StartEnrollEx(person,approval.FingerIndex,ReferenceStartFlag)?"awaiting-subject":"unknown";}
            catch{state="unknown";}
            return state;
        }
    }
    public string ObserveCompletion(RemoteEnrollCompletion completion,IRemoteEnrollSession session)
    {
        ArgumentNullException.ThrowIfNull(completion);ArgumentNullException.ThrowIfNull(session);
        lock(gate)
        {
            if(state!="awaiting-subject"||completion.OperationId!=operationId||completion.PersonIdentifier!=person||completion.FingerIndex!=approval.FingerIndex)
                return state;
            // The success action code belongs to the approved model, not a hardcoded generic vendor convention.
            if(completion.ActionCode!=approval.SuccessActionCode||completion.TemplateLength<=0)return state="unknown";
            try
            {
                // Recheck pinned identity and independently observe a newly present slot. No template export.
                state=Pinned(session.ReadIdentity())&&session.SlotPresent(person,approval.FingerIndex)==true?"completed":"unknown";
            }
            catch{state="unknown";}
            return state;
        }
    }
    public string Cancel(IRemoteEnrollSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock(gate)
        {
            if(state=="pending")return state="cancelled-before-dispatch";
            if(state!="awaiting-subject")return state;
            state="unknown";try{session.CancelOperation();}catch{ /* Preserve uncertainty, including a true/false SDK result. */ }
            return state;
        }
    }
    public string DeadlineExpired(){lock(gate){if(state is "dispatching" or "awaiting-subject")state="unknown";return state;}}
    private bool Pinned(RemoteEnrollIdentity value)=>value is not null&&value.Serial==approval.Serial&&value.Product==approval.Product&&value.Firmware==approval.Firmware;
    public override string ToString()=>"Remote enrollment lifecycle [subject and identity redacted]";
}
