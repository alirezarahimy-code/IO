using System.Collections.Generic;
using System.Linq;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// In-memory integrity store for the tests and the offline demo. It answers with the
    /// checks it was given, so T32 can assert the page's contract (a check with zero
    /// orphans reads as healthy, one with orphans shows samples) without a database.
    /// </summary>
    public sealed class InMemoryIntegrityStore : IIntegrityStore
    {
        private readonly List<IntegrityCheckRow> _checks = new List<IntegrityCheckRow>();

        public InMemoryIntegrityStore Check(string code, string title, string reference, int count,
                                            params string[] samples)
        {
            _checks.Add(new IntegrityCheckRow
            {
                Code = code,
                TitleFa = title,
                Reference = reference,
                Count = count,
                Samples = samples == null ? new List<string>() : samples.ToList()
            });
            return this;
        }

        public IReadOnlyList<IntegrityCheckRow> Run()
        {
            return _checks;
        }
    }
}
