using System.Data;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IO.Infrastructure.Security;

/// <summary>Changes only the authenticated actor's own password.</summary>
public sealed class PersonalCredentials(LoginIdentityDbContext identity, IoDbContext foundation, UserManager<LoginAccount> users)
{
    public async Task ChangePasswordAsync(Guid actor, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        if (actor == Guid.Empty || string.IsNullOrEmpty(currentPassword) || currentPassword.Length > 256 ||
            string.IsNullOrEmpty(newPassword) || newPassword.Length > 256 ||
            string.Equals(currentPassword, newPassword, StringComparison.Ordinal)) throw new ArgumentException("Invalid input.");
        await using var tx = await identity.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await foundation.Database.CloseConnectionAsync();
        foundation.Database.SetDbConnection(identity.Database.GetDbConnection());
        await foundation.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
        if (!await foundation.Users.AnyAsync(x => x.Id == actor && x.IsActive, ct)) throw new UnauthorizedAccessException();
        var login = await users.FindByIdAsync(actor.ToString());
        if (login is null) throw new UnauthorizedAccessException();
        var result = await users.ChangePasswordAsync(login, currentPassword, newPassword);
        if (!result.Succeeded) throw new ArgumentException("Credential change rejected.");
        var required = (await users.GetClaimsAsync(login)).Where(x => x.Type == IdentityServices.MustChangePasswordClaim).ToArray();
        foreach (var claim in required)
            if (!(await users.RemoveClaimAsync(login, claim)).Succeeded)
                throw new InvalidOperationException("Unable to clear password-change requirement.");
        identity.SecurityAudit.Add(new SecurityAuditRecord { Id = Guid.NewGuid(), ActorUserId = actor,
            Action = "identity.password.changed:" + actor.ToString("N"), OccurredAtUtc = DateTimeOffset.UtcNow });
        await identity.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
