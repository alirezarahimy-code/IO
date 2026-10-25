using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Application.Services
{
    /// <summary>
    /// Append-only audit trail with a hash chain.
    ///
    /// Why not [SystemLog]: that table belongs to the engine (docs/02, R1) and must not
    /// grow portal-specific columns. Why a hash chain at all: the most damaging admin
    /// incident is not a mistake, it is a quiet edit of the access matrix. Linking each
    /// entry to its predecessor means a deleted or altered row breaks the chain and is
    /// detectable offline with no external service.
    /// </summary>
    public sealed class AuditService
    {
        private readonly IAdminRepository _repository;

        public AuditService(IAdminRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }
            _repository = repository;
        }

        public AuditEntry Record(string actor, AdminFeature feature, string action, string target, string detail)
        {
            string previous = _repository.GetLastAuditHash() ?? string.Empty;
            var entry = new AuditEntry
            {
                UtcTime = DateTime.UtcNow,
                Actor = string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim(),
                Feature = feature,
                Action = action ?? string.Empty,
                Target = target ?? string.Empty,
                Detail = detail ?? string.Empty,
                PreviousHash = previous
            };
            entry.Hash = ComputeHash(entry, previous);
            _repository.AppendAudit(entry);
            return entry;
        }

        public IReadOnlyList<AuditEntry> Recent(int take, AdminFeature? feature = null)
        {
            if (take <= 0 || take > 500)
            {
                take = 100;
            }
            return _repository.GetAudit(take, feature);
        }

        /// <summary>
        /// Recomputes the chain. Returns the index of the first broken entry, or -1 when
        /// the whole visible history is intact.
        /// </summary>
        public int VerifyChain(IReadOnlyList<AuditEntry> newestFirst)
        {
            if (newestFirst == null || newestFirst.Count == 0)
            {
                return -1;
            }

            string expectedPrevious = string.Empty;
            for (int i = newestFirst.Count - 1; i >= 0; i--)
            {
                AuditEntry entry = newestFirst[i];
                if (!string.Equals(entry.PreviousHash ?? string.Empty, expectedPrevious, StringComparison.Ordinal))
                {
                    return i;
                }
                string recomputed = ComputeHash(entry, entry.PreviousHash);
                if (!string.Equals(recomputed, entry.Hash, StringComparison.Ordinal))
                {
                    return i;
                }
                expectedPrevious = entry.Hash;
            }
            return -1;
        }

        public static string ComputeHash(AuditEntry entry, string previousHash)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }
            string payload = string.Join("|", new[]
            {
                (previousHash ?? string.Empty),
                entry.UtcTime.ToString("O", CultureInfo.InvariantCulture),
                entry.Actor,
                ((int)entry.Feature).ToString(CultureInfo.InvariantCulture),
                entry.Action,
                entry.Target,
                entry.Detail
            });

            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }
    }
}
