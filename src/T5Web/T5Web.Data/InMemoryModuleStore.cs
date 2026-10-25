using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// In-memory module store for the tests.
    ///
    /// It enforces the same rules the SQL store does - unique keys, a key change is a
    /// different row, delete must match exactly one row - so a service test exercises
    /// the same failure paths the database would raise.
    /// </summary>
    public sealed class InMemoryModuleStore : IModuleStore
    {
        private readonly Dictionary<string, List<ModuleRow>> _tables =
            new Dictionary<string, List<ModuleRow>>(StringComparer.OrdinalIgnoreCase);

        public InMemoryModuleStore Seed(string table, params ModuleRow[] rows)
        {
            if (rows != null && rows.Length > 0)
            {
                List<ModuleRow> list;
                if (!_tables.TryGetValue(table, out list))
                {
                    list = new List<ModuleRow>();
                    _tables[table] = list;
                }
                list.AddRange(rows);
            }
            return this;
        }

        public int Count(string table)
        {
            List<ModuleRow> list;
            return _tables.TryGetValue(table, out list) ? list.Count : 0;
        }

        private List<ModuleRow> Rows(string table)
        {
            List<ModuleRow> list;
            if (!_tables.TryGetValue(table, out list))
            {
                list = new List<ModuleRow>();
                _tables[table] = list;
            }
            return list;
        }

        private static bool Matches(ModuleDescriptor descriptor, ModuleRow row, IReadOnlyDictionary<string, string> key)
        {
            foreach (ModuleField field in descriptor.Keys)
            {
                string expected;
                if (key == null || !key.TryGetValue(field.Column, out expected))
                {
                    return false;
                }
                string actual = Convert.ToString(row[field.Column]);
                if (!string.Equals((actual ?? string.Empty).Trim(), (expected ?? string.Empty).Trim(),
                                   StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        public ModulePage Query(ModuleDescriptor descriptor, string term, int page, int pageSize)
        {
            string needle = (term ?? string.Empty).Trim();
            var filtered = Rows(descriptor.Table).AsEnumerable();
            if (needle.Length > 0)
            {
                filtered = filtered.Where(row => descriptor.Fields
                    .Where(f => f.IsTextish)
                    .Any(f => (Convert.ToString(row[f.Column]) ?? string.Empty)
                        .IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0));
            }

            List<ModuleRow> all = filtered.ToList();
            string orderBy = string.IsNullOrWhiteSpace(descriptor.OrderBy) ? descriptor.Key.Column : descriptor.OrderBy;
            List<ModuleRow> ordered = all
                .OrderBy(row => Convert.ToString(row[orderBy]), StringComparer.CurrentCulture)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return new ModulePage
            {
                Rows = ordered,
                Total = all.Count,
                Page = page,
                PageSize = pageSize,
                Term = term
            };
        }

        public ModuleRow Get(ModuleDescriptor descriptor, IReadOnlyDictionary<string, string> key)
        {
            return Rows(descriptor.Table).FirstOrDefault(row => Matches(descriptor, row, key));
        }

        public OperationResult Save(ModuleDescriptor descriptor, ModuleRow row, bool isNew)
        {
            if (row == null)
            {
                return OperationResult.Fail("سطر خالی است.");
            }

            List<ModuleRow> rows = Rows(descriptor.Table);
            if (isNew)
            {
                if (rows.Any(existing => Matches(descriptor, existing, AsKey(descriptor, row))))
                {
                    return OperationResult.Fail("سطر با این کلید از قبل وجود دارد.");
                }
                rows.Add(row);
                return OperationResult.Ok();
            }

            ModuleRow current = rows.FirstOrDefault(existing => Matches(descriptor, existing, AsKey(descriptor, row)));
            if (current == null)
            {
                return OperationResult.Fail("سطر مورد نظر یافت نشد.");
            }
            foreach (ModuleField field in descriptor.Editable)
            {
                current[field.Column] = row[field.Column];
            }
            return OperationResult.Ok();
        }

        public OperationResult Delete(ModuleDescriptor descriptor, IReadOnlyDictionary<string, string> key)
        {
            List<ModuleRow> rows = Rows(descriptor.Table);
            int removed = rows.RemoveAll(row => Matches(descriptor, row, key));
            return removed > 0
                ? OperationResult.Ok()
                : OperationResult.Fail("سطر مورد نظر یافت نشد.");
        }

        public IReadOnlyList<KeyValuePair<string, string>> Options(ModuleDescriptor descriptor)
        {
            string label = string.IsNullOrWhiteSpace(descriptor.LabelColumn)
                ? descriptor.Key.Column
                : descriptor.LabelColumn;
            return Rows(descriptor.Table)
                .Select(row => new KeyValuePair<string, string>(
                    Convert.ToString(row[descriptor.Key.Column]),
                    Convert.ToString(row[label])))
                .OrderBy(pair => pair.Value, StringComparer.CurrentCulture)
                .ToList();
        }

        private static IReadOnlyDictionary<string, string> AsKey(ModuleDescriptor descriptor, ModuleRow row)
        {
            var key = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModuleField field in descriptor.Keys)
            {
                key[field.Column] = Convert.ToString(row[field.Column]);
            }
            return key;
        }
    }
}
