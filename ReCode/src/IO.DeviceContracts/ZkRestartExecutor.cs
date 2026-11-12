namespace IO.DeviceContracts;

/// <summary>Standalone SDK RestartDevice only. No factory reset, data clear, or door-control fallback.</summary>
public interface IZkRestartSession
{
    bool Restart();
}

public static class ZkRestartExecutor
{
    // Caller must connect and verify the configured serial before invoking this boundary.
    // A true result is only an SDK acknowledgement, not independent evidence of a completed reboot.
    public static ZkOperationOutput Execute(bool approved, IZkRestartSession session)
    {
        if (!approved) return new("unsupported");
        try { return new(session.Restart() ? "succeeded" : "unknown"); }
        catch { return new("unknown"); }
    }
}
