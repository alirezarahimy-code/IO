using System;
using TWF.Domain.Models;

namespace TWF.Domain.Interfaces
{
    /// <summary>
    /// Provides authenticated encryption (AES-256-CBC + HMAC-SHA256) for session tokens.
    /// Replaces MyTime3.Authorization.Encryption (which used a hardcoded 8-byte DES key without MAC - BUG-02).
    /// </summary>
    public interface IAuthTokenService
    {
        /// <summary>
        /// Encrypts and signs a <see cref="SessionTokenPayload"/> into a URL-safe token string.
        /// </summary>
        string ProtectToken(SessionTokenPayload payload);

        /// <summary>
        /// Verifies the HMAC-SHA256 signature, decrypts the token, and checks expiration against <paramref name="utcNow"/>.
        /// </summary>
        bool TryUnprotectToken(string protectedToken, DateTime utcNow, out SessionTokenPayload payload);
    }
}
