namespace IO.Domain.Workflows;

public enum RequestKind { AttendanceCorrection, Leave, Mission, Overtime }
public enum RequestState { Draft, Pending, Approved, Rejected, Cancelled }
public sealed record ApprovalAction(Guid ActorId, string Action, DateTimeOffset AtUtc, string? Reason, int Step);

/// <summary>
/// In-memory decision rules only. Callers must authenticate, authorize, persist
/// changes atomically, and resolve delegates before invoking this aggregate.
/// </summary>
public sealed class ApprovalRequest
{
    private readonly Guid[][] steps;
    private readonly List<ApprovalAction> history = [];
    public Guid Id { get; }
    public Guid EmployeeId { get; }
    public Guid RequesterId { get; }
    public RequestKind Kind { get; }
    private readonly byte[]? payloadHash;
    public byte[]? PayloadHash => payloadHash?.ToArray();
    public RequestState State { get; private set; } = RequestState.Draft;
    public int CurrentStep { get; private set; }
    public IReadOnlyList<ApprovalAction> History => history.AsReadOnly();

    public ApprovalRequest(Guid id, Guid employeeId, Guid requesterId, RequestKind kind, IEnumerable<IEnumerable<Guid>> approvers, byte[]? payloadHash = null)
    {
        if (id == Guid.Empty || employeeId == Guid.Empty || requesterId == Guid.Empty || !Enum.IsDefined(kind))
            throw new ArgumentException("Invalid request identity or kind.");
        ArgumentNullException.ThrowIfNull(approvers);
        steps = approvers.Select(s => s?.Distinct().ToArray() ?? []).ToArray();
        if (steps.Length == 0 || steps.Any(s => s.Length == 0 || s.Any(id => id == Guid.Empty)))
            throw new ArgumentException("Every approval step needs at least one designated actor.");
        if (kind == RequestKind.AttendanceCorrection && payloadHash is not { Length: 32 })
            throw new ArgumentException("Correction requires a SHA-256 content hash.");
        if (payloadHash is not null && payloadHash.Length != 32) throw new ArgumentException("Invalid content hash.");
        this.payloadHash = payloadHash?.ToArray();
        Id = id; EmployeeId = employeeId; RequesterId = requesterId; Kind = kind;
    }
    private static void Validate(Guid actor, DateTimeOffset atUtc)
    {
        if (actor == Guid.Empty || atUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Actor and UTC time required.");
    }
    private void Add(Guid actor, string action, DateTimeOffset atUtc, string? reason) =>
        history.Add(new(actor, action, atUtc, reason, CurrentStep));
    public void Submit(Guid actor, DateTimeOffset atUtc)
    {
        Validate(actor, atUtc);
        if (State != RequestState.Draft || actor != RequesterId) throw new InvalidOperationException("Cannot submit request.");
        State = RequestState.Pending; Add(actor, "submitted", atUtc, null);
    }
    public void Approve(Guid actor, DateTimeOffset atUtc)
    {
        Validate(actor, atUtc); RequireApprover(actor);
        Add(actor, "approved", atUtc, null); CurrentStep++;
        if (CurrentStep == steps.Length) State = RequestState.Approved;
    }
    public void Reject(Guid actor, DateTimeOffset atUtc, string reason)
    {
        Validate(actor, atUtc); RequireApprover(actor); ValidateReason(reason);
        Add(actor, "rejected", atUtc, reason); State = RequestState.Rejected;
    }
    public void Cancel(Guid actor, DateTimeOffset atUtc, string reason)
    {
        Validate(actor, atUtc); ValidateReason(reason);
        if (actor != RequesterId || State is not (RequestState.Draft or RequestState.Pending))
            throw new InvalidOperationException("Cannot cancel request.");
        Add(actor, "cancelled", atUtc, reason); State = RequestState.Cancelled;
    }
    private void RequireApprover(Guid actor)
    {
        if (State != RequestState.Pending || !steps[CurrentStep].Contains(actor))
            throw new InvalidOperationException("Actor is not an approver at the current step.");
    }
    internal static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500 || reason.Any(char.IsControl))
            throw new ArgumentException("Reason is required and must be at most 500 characters.");
    }
}
