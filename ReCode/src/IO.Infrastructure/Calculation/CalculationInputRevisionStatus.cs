namespace IO.Infrastructure.Calculation;

/// <summary>
/// Coarse database-wide detection only. Equality never establishes freshness,
/// complete data, legal correctness, period approval, or unchanged software/tzdata.
/// </summary>
public static class CalculationInputRevisionStatus
{
    public static string Evaluate(long? recorded, long? current, string? capturedMonitor, string? currentMonitor)
    {
        if (recorded is null) return "untracked";
        if (current is null) return "unavailable";
        if (recorded < 0 || current < 0 || recorded > current) return "inconsistent";
        if (capturedMonitor != CalculationMonitorHealth.Present) return "unverified-at-capture";
        if (currentMonitor != CalculationMonitorHealth.Present) return "monitor-unverified";
        return recorded != current ? "monitored-database-changed" : "no-monitored-database-change-detected";
    }
}
