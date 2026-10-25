using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace T5Web.Domain.Models
{
    /// <summary>How a module field is edited and rendered.</summary>
    public enum FieldKind
    {
        Text = 0,
        Number = 1,
        Boolean = 2,
        Date = 3,
        Time = 4,
        Select = 5
    }

    /// <summary>
    /// One editable or listed column of a module.
    ///
    /// <see cref="Column"/> is the *database* column name, exactly as it appears in
    /// db/schema-manifest.json; <see cref="TypeEvidence"/> names the evidence for its
    /// type (a vendor DDL statement or an engine ORM property). T16 refuses a field
    /// whose column or type cannot be proved - that is the rule that keeps BUG-01..07
    /// from coming back through a generic editor.
    /// </summary>
    public sealed class ModuleField
    {
        public string Column { get; set; }
        public string LabelFa { get; set; }
        public FieldKind Kind { get; set; }
        public bool Required { get; set; }
        public bool IsKey { get; set; }
        public bool Identity { get; set; }
        public int MaxLength { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public string HintFa { get; set; }
        public string TypeEvidence { get; set; }

        /// <summary>
        /// Shown (and exported) but never written and never used in a WHERE clause.
        ///
        /// Some legacy tables have a human label whose column type the engine does not
        /// model (Schedule.SchName, ShiftDetail.DayIndex). Refusing to *display* them
        /// would hide the row's identity from the operator; refusing to *write* them
        /// keeps the portal from binding a value it cannot prove. Display-only is that
        /// middle ground, and T16 checks the distinction.
        /// </summary>
        public bool DisplayOnly { get; set; }
        public IReadOnlyList<KeyValuePair<int, string>> Options { get; set; }

        public bool IsTextish
        {
            get { return Kind == FieldKind.Text || Kind == FieldKind.Select; }
        }

        public bool IsNumeric
        {
            get { return Kind == FieldKind.Number; }
        }

        public static ModuleField New(string column, string labelFa, FieldKind kind, string evidence)
        {
            return new ModuleField
            {
                Column = column,
                LabelFa = labelFa,
                Kind = kind,
                TypeEvidence = evidence
            };
        }
    }

    /// <summary>
    /// A generic CRUD screen: one table, its key columns, its editable columns.
    ///
    /// The legacy portal had 40+ controllers that were the same three actions over and
    /// over (Index / Edit / Delete / DeleteAll). Rather than 40 hand-written copies, the
    /// portal declares each of those tables once here; the controller, the views, the
    /// validation, the audit trail and the authorization are written once.
    /// </summary>
    public sealed class ModuleDescriptor
    {
        public string Code { get; set; }
        public string TitleFa { get; set; }
        public string GroupFa { get; set; }
        public string Table { get; set; }

        /// <summary>
        /// Which catalog of the two the table lives in: "t5" (ReportT5V3) or "personnel"
        /// (Personnel). Proved against engine/relations.json by T16; the store opens the
        /// matching connection, so a module never issues a statement on the wrong catalog
        /// (docs/09, BUG-30).
        /// </summary>
        public string Catalog { get; set; } = "t5";
        public string NoteFa { get; set; }
        public AdminFeature ViewFeature { get; set; }
        public AdminFeature EditFeature { get; set; }
        public bool AllowCreate { get; set; }
        public bool AllowDelete { get; set; }
        public bool ReadOnly { get; set; }
        public string OrderBy { get; set; }

        /// <summary>Column shown when this module is used as a picker (defaults to the key).</summary>
        public string LabelColumn { get; set; }
        public List<ModuleField> Fields { get; set; } = new List<ModuleField>();

        public IEnumerable<ModuleField> Keys
        {
            get { return Fields.Where(f => f.IsKey).OrderBy(f => Fields.IndexOf(f)); }
        }

        public IEnumerable<ModuleField> Editable
        {
            get { return Fields.Where(f => !f.IsKey && !f.DisplayOnly); }
        }

        /// <summary>
        /// True when the screen can update an existing row.
        ///
        /// A link table such as [PersonnelDeputy] or [ReportAccess] has no column besides
        /// its key, so "editing" one means deleting it and adding another. The button is
        /// hidden instead of offered and then refused by the store.
        /// </summary>
        public bool CanUpdate
        {
            get { return Editable.Any(); }
        }

        /// <summary>Fields the operator can actually type into (the form is built from this).</summary>
        public IEnumerable<ModuleField> Inputs
        {
            get { return Fields.Where(f => !f.DisplayOnly); }
        }

        public ModuleField Key
        {
            get { return Keys.FirstOrDefault(); }
        }

        public bool HasCompositeKey
        {
            get { return Keys.Count() > 1; }
        }

        public ModuleField FieldOf(string column)
        {
            return Fields.FirstOrDefault(f => string.Equals(f.Column, column, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>String used as the row key in URLs when the key is composite.</summary>
        public string KeyFrom(ModuleRow row)
        {
            return string.Join("/", Keys.Select(k => row.GetText(k.Column)));
        }

        public ModuleDescriptor With(params ModuleField[] fields)
        {
            Fields.AddRange(fields);
            return this;
        }
    }

    /// <summary>One row of a module, with the raw values storage returned.</summary>
    public sealed class ModuleRow
    {
        private readonly Dictionary<string, object> _values =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public IDictionary<string, object> Values
        {
            get { return _values; }
        }

        public object this[string column]
        {
            get
            {
                object value;
                return _values.TryGetValue(column, out value) ? value : null;
            }
            set { _values[column] = value; }
        }

        public object GetValue(string column)
        {
            return this[column];
        }

        /// <summary>Value formatted for display: never throws, never shows a CLR type name.</summary>
        public string Display(string column, FieldKind kind)
        {
            object value = this[column];
            if (value == null || value == DBNull.Value)
            {
                return "—";
            }
            if (kind == FieldKind.Boolean)
            {
                return IsTrue(value) ? "بله" : "خیر";
            }
            if (kind == FieldKind.Date)
            {
                DateTime date;
                if (DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out date))
                {
                    return JalaliText.Format(date);
                }
            }
            if (kind == FieldKind.Time)
            {
                TimeSpan time;
                if (TimeSpan.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out time))
                {
                    return time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
                }
            }
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }

        public static bool IsTrue(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return false;
            }
            if (value is bool)
            {
                return (bool)value;
            }
            if (value is string)
            {
                string text = ((string)value).Trim();
                return text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "on", StringComparison.OrdinalIgnoreCase);
            }
            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>One page of module rows.</summary>
    public sealed class ModulePage
    {
        public IReadOnlyList<ModuleRow> Rows { get; set; } = new List<ModuleRow>();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public string Term { get; set; }

        public int PageCount
        {
            get
            {
                if (PageSize <= 0)
                {
                    return 1;
                }
                return Math.Max(1, (Total + PageSize - 1) / PageSize);
            }
        }

        public bool HasPrevious
        {
            get { return Page > 1; }
        }

        public bool HasNext
        {
            get { return Page < PageCount; }
        }
    }

    /// <summary>
    /// Jalali formatting in one place.
    ///
    /// A hand-rolled conversion was wrong by a day at the 1403/1404 boundary in the ARIA
    /// rewrite (BUG-12); this calls the framework's PersianCalendar instead.
    /// </summary>
    public static class JalaliText
    {
        private static readonly PersianCalendar Calendar = new PersianCalendar();

        public static string Format(DateTime? value)
        {
            if (!value.HasValue)
            {
                return "—";
            }
            return Format(value.Value);
        }

        public static string Format(DateTime value)
        {
            try
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:0000}/{1:00}/{2:00}",
                    Calendar.GetYear(value), Calendar.GetMonth(value), Calendar.GetDayOfMonth(value));
            }
            catch (ArgumentOutOfRangeException)
            {
                return "—";
            }
        }

        public static string FormatDateTime(DateTime? value)
        {
            if (!value.HasValue)
            {
                return "—";
            }
            return Format(value.Value) + " " + value.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>Sortable text for CSV export (Gregorian ISO plus Jalali, both unambiguous).</summary>
        public static string ForExport(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return string.Empty;
            }
            if (value is DateTime)
            {
                DateTime date = (DateTime)value;
                return Format(date) + " (" + date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")";
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        /// <summary>Quotes one CSV cell; used by every export in the portal.</summary>
        public static string CsvCell(string value)
        {
            string text = value ?? string.Empty;
            if (text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return text;
            }
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        public static string CsvRow(IEnumerable<string> cells)
        {
            var builder = new StringBuilder();
            bool first = true;
            foreach (string cell in cells)
            {
                if (!first)
                {
                    builder.Append(',');
                }
                builder.Append(CsvCell(cell));
                first = false;
            }
            return builder.ToString();
        }
    }
}
