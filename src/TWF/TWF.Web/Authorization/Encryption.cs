using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MyTime3.Authorization
{
    /// <summary>
    /// Rewritten Encryption service for MyTime3.Authorization (fixing BUG-02).
    /// Replaces legacy 8-byte DES (Key=[124,32,132,56,145,122,68,194]) with authenticated
    /// AES-256-CBC + HMAC-SHA256 using random 16-byte IV per encryption and constant-time MAC verification.
    /// </summary>
    public static class Encryption
    {
        private const string TokenPrefix = "AES256v1$";
        private static byte[] _encryptionKey;
        private static byte[] _macKey;
        private static readonly object KeyLock = new object();

        static Encryption()
        {
            ConfigureMasterSecret("TWF-Enterprise-Offline-Default-Master-Secret-Key-2026!@#");
        }

        public static void ConfigureMasterSecret(string masterSecret)
        {
            if (string.IsNullOrWhiteSpace(masterSecret))
                throw new ArgumentException("Master secret cannot be empty.", nameof(masterSecret));

            lock (KeyLock)
            {
                using (var sha = SHA256.Create())
                {
                    _encryptionKey = sha.ComputeHash(Encoding.UTF8.GetBytes("ENC|" + masterSecret));
                    _macKey = sha.ComputeHash(Encoding.UTF8.GetBytes("MAC|" + masterSecret));
                }
            }
        }

        public static string Encrypt(string PlainText)
        {
            if (PlainText == null)
                return string.Empty;

            byte[] plainBytes = Encoding.UTF8.GetBytes(PlainText);
            byte[] iv = new byte[16];
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

                using (var ms = new MemoryStream())
                using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(plainBytes, 0, plainBytes.Length);
                    cs.FlushFinalBlock();
                    cipherBytes = ms.ToArray();
                }
            }

            byte[] payload = new byte[iv.Length + cipherBytes.Length];
            Buffer.BlockCopy(iv, 0, payload, 0, iv.Length);
            Buffer.BlockCopy(cipherBytes, 0, payload, iv.Length, cipherBytes.Length);

            byte[] mac;
            using (var hmac = new HMACSHA256(_macKey))
            {
                mac = hmac.ComputeHash(payload);
            }

            return TokenPrefix + ToBase64Url(payload) + "." + ToBase64Url(mac);
        }

        public static string Decrypt(string CypherText)
        {
            if (string.IsNullOrWhiteSpace(CypherText))
                return string.Empty;

            try
            {
                if (!CypherText.StartsWith(TokenPrefix, StringComparison.Ordinal))
                {
                    return string.Empty;
                }

                string body = CypherText.Substring(TokenPrefix.Length);
                int dotIndex = body.IndexOf('.');
                if (dotIndex <= 0 || dotIndex >= body.Length - 1)
                {
                    return string.Empty;
                }

                byte[] payload = FromBase64Url(body.Substring(0, dotIndex));
                byte[] providedMac = FromBase64Url(body.Substring(dotIndex + 1));

                if (payload.Length <= 16)
                {
                    return string.Empty;
                }

                byte[] computedMac;
                using (var hmac = new HMACSHA256(_macKey))
                {
                    computedMac = hmac.ComputeHash(payload);
                }

                if (!ConstantTimeEquals(providedMac, computedMac))
                {
                    return string.Empty;
                }

                byte[] iv = new byte[16];
                byte[] cipherBytes = new byte[payload.Length - 16];
                Buffer.BlockCopy(payload, 0, iv, 0, 16);
                Buffer.BlockCopy(payload, 16, cipherBytes, 0, cipherBytes.Length);

                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.BlockSize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = _encryptionKey;
                    aes.IV = iv;

                    using (var ms = new MemoryStream(cipherBytes))
                    using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    using (var sr = new StreamReader(cs, Encoding.UTF8))
                    {
                        return sr.ReadToEnd();
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
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
            string padded = input.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }
    }
}
