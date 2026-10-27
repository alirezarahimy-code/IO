using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Aria.Web.Security;

/// <summary>
/// UNWIRED fresh-session primitive, never the original unauthenticated DES
/// mytimeauth cookie. Requires BOTH an externally provisioned protected key
/// ring and a durable atomic revocation store before HTTP/production use.
/// There is deliberately no default in-memory/file store in the published Web.
/// </summary>
internal sealed class FreshAriaSessionManager
{
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(20);
    private readonly IDataProtector _protector;
    private readonly IAriaSessionRegistry _registry;
    private readonly TimeProvider _clock;

    internal FreshAriaSessionManager(IDataProtectionProvider keys, IAriaSessionRegistry registry,
        TimeProvider clock)
    {
        _protector = (keys ?? throw new ArgumentNullException(nameof(keys)))
            .CreateProtector("Aria.Web", "fresh-session", "v1");
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    internal async Task<string?> IssueAsync(VerifiedAriaAccount account, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (account == null || account.BadgeNumber <= 0 ||
            string.IsNullOrWhiteSpace(account.AccountName) || account.AccountName.Length > 100 ||
            account.AccountName != account.AccountName.Trim() || account.AccountName.Any(char.IsControl))
            return null;
        var issued = _clock.GetUtcNow().ToUniversalTime();
        var expires = issued.Add(MaximumLifetime);
        var id = Guid.NewGuid();
        var record = new AriaSessionRecord(id, account, expires, Revoked: false);
        // No token is returned on store failure, and production has NO registry.
        try
        {
            if (!await _registry.TryCreateAsync(record, cancellationToken)) return null;
            var ticket = new SessionTicket(1, id, account.AccountName, account.BadgeNumber,
                issued.UtcTicks, expires.UtcTicks);
            return WebEncoders.Base64UrlEncode(
                _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(ticket)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return null; } // No token from an unavailable registry/key ring.
    }

    internal async Task<FreshAriaSessionResolution> VerifyAsync(string? token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SessionTicket? ticket;
        try
        {
            if (!TryDecode(token, out ticket) || ticket is null)
                return FreshAriaSessionResolution.Denied();
        }
        catch (Exception)
        {
            // A broken key ring must never be treated as a verified ticket.
            return FreshAriaSessionResolution.Unavailable();
        }
        var now = _clock.GetUtcNow().ToUniversalTime();
        if (ticket.Version != 1 || ticket.Id == Guid.Empty || ticket.Badge <= 0 ||
            string.IsNullOrWhiteSpace(ticket.Name) || ticket.Name.Length > 100 ||
            ticket.Name != ticket.Name.Trim() || ticket.Name.Any(char.IsControl) ||
            ticket.IssuedUtcTicks < DateTime.MinValue.Ticks ||
            ticket.ExpiresUtcTicks > DateTime.MaxValue.Ticks ||
            ticket.IssuedUtcTicks > now.UtcTicks || ticket.ExpiresUtcTicks <= now.UtcTicks ||
            ticket.ExpiresUtcTicks <= ticket.IssuedUtcTicks ||
            ticket.ExpiresUtcTicks - ticket.IssuedUtcTicks > MaximumLifetime.Ticks)
            return FreshAriaSessionResolution.Denied();
        try
        {
            AriaSessionRecord? active = await _registry.FindAsync(ticket.Id, cancellationToken);
            if (active == null || active.Revoked || active.Id != ticket.Id ||
                active.ExpiresUtc.ToUniversalTime().UtcTicks != ticket.ExpiresUtcTicks ||
                active.Account.BadgeNumber != ticket.Badge ||
                !string.Equals(active.Account.AccountName, ticket.Name, StringComparison.Ordinal))
                return FreshAriaSessionResolution.Denied();
            return FreshAriaSessionResolution.Verified(active.Account, ticket.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return FreshAriaSessionResolution.Unavailable(); }
    }

    internal async Task<FreshAriaRevokeState> RevokeAsync(string? token,
        CancellationToken cancellationToken)
    {
        var verified = await VerifyAsync(token, cancellationToken);
        if (verified.State != FreshAriaSessionState.Verified || verified.SessionId is null)
            return verified.State == FreshAriaSessionState.Unavailable
                ? FreshAriaRevokeState.Unavailable : FreshAriaRevokeState.Invalid;
        try
        {
            bool revoked = await _registry.TryRevokeAsync(verified.SessionId.Value, cancellationToken);
            return revoked ? FreshAriaRevokeState.Revoked : FreshAriaRevokeState.Unavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return FreshAriaRevokeState.Unavailable; }
    }

    private bool TryDecode(string? token, out SessionTicket? ticket)
    {
        ticket = null;
        if (string.IsNullOrEmpty(token) || token.Length > 4096) return false;
        try
        {
            byte[] payload = _protector.Unprotect(WebEncoders.Base64UrlDecode(token));
            ticket = JsonSerializer.Deserialize<SessionTicket>(payload);
            return ticket != null;
        }
        catch (Exception error) when (error is FormatException or CryptographicException or
            JsonException or ArgumentException)
        {
            return false;
        }
    }

    private sealed record SessionTicket(int Version, Guid Id, string Name, long Badge,
        long IssuedUtcTicks, long ExpiresUtcTicks);
}

/// <summary>
/// Production implementations MUST persist across processes, atomically insert
/// and revoke, protect identifiers at rest and fail closed on storage errors.
/// An in-memory test implementation is NEVER registered in published Web.
/// </summary>
internal interface IAriaSessionRegistry
{
    Task<bool> TryCreateAsync(AriaSessionRecord record, CancellationToken cancellationToken);
    Task<AriaSessionRecord?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> TryRevokeAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed record AriaSessionRecord(Guid Id, VerifiedAriaAccount Account,
    DateTimeOffset ExpiresUtc, bool Revoked);
internal enum FreshAriaSessionState { Unavailable, Denied, Verified }
internal enum FreshAriaRevokeState { Unavailable, Invalid, Revoked }
internal sealed record FreshAriaSessionResolution(FreshAriaSessionState State,
    VerifiedAriaAccount? Account, Guid? SessionId)
{
    internal static FreshAriaSessionResolution Unavailable() =>
        new(FreshAriaSessionState.Unavailable, null, null);
    internal static FreshAriaSessionResolution Denied() =>
        new(FreshAriaSessionState.Denied, null, null);
    internal static FreshAriaSessionResolution Verified(VerifiedAriaAccount account, Guid id) =>
        new(FreshAriaSessionState.Verified, account, id);
}
