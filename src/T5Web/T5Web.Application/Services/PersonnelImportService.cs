using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Application.Services
{
    /// <summary>One parsed line of an import file.</summary>
    public sealed class ImportRow
    {
        public int Line { get; set; }
        public long BadgeNumber { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string NationalNo { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int DeptId { get; set; }
        public int EmpTypeId { get; set; }
        public DateTime? HiredDate { get; set; }
        public DateTime? FiredDate { get; set; }
        public bool Exists { get; set; }

        public List<string> Problems { get; set; } = new List<string>();

        public bool Accepted
        {
            get { return Problems.Count == 0; }
        }

        public string ProblemsFa
        {
            get { return string.Join(" ", Problems); }
        }
    }

    /// <summary>Result of parsing an import file, before anything is written.</summary>
    public sealed class ImportPreview
    {
        public IReadOnlyList<ImportRow> Rows { get; set; } = new List<ImportRow>();
        public IReadOnlyList<string> FileProblems { get; set; } = new List<string>();

        public int AcceptedCount
        {
            get { return Rows.Count(r => r.Accepted); }
        }

        public int RejectedCount
        {
            get { return Rows.Count(r => !r.Accepted); }
        }

        public int UpdateCount
        {
            get { return Rows.Count(r => r.Accepted && r.Exists); }
        }

        public bool CanCommit
        {
            get { return FileProblems.Count == 0 && AcceptedCount > 0; }
        }
    }

    /// <summary>Outcome of writing the accepted rows.</summary>
    public sealed class ImportOutcome
    {
        public int Updated { get; set; }
        public int Failed { get; set; }
        public IReadOnlyList<string> Messages { get; set; } = new List<string>();

        public string SummaryFa
        {
            get
            {
                return "به‌روزرسانی‌شده: " + Updated + " · ناموفق: " + Failed;
            }
        }
    }

    /// <summary>
    /// Offline personnel import from a CSV file.
    ///
    /// The legacy ImportFromExcelController used Excel Interop, which needs Office
    /// installed on the server and executes untrusted spreadsheet content. Here the
    /// portal reads a **UTF-8 CSV** (Excel saves one with «ذخیره به‌عنوان CSV UTF-8»),
    /// validates every cell itself, previews the result, and writes only after the
    /// operator confirms.
    ///
    /// Scope decision, stated openly on the screen: import **updates** existing personnel.
    /// Adding a person is refused because the engine derives the device card number and
    /// the badge sequence on the desktop side; guessing them here would create personnel
    /// the attendance devices cannot recognise.
    /// </summary>
    public sealed class PersonnelImportService
    {
        private static readonly string[] ExpectedHeader =
        {
            "badge", "firstname", "lastname", "nationalno", "phone", "email",
            "deptid", "emptypeid", "hireddate", "fireddate"
        };

        private readonly IAdminRepository _repository;

        public PersonnelImportService(IAdminRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }
            _repository = repository;
        }

        /// <summary>Parses and validates a CSV file without writing anything.</summary>
        public ImportPreview Parse(string csv)
        {
            var preview = new ImportPreview();
            var problems = new List<string>();
            var rows = new List<ImportRow>();

            if (string.IsNullOrWhiteSpace(csv))
            {
                problems.Add("فایل خالی است.");
                preview.FileProblems = problems;
                preview.Rows = rows;
                return preview;
            }

            List<string[]> lines = SplitCsv(csv);
            if (lines.Count == 0)
            {
                problems.Add("هیچ سطری در فایل نبود.");
                preview.FileProblems = problems;
                preview.Rows = rows;
                return preview;
            }

            string[] header = lines[0].Select(c => (c ?? string.Empty).Trim().ToLowerInvariant()).ToArray();
            string headerProblem = CheckHeader(header);
            if (headerProblem != null)
            {
                problems.Add(headerProblem);
                preview.FileProblems = problems;
                preview.Rows = rows;
                return preview;
            }

            for (int i = 1; i < lines.Count; i++)
            {
                string[] cells = lines[i];
                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }
                rows.Add(BuildRow(i + 1, header, cells));
            }

            if (rows.Count == 0)
            {
                problems.Add("فایل هیچ سطر داده‌ای ندارد.");
            }
            preview.FileProblems = problems;
            preview.Rows = rows;
            return preview;
        }

        /// <summary>
        /// Serialises the preview back into the form so the confirm step can be a plain
        /// POST without server-side session state. The confirm step re-validates.
        /// </summary>
        public static string ToCanonical(IEnumerable<ImportRow> rows)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", ExpectedHeader));
            foreach (ImportRow row in rows.Where(r => r.Accepted))
            {
                builder.AppendLine(string.Join(",", new[]
                {
                    row.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                    Escape(row.FirstName),
                    Escape(row.LastName),
                    Escape(row.NationalNo),
                    Escape(row.Phone),
                    Escape(row.Email),
                    row.DeptId.ToString(CultureInfo.InvariantCulture),
                    row.EmpTypeId.ToString(CultureInfo.InvariantCulture),
                    row.HiredDate.HasValue ? row.HiredDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty,
                    row.FiredDate.HasValue ? row.FiredDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty
                }));
            }
            return builder.ToString();
        }

        /// <summary>Writes the accepted rows. Every row is re-validated here.</summary>
        public ImportOutcome Commit(string canonicalCsv)
        {
            ImportPreview preview = Parse(canonicalCsv);
            var outcome = new ImportOutcome();
            var messages = new List<string>();

            if (preview.FileProblems.Count > 0)
            {
                outcome.Failed = preview.Rows.Count;
                outcome.Messages = preview.FileProblems;
                return outcome;
            }

            foreach (ImportRow row in preview.Rows)
            {
                if (!row.Accepted)
                {
                    outcome.Failed++;
                    messages.Add("سطر " + row.Line + ": " + row.ProblemsFa);
                    continue;
                }
                if (!row.Exists)
                {
                    outcome.Failed++;
                    messages.Add("سطر " + row.Line + ": پرسنل " + row.BadgeNumber +
                                 " وجود ندارد؛ افزودن پرسنل جدید فقط از برنامه دسکتاپ ممکن است.");
                    continue;
                }

                PersonnelRow person = _repository.GetPersonnel(row.BadgeNumber);
                if (person == null)
                {
                    outcome.Failed++;
                    messages.Add("سطر " + row.Line + ": پرسنل " + row.BadgeNumber + " یافت نشد.");
                    continue;
                }

                var updated = new PersonnelRow
                {
                    BadgeNumber = person.BadgeNumber,
                    EmployeeNumber = person.EmployeeNumber,
                    FirstName = row.FirstName,
                    LastName = row.LastName,
                    FatherName = person.FatherName,
                    NationalNo = row.NationalNo,
                    Phone = row.Phone,
                    Email = row.Email,
                    DeptId = row.DeptId,
                    EmpTypeId = row.EmpTypeId,
                    CertificateId = person.CertificateId,
                    HiredDate = row.HiredDate,
                    FiredDate = row.FiredDate,
                    Gender = person.Gender,
                    IsProgramUser = person.IsProgramUser,
                    IsWebUser = person.IsWebUser,
                    DeviceEnabled = person.DeviceEnabled,
                    UserName = person.UserName
                };

                OperationResult result = _repository.UpdatePersonnel(updated);
                string failure;
                if (!result.Succeeded)
                {
                    outcome.Failed++;
                    messages.Add("سطر " + row.Line + ": " + result.Message);
                    continue;
                }
                if (!Validate(updated, out failure))
                {
                    outcome.Failed++;
                    messages.Add("سطر " + row.Line + ": " + failure);
                    continue;
                }
                outcome.Updated++;
            }

            outcome.Messages = messages;
            return outcome;
        }

        private ImportRow BuildRow(int line, string[] header, string[] cells)
        {
            var row = new ImportRow { Line = line };
            string badge = Cell(header, cells, "badge");
            long parsedBadge;
            if (!long.TryParse(badge, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedBadge) ||
                parsedBadge <= 0)
            {
                row.Problems.Add("شماره پرسنلی «" + badge + "» عدد معتبر نیست.");
            }
            else
            {
                row.BadgeNumber = parsedBadge;
            }

            row.FirstName = Cell(header, cells, "firstname").Trim();
            row.LastName = Cell(header, cells, "lastname").Trim();
            row.NationalNo = Cell(header, cells, "nationalno").Trim();
            row.Phone = Cell(header, cells, "phone").Trim();
            row.Email = Cell(header, cells, "email").Trim();

            row.DeptId = ParseInt(Cell(header, cells, "deptid"), "شناسه واحد", row.Problems);
            row.EmpTypeId = ParseInt(Cell(header, cells, "emptypeid"), "نوع استخدام", row.Problems);
            row.HiredDate = ParseDate(Cell(header, cells, "hireddate"), "تاریخ استخدام", row.Problems);
            row.FiredDate = ParseDate(Cell(header, cells, "fireddate"), "تاریخ خروج", row.Problems);

            if (row.FirstName.Length == 0)
            {
                row.Problems.Add("نام خالی است.");
            }
            if (row.LastName.Length == 0)
            {
                row.Problems.Add("نام خانوادگی خالی است.");
            }
            if (row.FirstName.Length > 60 || row.LastName.Length > 60)
            {
                row.Problems.Add("نام یا نام خانوادگی بیش از ۶۰ نویسه است.");
            }
            if (row.NationalNo.Length > 0 && row.NationalNo.Length != 10)
            {
                row.Problems.Add("کد ملی باید ۱۰ رقم باشد.");
            }
            if (row.Email.Length > 0 && row.Email.IndexOf('@') <= 0)
            {
                row.Problems.Add("ایمیل معتبر نیست.");
            }
            if (row.HiredDate.HasValue && row.FiredDate.HasValue && row.FiredDate.Value.Date < row.HiredDate.Value.Date)
            {
                row.Problems.Add("تاریخ خروج پیش از تاریخ استخدام است.");
            }

            if (row.BadgeNumber > 0)
            {
                row.Exists = _repository.GetPersonnel(row.BadgeNumber) != null;
                if (!row.Exists)
                {
                    // Adding personnel is out of scope for the import: the engine mints the
                    // device card number and the badge sequence on the desktop side.
                    row.Problems.Add("پرسنلی با شماره " + row.BadgeNumber + " وجود ندارد؛ افزودن پرسنل جدید از این صفحه ممکن نیست.");
                }
            }
            return row;
        }

        /// <summary>Server-side re-validation applied again right before the write.</summary>
        private static bool Validate(PersonnelRow row, out string failure)
        {
            failure = string.Empty;
            if (row.BadgeNumber <= 0)
            {
                failure = "شماره پرسنلی معتبر نیست.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(row.FirstName) || string.IsNullOrWhiteSpace(row.LastName))
            {
                failure = "نام و نام خانوادگی الزامی است.";
                return false;
            }
            if (row.DeptId <= 0)
            {
                failure = "واحد سازمانی معتبر نیست.";
                return false;
            }
            if (row.FiredDate.HasValue && row.HiredDate.HasValue && row.FiredDate.Value.Date < row.HiredDate.Value.Date)
            {
                failure = "تاریخ خروج پیش از تاریخ استخدام است.";
                return false;
            }
            return true;
        }

        private static string CheckHeader(string[] header)
        {
            if (header.Length < ExpectedHeader.Length)
            {
                return "سرستون‌ها ناقص است؛ ستون‌های لازم: " + string.Join(", ", ExpectedHeader);
            }
            for (int i = 0; i < ExpectedHeader.Length; i++)
            {
                if (header[i] != ExpectedHeader[i])
                {
                    return "ستون " + (i + 1) + " باید «" + ExpectedHeader[i] + "» باشد (مقدار فعلی: «" +
                           header[i] + "»).";
                }
            }
            return null;
        }

        private static string Cell(string[] header, string[] cells, string name)
        {
            for (int i = 0; i < header.Length; i++)
            {
                if (header[i] == name)
                {
                    return i < cells.Length ? (cells[i] ?? string.Empty) : string.Empty;
                }
            }
            return string.Empty;
        }

        private static int ParseInt(string value, string label, List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }
            int parsed;
            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                problems.Add(label + " («" + value.Trim() + "») عدد نیست.");
                return 0;
            }
            return parsed;
        }

        private static DateTime? ParseDate(string value, string label, List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            DateTime parsed;
            if (!DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out parsed))
            {
                problems.Add(label + " («" + value.Trim() + "») باید به شکل yyyy-MM-dd باشد.");
                return null;
            }
            return parsed;
        }

        /// <summary>Minimal RFC4180 splitter: quotes, doubled quotes, embedded commas.</summary>
        internal static List<string[]> SplitCsv(string text)
        {
            var rows = new List<string[]>();
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        cells.Add(cell.ToString());
                        cell.Length = 0;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        cells.Add(cell.ToString());
                        cell.Length = 0;
                        rows.Add(cells.ToArray());
                        cells.Clear();
                        break;
                    default:
                        cell.Append(c);
                        break;
                }
            }

            if (cell.Length > 0 || cells.Count > 0)
            {
                cells.Add(cell.ToString());
                rows.Add(cells.ToArray());
            }

            // A leading BOM arrives from Excel's "CSV UTF-8" export.
            if (rows.Count > 0 && rows[0].Length > 0)
            {
                rows[0][0] = rows[0][0].TrimStart('\uFEFF');
            }
            return rows;
        }

        private static string Escape(string value)
        {
            string text = value ?? string.Empty;
            if (text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return text;
            }
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
