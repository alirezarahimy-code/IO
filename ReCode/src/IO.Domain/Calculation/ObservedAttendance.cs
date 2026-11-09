using IO.Domain.Workflows;
using System.Security.Cryptography;

namespace IO.Domain.Calculation;

public enum PunchKind { In, Out }
public sealed record AttendancePoint(Guid Id, DateTimeOffset AtUtc, PunchKind Kind);
public sealed class AttendanceResult
{
    public Guid EmployeeId { get; }
    public DateOnly WorkDate { get; }
    public Guid RuleVersion { get; }
    public IReadOnlyList<Guid> InputIds { get; }
    public IReadOnlyList<Guid> AppliedRequestIds { get; }
    public TimeSpan? ObservedPresence { get; }
    public IReadOnlyList<string> Anomalies { get; }
    internal AttendanceResult(Guid employeeId, DateOnly workDate, Guid ruleVersion, IReadOnlyList<Guid> inputIds,
        IReadOnlyList<Guid> appliedRequestIds, TimeSpan? observedPresence, IReadOnlyList<string> anomalies)
        => (EmployeeId, WorkDate, RuleVersion, InputIds, AppliedRequestIds, ObservedPresence, Anomalies) =
            (employeeId, workDate, ruleVersion, inputIds, appliedRequestIds, observedPresence, anomalies);
}

/// <summary>An approved correction suppresses explicit points and adds explicit points without changing raw data.</summary>
public sealed class ApprovedCorrection
{
    public Guid RequestId { get; }
    public Guid EmployeeId { get; }
    public IReadOnlyList<Guid> SuppressIds { get; }
    public IReadOnlyList<AttendancePoint> Additions { get; }
    public ApprovedCorrection(ApprovalRequest request, IEnumerable<Guid> suppressIds, IEnumerable<AttendancePoint> additions)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind != RequestKind.AttendanceCorrection || request.State != RequestState.Approved)
            throw new InvalidOperationException("Only approved attendance corrections can affect calculation.");
        RequestId = request.Id; EmployeeId = request.EmployeeId;
        SuppressIds = Array.AsReadOnly((suppressIds ?? throw new ArgumentNullException(nameof(suppressIds))).ToArray());
        Additions = Array.AsReadOnly((additions ?? throw new ArgumentNullException(nameof(additions))).ToArray());
        if (!CryptographicOperations.FixedTimeEquals(request.PayloadHash!, ComputePayloadHash(SuppressIds, Additions)))
            throw new InvalidOperationException("Correction content differs from the approved request.");
    }
    public static byte[] ComputePayloadHash(IEnumerable<Guid> suppressIds, IEnumerable<AttendancePoint> additions)
    {
        var suppressed = (suppressIds ?? throw new ArgumentNullException(nameof(suppressIds))).ToArray();
        var added = (additions ?? throw new ArgumentNullException(nameof(additions))).ToArray();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("IO.AttendanceCorrection.v1");
            writer.Write(suppressed.Length); foreach (var id in suppressed) writer.Write(id.ToByteArray());
            writer.Write(added.Length); foreach (var p in added)
            {
                writer.Write(p.Id.ToByteArray()); writer.Write(p.AtUtc.UtcTicks); writer.Write((int)p.Kind);
                writer.Write(p.AtUtc.Offset.Ticks);
            }
        }
        return SHA256.HashData(stream.ToArray());
    }
}

/// <summary>
/// Experimental paired-punch elapsed time, not payroll or legal work time.
/// Selection of trusted raw points and the calculation window is a caller responsibility.
/// </summary>
public static class ObservedAttendance
{
    public static AttendanceResult Calculate(Guid employeeId, DateOnly workDate, Guid ruleVersion, DateTimeOffset startUtc, DateTimeOffset endUtc,
        IEnumerable<AttendancePoint> raw, IEnumerable<ApprovedCorrection> corrections)
    {
        if (employeeId == Guid.Empty || ruleVersion == Guid.Empty || startUtc.Offset != TimeSpan.Zero ||
            endUtc.Offset != TimeSpan.Zero || startUtc >= endUtc) throw new ArgumentException("Invalid calculation context.");
        var original = (raw ?? throw new ArgumentNullException(nameof(raw))).ToArray();
        var approved = (corrections ?? throw new ArgumentNullException(nameof(corrections))).ToArray();
        if (approved.Any(c => c is null || c.EmployeeId != employeeId) || approved.Select(c => c.RequestId).Distinct().Count() != approved.Length)
            throw new ArgumentException("Correction belongs to another employee or is duplicated.");
        var rawIds = original.Select(x => x.Id).ToArray();
        if (rawIds.Any(id => id == Guid.Empty) || rawIds.Distinct().Count() != rawIds.Length)
            throw new ArgumentException("Raw point identities must be unique.");
        var suppress = approved.SelectMany(x => x.SuppressIds).ToArray();
        if (suppress.Distinct().Count() != suppress.Length || suppress.Any(x => !rawIds.Contains(x)))
            throw new ArgumentException("Suppressed raw point must exist exactly once.");
        var points = original.Where(x => !suppress.Contains(x.Id)).Concat(approved.SelectMany(x => x.Additions)).ToArray();
        if (points.Any(x => x.Id == Guid.Empty || x.AtUtc.Offset != TimeSpan.Zero || !Enum.IsDefined(x.Kind) || x.AtUtc < startUtc || x.AtUtc >= endUtc) ||
            points.Select(x => x.Id).Distinct().Count() != points.Length || points.Any(x => suppress.Contains(x.Id)))
            throw new ArgumentException("Invalid, duplicate, suppressed or out-of-window point.");
        var sorted = points.OrderBy(x => x.AtUtc).ThenBy(x => x.Id).ToArray();
        var problems = new List<string>();
        if (sorted.Length == 0) problems.Add("no-punches");
        if (sorted.GroupBy(x => x.AtUtc).Any(g => g.Count() > 1)) problems.Add("simultaneous-punches");
        TimeSpan total = TimeSpan.Zero;
        for (var i = 0; i < sorted.Length; i += 2)
        {
            if (i + 1 >= sorted.Length) { problems.Add("unpaired-punch"); break; }
            if (sorted[i].Kind != PunchKind.In || sorted[i + 1].Kind != PunchKind.Out || sorted[i].AtUtc >= sorted[i + 1].AtUtc)
                problems.Add("invalid-punch-order");
            else total += sorted[i + 1].AtUtc - sorted[i].AtUtc;
        }
        return new(employeeId, workDate, ruleVersion, Array.AsReadOnly(rawIds), Array.AsReadOnly(approved.Select(x => x.RequestId).ToArray()),
            problems.Count == 0 ? total : null, problems.AsReadOnly());
    }
}
