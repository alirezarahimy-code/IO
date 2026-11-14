namespace IO.DeviceContracts;
/// <summary>Deployment-approved physical recovery, not a remote cancel success claim.</summary>
public sealed record RemoteEnrollmentRecoveryApproval(RemoteEnrollApproval Enrollment,string PhysicalResetProcedureReference,int MinimumSettlingSeconds,bool ResetClearsPendingEnrollment=false,bool AllowRecovery=false)
{
    public bool IsValid()=>AllowRecovery&&ResetClearsPendingEnrollment&&Enrollment?.IsValid()==true&&
        PushGatewayBinding.Text(PhysicalResetProcedureReference,200)&&MinimumSettlingSeconds is >=10 and <=300;
    public override string ToString()=>"Installed enrollment recovery approval [references redacted]";
}
