using T5Web.Domain.Models;

namespace AriaAdmin.Web.Security;

// Final identity must come from the SAME verified user/account used by the
// existing system, not a second Admin or Windows password. The source must
// never treat a request header, raw name or an unverified cookie as proof.
// The original three-application credential/session contract is not verified
// yet, so the publishable implementation always reports Unavailable.
public enum AccountState { Unavailable, Anonymous, Verified }

// The Personnel badge comes from a successful verification of the ONE shared
// account. A name alone must never authorize access to another person's badge.
// This is not a credential or a verification implementation.
public sealed record VerifiedAccount(string AccountName, long BadgeNumber);

public sealed record AccountResolution(AccountState State, VerifiedAccount? Account)
{
    public static AccountResolution Unavailable() => new(AccountState.Unavailable, null);
    public static AccountResolution Anonymous() => new(AccountState.Anonymous, null);
    public static AccountResolution Verified(string accountName, long badgeNumber) =>
        new(AccountState.Verified, new VerifiedAccount(accountName, badgeNumber));
}

public interface IAdminIdentitySource
{
    Task<AccountResolution> ResolveAsync(HttpContext context, CancellationToken cancellationToken);
}

internal sealed class UnavailableSharedAccountSource : IAdminIdentitySource
{
    public Task<AccountResolution> ResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(AccountResolution.Unavailable());
}

public enum DirectoryState { Unavailable, Denied, Allowed }

public sealed record DirectoryResolution(DirectoryState State, AdminSession? Session)
{
    public static DirectoryResolution Unavailable() => new(DirectoryState.Unavailable, null);
    public static DirectoryResolution Denied() => new(DirectoryState.Denied, null);
    public static DirectoryResolution Allowed(AdminSession session) => new(DirectoryState.Allowed, session);
}

public interface IAdminDirectory
{
    // Name AND badge must come from the same verified shared-account result.
    // This directory checks roles/availability; it does not verify a password.
    Task<DirectoryResolution> ResolveAsync(VerifiedAccount verifiedAccount, CancellationToken cancellationToken);
}

// No SQL provider or verified shared-account login contract has been approved
// yet. Production cannot infer a user's permissions from an unverified name.
internal sealed class UnavailableAdminDirectory : IAdminDirectory
{
    public Task<DirectoryResolution> ResolveAsync(VerifiedAccount verifiedAccount, CancellationToken cancellationToken) =>
        Task.FromResult(DirectoryResolution.Unavailable());
}

public static class PortalSession
{
    private static readonly object Key = new();
    public static AdminSession? Get(HttpContext context) =>
        context.Items.TryGetValue(Key, out var value) ? value as AdminSession : null;
    internal static void Set(HttpContext context, AdminSession session) => context.Items[Key] = session;
}
