using TWF.Domain.Models;

namespace Aria.Web.Security;

// TWF 4.28's "mytimeauth" cookie must be verified against its ORIGINAL format,
// secret provisioning and Personnel/T5 state before any protected action executes.
// The current TWF rewrite has *two* incompatible session models (minute-based
// MVC LoginManager and hour-based Domain SessionManager). No silent Core cookie
// substitution or hard-coded default master secret is permitted here.
public enum SessionDecision { Unavailable, Anonymous, Denied, Verified }

// The same verified shared account supplies both name and positive personnel
// badge. A separately hydrated session cannot silently replace either value.
// No real credential/cookie verifier is registered in the published product.
public sealed record VerifiedAriaAccount(string AccountName, long BadgeNumber);

public sealed record SessionResolution(SessionDecision Decision, VerifiedAriaAccount? Account,
    UserSessionIdentity? Identity)
{
    public static SessionResolution Unavailable() => new(SessionDecision.Unavailable, null, null);
    public static SessionResolution Anonymous() => new(SessionDecision.Anonymous, null, null);
    public static SessionResolution Denied() => new(SessionDecision.Denied, null, null);
    public static SessionResolution Verified(VerifiedAriaAccount account, UserSessionIdentity identity) =>
        new(SessionDecision.Verified, account, identity);
}

public interface IAriaSessionVerifier
{
    Task<SessionResolution> VerifyAsync(HttpContext context, CancellationToken cancellationToken);
}

// This is the ONLY production implementation until original token/binary golden
// tests, secret provisioning, active-user access and two-catalog SQL are proved.
// It never decrypts or accepts a cookie, HTTP header, environment bypass, or key.
internal sealed class UnavailableLegacySessionVerifier : IAriaSessionVerifier
{
    public Task<SessionResolution> VerifyAsync(HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(SessionResolution.Unavailable());
}

public static class AriaRequestIdentity
{
    private static readonly object Key = new();
    public static UserSessionIdentity? Get(HttpContext context) =>
        context.Items.TryGetValue(Key, out var value) ? value as UserSessionIdentity : null;
    internal static void Set(HttpContext context, UserSessionIdentity identity) => context.Items[Key] = identity;
}
