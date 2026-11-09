namespace IO.Infrastructure.Security;

/// <summary>A validated business conflict, safe to map to HTTP 409.</summary>
public sealed class DomainConflictException(string message) : InvalidOperationException(message);
