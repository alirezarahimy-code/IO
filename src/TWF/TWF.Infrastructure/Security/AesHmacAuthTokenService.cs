using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TWF.Domain.Interfaces;
using TWF.Domain.Models;

namespace TWF.Infrastructure.Security
{
    /// <summary>
    /// Authenticated Encryption (AES-256-CBC + HMAC-SHA256, Encrypt-then-MAC) implementation of <see cref="IAuthTokenService"/>.
    /// Replaces the insecure hardcoded 8-byte DES key (7C208438917A44C2) in MyTime3.Authorization.Encryption (BUG-02).
    /// </summary>
    public sealed class AesHmacAuthTokenService : IAuthTokenService
    {
        private const byte TokenVersion = 0x01;
        private const int IvSize = 16;
        private const int MacSize = 32;
        private const char FieldSeparator = '\x1F'; // ASCII Unit Separator to avoid collisions with usernames

        private readonly byte[] _encryptionKey;
        private readonly byte[] _macKey;

        /// <summary>
        /// Initializes the token service using a master secret.
        /// Derives two cryptographically independent 256-bit keys (one for AES-256 encryption, one for HMAC-SHA256 signing).
        /// </summary>
        public AesHmacAuthTokenService(string masterSecret = null)
        {
            string effectiveSecret = string.IsNullOrWhiteSpace(masterSecret)
                ? GetDefaultMachineBoundSecret()
                : masterSecret;

            using (var sha = SHA256.Create())
            {
                _encryptionKey = sha.ComputeHash(Encoding.UTF8.GetBytes("TWF.AES256.ENC.v1|" + effectiveSecret));
                _macKey = sha.ComputeHash(Encoding.UTF8.GetBytes("TWF.HMAC256.MAC.v1|" + effectiveSecret));
            }
        }

        public string ProtectToken(SessionTokenPayload payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            string serialized = string.Join(
                FieldSeparator.ToString(),
                payload.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                payload.UserName ?? string.Empty,
                payload.Role ?? "user",
                payload.IssuedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                payload.ExpiresAtUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                payload.Nonce ?? Guid.NewGuid().ToString("N"));

            byte[] plainBytes = Encoding.UTF8.GetBytes(serialized);
            byte[] iv = new byte[IvSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(iv);
            }

            byte[] cipherBytes;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = _encryptionKey;
                aes.IV = iv;

                using (var encryptor = aes.CreateEncryptor())
                {
                    cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                }
            }

            // Envelope layout: [Version (1B)] [IV (16B)] [Ciphertext (NB)] [HMAC-SHA256 (32B)]
            int bodyLength = 1 + IvSize + cipherBytes.Length;
            byte[] envelope = new byte[bodyLength + MacSize];
            envelope[0] = TokenVersion;
            Buffer.BlockCopy(iv, 0, envelope, 1, IvSize);
            Buffer.BlockCopy(cipherBytes, 0, envelope, 1 + IvSize, cipherBytes.Length);

            using (var hmac = new HMACSHA256(_macKey))
            {
                byte[] tag = hmac.ComputeHash(envelope, 0, bodyLength);
                Buffer.BlockCopy(tag, 0, envelope, bodyLength, MacSize);
            }

            return ToBase64Url(envelope);
        }

        public bool TryUnprotectToken(string protectedToken, DateTime utcNow, out SessionTokenPayload payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(protectedToken))
                return false;

            byte[] envelope;
            try
            {
                envelope = FromBase64Url(protectedToken);
            }
            catch
            {
                return false;
            }

            int minEnvelopeLength = 1 + IvSize + 16 + MacSize;
            if (envelope == null || envelope.Length < minEnvelopeLength)
                return false;

            if (envelope[0] != TokenVersion)
                return false;

            int bodyLength = envelope.Length - MacSize;
            byte[] expectedTag;
            using (var hmac = new HMACSHA256(_macKey))
            {
                expectedTag = hmac.ComputeHash(envelope, 0, bodyLength);
            }

            // Constant-time MAC verification before decryption (prevents padding oracle attacks)
            if (!ConstantTimeEquals(envelope, bodyLength, expectedTag, 0, MacSize))
                return false;

            byte[] iv = new byte[IvSize];
            Buffer.BlockCopy(envelope, 1, iv, 0, IvSize);

            int cipherLength = bodyLength - 1 - IvSize;
            if (cipherLength <= 0 || (cipherLength % 16) != 0)
                return false;

            byte[] plainBytes;
            try
            {
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.BlockSize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = _encryptionKey;
                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor())
                    {
                        plainBytes = decryptor.TransformFinalBlock(envelope, 1 + IvSize, cipherLength);
                    }
                }
            }
            catch
            {
                return false;
            }

            string decoded = Encoding.UTF8.GetString(plainBytes);
            string[] parts = decoded.Split(FieldSeparator);
            if (parts.Length != 6)
                return false;

            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long badgeNumber) ||
                !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long issuedTicks) ||
                !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long expiresTicks))
            {
                return false;
            }

            var issuedAtUtc = new DateTime(issuedTicks, DateTimeKind.Utc);
            var expiresAtUtc = new DateTime(expiresTicks, DateTimeKind.Utc);

            var candidate = new SessionTokenPayload(
                badgeNumber: badgeNumber,
                userName: parts[1],
                role: parts[2],
                issuedAtUtc: issuedAtUtc,
                expiresAtUtc: expiresAtUtc,
                nonce: parts[5]);

            if (candidate.IsExpired(utcNow))
                return false;

            payload = candidate;
            return true;
        }

        private static bool ConstantTimeEquals(byte[] a, int aOffset, byte[] b, int bOffset, int length)
        {
            int diff = 0;
            for (int i = 0; i < length; i++)
            {
                diff |= a[aOffset + i] ^ b[bOffset + i];
            }
            return diff == 0;
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] FromBase64Url(string input)
        {
            string base64 = input.Trim().Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }

        private static string GetDefaultMachineBoundSecret()
        {
            string machine = Environment.MachineName ?? "TWF-HOST";
            return $"TWF-Enterprise-ZeroLock-Secret-v5.0::{machine}::BodePanjenrin";
        }
    }
}
