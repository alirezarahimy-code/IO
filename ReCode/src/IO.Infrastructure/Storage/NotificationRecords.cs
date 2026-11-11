namespace IO.Infrastructure.Storage;

/// <summary>In-app notification for a request decision; the recipient reads and marks it read.</summary>
public sealed class NotificationRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    // Protocol tag of the originating workflow, not a display name: 'correction' or 'leave'.
    public string Category { get; set; } = null!;
    public string Text { get; set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ReadAtUtc { get; set; }
}
