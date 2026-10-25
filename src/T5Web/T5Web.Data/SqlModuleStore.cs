using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// Generic table access for the module screens, over the shared T5 database.
    ///
    /// Safety model (this is the only place in the portal that builds SQL text):
    ///   1. a table or column name is *never* taken from a request - it comes from a
    ///      ModuleDescriptor, which is a compile-time object;
    ///   2. every identifier is re-checked against a strict pattern here, and the whole
    ///      descriptor is checked against the schema manifest by test T16, so a typo
    ///      fails the build instead of the shift;
    ///   3. every *value* is a Dapper parameter.
    ///
    /// Paging uses ROW_NUMBER() rather than OFFSET/FETCH: the engine databases are old
    /// enough that OFFSET support cannot be assumed, and a paging failure would look
    /// like "no data" to an operator.
    /// </summary>
    public sealed class SqlModuleStore : IModuleStore
    {
        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);

        private readonly Func<IDbConnection> _connectionFactory;

        public SqlModuleStore(Func<IDbConnection> connectionFactory)
        {
            if (connectionFactory == null)
            {
                throw new ArgumentNullException(nameof(connectionFactory));
            }
            _connectionFactory = connectionFactory;
        }

        private IDbConnection Open()
        {
            IDbConnection connection = _connectionFactory();
            connection.Open();
            return connection;
        }

        private static string Quote(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier) || !Identifier.IsMatch(identifier))
            {
                throw new ArgumentException("شناسه نامعتبر در تعریف ماژول: " + identifier);
            }
            return "[" + identifier + "]";
        }

        private static string ColumnList(ModuleDescriptor descriptor)
        {
            return string.Join(", ", descriptor.Fields.Select(f => Quote(f.Column)));
        }

        private static string KeyPredicate(ModuleDescriptor descriptor, out List<string> parameters)
        {
            parameters = descriptor.Keys.Select((field, index) => "@k" + index).ToList();
            int position = 0;
            return string.Join(" AND ", descriptor.Keys.Select(field => Quote(field.Column) + " = @k" + position++));
        }

        public ModulePage Query(ModuleDescriptor descriptor, string term, int page, int pageSize)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            string search = string.Empty;
            var searchColumns = descriptor.Fields.Where(f => f.IsTextish && !f.IsKey).ToList();
            if (!string.IsNullOrWhiteSpace(term) && searchColumns.Count > 0)
            {
                search = " WHERE " + string.Join(" OR ",
                    searchColumns.Select(f => "CAST(" + Quote(f.Column) + " AS nvarchar(400)) LIKE @term"));
            }

            string table = Quote(descriptor.Table);
            string orderBy = Quote(string.IsNullOrWhiteSpace(descriptor.OrderBy) ? descriptor.Key.Column : descriptor.OrderBy);

            string countSql = "SELECT COUNT(1) FROM " + table + search + ";";
            string pageSql =
                "SELECT * FROM (SELECT ROW_NUMBER() OVER (ORDER BY " + orderBy + ") AS __rn, " +
                ColumnList(descriptor) + " FROM " + table + search + ") AS paged " +
                "WHERE __rn BETWEEN @from AND @to ORDER BY __rn;";

            var arguments = new DynamicParameters();
            arguments.Add("term", "%" + (term ?? string.Empty).Trim() + "%");
            arguments.Add("from", ((page - 1) * pageSize) + 1);
            arguments.Add("to", page * pageSize);

            using (IDbConnection connection = Open())
            {
                int total = connection.ExecuteScalar<int>(countSql, arguments);
                List<IDictionary<string, object>> raw = connection.Query(pageSql, arguments)
                    .Cast<IDictionary<string, object>>()
                    .ToList();

                var rows = raw.Select(source =>
                {
                    var row = new ModuleRow();
                    foreach (ModuleField field in descriptor.Fields)
                    {
                        object value;
                        row[field.Column] = source.TryGetValue(field.Column, out value) ? value : null;
                    }
                    return row;
                }).ToList();

                return new ModulePage
                {
                    Rows = rows,
                    Total = total,
                    Page = page,
                    PageSize = pageSize,
                    Term = term
                };
            }
        }

        public ModuleRow Get(ModuleDescriptor descriptor, IReadOnlyDictionary<string, string> key)
        {
            var arguments = new DynamicParameters();
            string predicate;
            BuildKeyArguments(descriptor, key, arguments, out predicate);

            string sql = "SELECT TOP 1 " + ColumnList(descriptor) + " FROM " + Quote(descriptor.Table) +
                         " WHERE " + predicate + ";";

            using (IDbConnection connection = Open())
            {
                IDictionary<string, object> source = connection.Query(sql, arguments)
                    .Cast<IDictionary<string, object>>()
                    .FirstOrDefault();
                if (source == null)
                {
                    return null;
                }
                var row = new ModuleRow();
                foreach (ModuleField field in descriptor.Fields)
                {
                    object value;
                    row[field.Column] = source.TryGetValue(field.Column, out value) ? value : null;
                }
                return row;
            }
        }

        public OperationResult Save(ModuleDescriptor descriptor, ModuleRow row, bool isNew)
        {
            if (row == null)
            {
                return OperationResult.Fail("سطر خالی است.");
            }

            var arguments = new DynamicParameters();
            try
            {
                if (isNew)
                {
                    var columns = new List<string>();
                    var values = new List<string>();
                    foreach (ModuleField field in descriptor.Fields)
                    {
                        if (field.Identity)
                        {
                            continue;
                        }
                        columns.Add(Quote(field.Column));
                        values.Add("@p" + (values.Count));
                        arguments.Add("p" + (values.Count - 1), Normalize(row[field.Column], field));
                    }

                    string sql = "INSERT INTO " + Quote(descriptor.Table) +
                                 " (" + string.Join(", ", columns) + ") VALUES (" + string.Join(", ", values) + ");";

                    using (IDbConnection connection = Open())
                    {
                        connection.Execute(sql, arguments);
                    }
                    return OperationResult.Ok();
                }

                var assignments = new List<string>();
                foreach (ModuleField field in descriptor.Editable)
                {
                    assignments.Add(Quote(field.Column) + " = @p" + assignments.Count);
                    arguments.Add("p" + (assignments.Count - 1), Normalize(row[field.Column], field));
                }
                if (assignments.Count == 0)
                {
                    return OperationResult.Fail("این ماژول ستون قابل ویرایش ندارد.");
                }

                string predicate;
                BuildKeyArguments(descriptor, KeyValues(descriptor, row), arguments, out predicate);

                string update = "UPDATE " + Quote(descriptor.Table) + " SET " +
                                string.Join(", ", assignments) + " WHERE " + predicate + ";";

                using (IDbConnection connection = Open())
                {
                    int affected = connection.Execute(update, arguments);
                    return affected == 1
                        ? OperationResult.Ok()
                        : OperationResult.Fail("سطر مورد نظر یافت نشد (ممکن است هم‌زمان حذف شده باشد).");
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("ثبت تغییرات ناموفق بود: " + Clean(ex.Message));
            }
        }

        public OperationResult Delete(ModuleDescriptor descriptor, IReadOnlyDictionary<string, string> key)
        {
            var arguments = new DynamicParameters();
            string predicate;
            try
            {
                BuildKeyArguments(descriptor, key, arguments, out predicate);
            }
            catch (ArgumentException ex)
            {
                return OperationResult.Fail(ex.Message);
            }

            string sql = "DELETE FROM " + Quote(descriptor.Table) + " WHERE " + predicate + ";";
            try
            {
                using (IDbConnection connection = Open())
                {
                    int affected = connection.Execute(sql, arguments);
                    return affected >= 1
                        ? OperationResult.Ok()
                        : OperationResult.Fail("سطر مورد نظر یافت نشد.");
                }
            }
            catch (Exception ex)
            {
                // A foreign key that still points at this row is an operational fact,
                // not a crash: report it in the operator's language.
                return OperationResult.Fail("حذف ناموفق بود (احتمالاً این سطر در جای دیگری استفاده شده است): " + Clean(ex.Message));
            }
        }

        public IReadOnlyList<KeyValuePair<string, string>> Options(ModuleDescriptor descriptor)
        {
            string keyColumn = descriptor.Key.Column;
            string labelColumn = string.IsNullOrWhiteSpace(descriptor.LabelColumn) ? keyColumn : descriptor.LabelColumn;
            string sql = "SELECT " + Quote(keyColumn) + " AS K, " + Quote(labelColumn) + " AS L FROM " +
                         Quote(descriptor.Table) + " ORDER BY " + Quote(labelColumn) + ";";

            using (IDbConnection connection = Open())
            {
                return connection.Query(sql)
                    .Cast<IDictionary<string, object>>()
                    .Select(source => new KeyValuePair<string, string>(
                        Convert.ToString(source["K"], CultureInfo.InvariantCulture) ?? string.Empty,
                        Convert.ToString(source["L"], CultureInfo.InvariantCulture) ?? string.Empty))
                    .ToList();
            }
        }

        // ------------------------------------------------------------------ helpers

        private static void BuildKeyArguments(ModuleDescriptor descriptor, IReadOnlyDictionary<string, string> key,
                                              DynamicParameters arguments, out string predicate)
        {
            var parts = new List<string>();
            int position = 0;
            foreach (ModuleField field in descriptor.Keys)
            {
                string value;
                if (key == null || !key.TryGetValue(field.Column, out value) || string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("کلید سطر ناقص است: " + field.LabelFa);
                }
                string name = "k" + position++;
                parts.Add(Quote(field.Column) + " = @" + name);
                arguments.Add(name, Normalize(value, field));
            }
            predicate = string.Join(" AND ", parts);
        }

        private static IReadOnlyDictionary<string, string> KeyValues(ModuleDescriptor descriptor, ModuleRow row)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModuleField field in descriptor.Keys)
            {
                values[field.Column] = Convert.ToString(row[field.Column], CultureInfo.InvariantCulture);
            }
            return values;
        }

        /// <summary>CLR value for a field: text keys stay text, numbers become numbers.</summary>
        private static object Normalize(object value, ModuleField field)
        {
            if (value == null || value == DBNull.Value)
            {
                return null;
            }
            if (value is string)
            {
                string text = ((string)value).Trim();
                if (text.Length == 0)
                {
                    return null;
                }
                switch (field.Kind)
                {
                    case FieldKind.Number:
                    {
                        long integer;
                        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                        {
                            return integer;
                        }
                        double real;
                        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out real))
                        {
                            return real;
                        }
                        break;
                    }
                    case FieldKind.Boolean:
                        return ModuleRow.IsTrue(text);
                    case FieldKind.Date:
                    {
                        DateTime date;
                        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                        {
                            return date;
                        }
                        break;
                    }
                    case FieldKind.Time:
                    {
                        TimeSpan time;
                        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out time))
                        {
                            return time;
                        }
                        break;
                    }
                }
                return text;
            }
            if (value is bool && field.Kind == FieldKind.Number)
            {
                return ModuleRow.IsTrue(value) ? 1L : 0L;
            }
            return value;
        }

        /// <summary>Drops the SQL Server prefix so a failure reads like a sentence.</summary>
        private static string Clean(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }
            string first = message.Split('\n')[0].Trim();
            return first.Length > 220 ? first.Substring(0, 220) + "…" : first;
        }
    }
}
