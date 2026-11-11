using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Notifications;

/// <summary>Own-data in-app notifications; any authenticated user reads only their own rows.</summary>
public sealed class Notifications(IoDbContext db)
{
    public async Task<object> ListAsync(Guid actor, int offset, CancellationToken ct = default)
    {
        if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
        var unread = await db.Notifications.CountAsync(x => x.UserId == actor && x.ReadAtUtc == null, ct);
        var items = await db.Notifications.AsNoTracking().Where(x => x.UserId == actor)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Skip(offset).Take(50)
            .Select(x => new { x.Id, x.Category, x.Text, x.CreatedAtUtc, x.ReadAtUtc }).ToListAsync(ct);
        return new { offset, pageSize = 50, unread, items };
    }

    public Task<int> MarkAllReadAsync(Guid actor, CancellationToken ct = default) =>
        db.Notifications.Where(x => x.UserId == actor && x.ReadAtUtc == null)
            .ExecuteUpdateAsync(setter => setter.SetProperty(x => x.ReadAtUtc, DateTimeOffset.UtcNow), ct);
}
