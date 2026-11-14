namespace IO.DeviceContracts;

/// <summary>One enrollment per isolated child; approval must be pinned by trusted deployment configuration, never inferred from a browser.</summary>
public sealed record RemoteEnrollInput(ZkProbeInput Connection,Guid OperationId,string PersonIdentifier,RemoteEnrollApproval Approval,int SubjectWaitSeconds,bool Enroll=true)
{
    public bool IsValid()=>Enroll&&Connection?.IsValid()==true&&OperationId!=Guid.Empty&&PersonIdentifier is{Length:>=1 and <=100}&&PersonIdentifier.All(char.IsAsciiDigit)&&Approval?.IsValid()==true&&Approval.EmptySlotErrorCode is not null and not 0 and not int.MinValue&&Approval.EventMask>0&&Approval.Serial==Connection.ExpectedSerial&&SubjectWaitSeconds is >=1 and <=45;
    public override string ToString()=>"Remote enrollment request [credentials and subject redacted]";
}
public sealed record RemoteEnrollOutput(string State,RemoteEnrollCompletion? Completion=null)
{
    public bool IsValidFor(RemoteEnrollInput input)=>State=="completed"?
        Completion is{} c&&c.OperationId==input.OperationId&&c.PersonIdentifier==input.PersonIdentifier&&c.FingerIndex==input.Approval.FingerIndex&&c.ActionCode==input.Approval.SuccessActionCode&&c.TemplateLength is >0 and <=1048576:
        Completion is null&&State is "unknown" or "unsupported" or "identity-mismatch" or "presence-unavailable" or "existing-slot-refused" or "failed-before-dispatch" or "cancelled-before-dispatch" or "blocked-policy-or-model";
    public override string ToString()=>"Remote enrollment result [no template bytes]";
}
