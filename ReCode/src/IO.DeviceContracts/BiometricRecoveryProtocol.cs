namespace IO.DeviceContracts;
/// <summary>Installed non-destructive reset/quiescence procedure; never enrollment recovery or proof that an unknown copy succeeded.</summary>
public sealed record BiometricRecoveryApproval(BiometricTransferApproval Transfer,string ResetProcedureReference,int MinimumSettlingSeconds,bool ResetClearsPendingWrites=false,bool AllowRecovery=false)
{
 public bool IsValid()=>Transfer?.IsValid()==true&&AllowRecovery&&ResetClearsPendingWrites&&MinimumSettlingSeconds is >=10 and <=300&&!string.IsNullOrWhiteSpace(ResetProcedureReference)&&ResetProcedureReference.Length<=200&&!ResetProcedureReference.Any(char.IsControl);
 public override string ToString()=>"Biometric recovery approval [references redacted]";
}
