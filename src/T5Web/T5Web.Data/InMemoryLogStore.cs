using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// In-memory log reader for the tests and the offline demo.
    ///
    /// It reproduces the three behaviours the SQL store guarantees: the window filter,
    /// the "newest first" order and the take cap. T32 asserts all three.
    /// </summary>
    public sealed class InMemoryLogStore : ILogStore
    {
        private readonly List<LogEntryRow> _rows = new List<LogEntryRow>();
        private readonly List<LogSourceDescriptor> _sources = new List<LogSourceDescriptor>();

        public InMemoryLogStore Source(string code, string title, string table, string catalog)
        {
            _sources.Add(new LogSourceDescriptor
            {
                Code = code,
                TitleFa = title,
                Table = table,
                Catalog = catalog
            });
            return this;
        }

        public InMemoryLogStore Add(string source, DateTime time, long badge, int machine,
                                    string kind, string detail)
        {
            _rows.Add(new LogEntryRow
            {
                Source = source,
                Time = time,
                BadgeNumber = badge,
                MachineNumber = machine,
                KindFa = kind,
                Detail = detail
            });
            return this;
        }

        public IReadOnlyList<LogSourceDescriptor> Sources()
        {
            return _sources;
        }

        public IReadOnlyList<LogEntryRow> Search(string source, DateTime from, DateTime to,
                                                 long? badgeNumber, string term, int take)
        {
            IEnumerable<LogEntryRow> query = _rows;
            if (!string.IsNullOrWhiteSpace(source))
            {
                query = query.Where(r => string.Equals(r.Source, source, StringComparison.OrdinalIgnoreCase));
            }
            query = query.Where(r => r.Time.HasValue &&
                                     r.Time.Value.Date >= from.Date &&
                                     r.Time.Value.Date <= to.Date);
            if (badgeNumber.HasValue && badgeNumber.Value > 0)
            {
                query = query.Where(r => r.BadgeNumber == badgeNumber.Value);
            }
            if (!string.IsNullOrWhiteSpace(term))
            {
                string needle = term.Trim();
                query = query.Where(r =>
                    (r.Detail ?? string.Empty).IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (r.KindFa ?? string.Empty).IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0);
            }

            int limit = take <= 0 ? 100 : Math.Min(take, SqlLogStore.MaxTake);
            return query.OrderByDescending(r => r.Time).Take(limit).ToList();
        }
    }
}
