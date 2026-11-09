namespace IO.Infrastructure.Storage;

// Informational registry only: it does not transfer workflow permissions.
public sealed class DelegationRecord
{
    public Guid Id { get; set; } // client-generated idempotency key
    public Guid CompanyId { get; set; }
    public Guid PrincipalUserId { get; set; }
    public Guid PrincipalEmployeeId { get; set; }
    public Guid DelegateUserId { get; set; }
    public Guid DelegateEmployeeId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidUntilExclusive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

// Revocation is an immutable event rather than an edit of an assignment.
public sealed class DelegationRevocationRecord
{
    public Guid DelegationId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset RevokedAtUtc { get; set; }
    public string Reason { get; set; } = null!;
}

// Explicit separate grant for correction decisions. Registration alone conveys no authority.
public sealed class CorrectionDelegationGrantRecord
{
    public Guid DelegationId { get; set; }
    public Guid PrincipalUserId { get; set; }
    public DateTimeOffset GrantedAtUtc { get; set; }
}

// Immutable evidence linking the actual decision to the explicit grant.
public sealed class DelegatedCorrectionDecisionRecord
{
    public Guid DecisionId { get; set; }
    public Guid DelegationId { get; set; }
}
