using T5Web.Domain.Models;

namespace AriaAdmin.Web.Security;

// Only the IIS/Windows principal supplied by the web host is trusted in production.
// There is deliberately no request-header, password, cookie or environment bypass.
public interface IAdminIdentitySource
{
    string? GetWindowsUser(HttpContext context);
}

internal sealed class IisWindowsIdentitySource : IAdminIdentitySource
{
    public string? GetWindowsUser(HttpContext context)
    {
        var identity = context.User.Identity;
        if (identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(identity.Name)) return null;
        // Do not accept Basic/cookie/JWT identities in a Windows-auth-only product.
        if (identity.AuthenticationType is not ("Negotiate" or "Kerberos" or "NTLM" or "Windows")) return null;
        return identity.Name;
    }
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
    Task<DirectoryResolution> ResolveAsync(string windowsName, CancellationToken cancellationToken);
}

// No SQL provider has been approved from an offline local feed yet. Every
// authenticated production request fails 503 instead of guessing access rights.
internal sealed class UnavailableAdminDirectory : IAdminDirectory
{
    public Task<DirectoryResolution> ResolveAsync(string windowsName, CancellationToken cancellationToken) =>
        Task.FromResult(DirectoryResolution.Unavailable());
}

public static class PortalSession
{
    private static readonly object Key = new();
    public static AdminSession? Get(HttpContext context) =>
        context.Items.TryGetValue(Key, out var value) ? value as AdminSession : null;
    internal static void Set(HttpContext context, AdminSession session) => context.Items[Key] = session;
}
