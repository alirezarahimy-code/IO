namespace IO.Infrastructure.Calculation;

/// <summary>
/// Coarse database-wide detection only. Equality never establishes freshness,
/// complete data, legal correctness, period approval, or unchanged software/tzdata.
/// </summary>
public static class CalculationInputRevisionStatus
{
    public static string Evaluate(long? recorded, long? current) => (recorded, current) switch
    {
        (null, _) => "untracked",
        (_, null) => "unavailable",
        (< 0, _) or (_, < 0) => "inconsistent",
        (var prior, var now) when prior > now => "inconsistent",
        (var prior, var now) when prior != now => "monitored-database-changed",
        _ => "no-monitored-database-change-detected"
    };
}
