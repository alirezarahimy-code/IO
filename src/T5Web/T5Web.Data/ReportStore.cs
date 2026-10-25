using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// Report execution for both implementations.
    ///
    /// The class is deliberately narrow: it composes the attendance, leave and personnel
    /// reads that already exist and never issues attendance SQL of its own. That is what
    /// keeps the rewrite inside its promise - the portal shows what the engine stored,
    /// it does not recalculate it.
    ///
    /// "engine" reports (late/early, overtime, monthly grid) are the ones whose numbers
    /// belong to T5V3.Attendance. The portal cannot call the desktop engine from a web
    /// request, so it returns the *raw punches* of the window, clearly labelled, plus the
    /// name of the legacy RDLC that produces the calculated figure. Operators keep working
    /// offline, and no number is invented.
    /// </summary>
    public sealed class ReportStore : IReportStore
    {
        private const int RowLimit = 5000;

        private readonly IAttendanceStore _attendance;
        private readonly ILeaveStore _leave;
        private readonly IAdminRepository _admin;

        public ReportStore(IAttendanceStore attendance, ILeaveStore leave, IAdminRepository admin)
        {
            if (attendance == null)
            {
                throw new ArgumentNullException(nameof(attendance));
            }
            if (leave == null)
            {
                throw new ArgumentNullException(nameof(leave));
            }
            if (admin == null)
            {
                throw new ArgumentNullException(nameof(admin));
            }
            _attendance = attendance;
            _leave = leave;
            _admin = admin;
        }

        public ReportResult Run(string code, DateTime from, DateTime to, long? badgeNumber, int? departmentId)
        {
            var watch = Stopwatch.StartNew();
            ReportResult result;
            string normalized = (code ?? string.Empty).Trim().ToLowerInvariant();

            switch (normalized)
            {
                case "daily":
                    result = Daily(from, to, badgeNumber);
                    break;
                case "sum":
                    result = MonthlySum(from, to, badgeNumber);
                    break;
                case "exit":
                    result = Exits(from, to, departmentId);
                    break;
                case "cardex":
                    result = Cardex(from, to, badgeNumber);
                    break;
                case "user":
                    result = Personnel(departmentId, false);
                    break;
                case "personnel-status":
                    result = Personnel(departmentId, true);
                    break;
                case "remained-leaves":
                    result = RemainedLeaves(to, badgeNumber);
                    break;
                case "requests":
                    result = Requests(from, to, badgeNumber);
                    break;
                case "leaves":
                    result = Leaves(from, to, badgeNumber, departmentId);
                    break;
                case "late-early":
                    result = RawPunches("تأخیر و تعجیل", "LateEarlyReport.rdlc", from, to, badgeNumber);
                    break;
                case "overtime":
                    result = RawPunches("اضافه‌کاری", "OverTimeReport.rdlc", from, to, badgeNumber);
                    break;
                case "grid":
                    result = RawPunches("جدول حضور", "GridReport.rdlc", from, to, badgeNumber);
                    break;
                default:
                    result = new ReportResult
                    {
                        Code = normalized,
                        TitleFa = "گزارش ناشناخته",
                        Columns = new List<string>(),
                        Rows = new List<IReadOnlyList<string>>(),
                        From = from,
                        To = to,
                        NoteFa = "این گزارش در فهرست پورتال وجود ندارد."
                    };
                    break;
            }

            result.Code = normalized;
            result.From = from;
            result.To = to;
            watch.Stop();
            result.Elapsed = watch.Elapsed;
            return result;
        }

        private ReportResult Daily(DateTime from, DateTime to, long? badgeNumber)
        {
            var query = new RecordQuery
            {
                FromDate = from,
                ToDate = to,
                BadgeNumber = badgeNumber.HasValue && badgeNumber.Value > 0 ? badgeNumber : null,
                Take = RowLimit
            }.Clamped();

            IReadOnlyList<RecordRow> rows = _attendance.QueryRecords(query);
            return new ReportResult
            {
                TitleFa = "گزارش روزانه تردد",
                Columns = new[] { "شماره", "نام", "تاریخ", "ساعت", "نوع", "دستگاه", "وضعیت" },
                Rows = rows.Select(r => (IReadOnlyList<string>)new[]
                {
                    r.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                    r.PersonName,
                    JalaliText.Format(r.CheckTime),
                    r.CheckTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    r.CheckTypeFa,
                    r.MachineName,
                    r.IsValid ? "معتبر" : "نامعتبر"
                }).ToList(),
                Truncated = rows.Count >= RowLimit,
                NoteFa = "ترددهای ثبت‌شده؛ بدون بازمحاسبه."
            };
        }

        private ReportResult MonthlySum(DateTime from, DateTime to, long? badgeNumber)
        {
            DateTime cursor = new DateTime(from.Year, from.Month, 1);
            var rows = new List<IReadOnlyList<string>>();
            bool truncated = false;
            while (cursor <= to)
            {
                foreach (AttendanceSummaryRow row in _attendance.GetMonthlySummary(
                             cursor.Year, cursor.Month, badgeNumber, 500))
                {
                    if (rows.Count >= RowLimit)
                    {
                        truncated = true;
                        break;
                    }
                    rows.Add(new[]
                    {
                        JalaliText.Format(cursor).Substring(0, 7),
                        row.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                        row.PersonName,
                        row.PresentDays.ToString(CultureInfo.InvariantCulture),
                        row.LateCount.ToString(CultureInfo.InvariantCulture),
                        row.EarlyCount.ToString(CultureInfo.InvariantCulture),
                        row.MissingPunchDays.ToString(CultureInfo.InvariantCulture)
                    });
                }
                cursor = cursor.AddMonths(1);
            }

            return new ReportResult
            {
                TitleFa = "جمع‌بندی ماهانه",
                Columns = new[] { "ماه", "شماره", "نام", "روز حضور", "تأخیر", "تعجیل", "تردد ناقص" },
                Rows = rows,
                Truncated = truncated,
                NoteFa = "جمع‌بندی از ترددهای خام؛ اعداد رسمی موتور در گزارش‌های «موتور حضور» است."
            };
        }

        private ReportResult Exits(DateTime from, DateTime to, int? departmentId)
        {
            IReadOnlyList<PersonnelRow> people = _admin.SearchPersonnel(string.Empty,
                departmentId.HasValue && departmentId.Value > 0 ? departmentId : null, true, 1000);

            List<PersonnelRow> exiting = people
                .Where(p => p.FiredDate.HasValue &&
                            p.FiredDate.Value.Date >= from.Date && p.FiredDate.Value.Date <= to.Date)
                .ToList();

            return new ReportResult
            {
                TitleFa = "خروج از سازمان",
                Columns = new[] { "شماره", "نام", "واحد", "تاریخ استخدام", "تاریخ خروج" },
                Rows = exiting.Select(p => (IReadOnlyList<string>)new[]
                {
                    p.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                    p.FullName,
                    p.DeptName,
                    JalaliText.Format(p.HiredDate),
                    JalaliText.Format(p.FiredDate)
                }).ToList(),
                Truncated = people.Count >= 1000
            };
        }

        private ReportResult Cardex(DateTime from, DateTime to, long? badgeNumber)
        {
            var rows = new List<IReadOnlyList<string>>();
            var query = new RecordQuery
            {
                FromDate = from,
                ToDate = to,
                BadgeNumber = badgeNumber.HasValue && badgeNumber.Value > 0 ? badgeNumber : null,
                Take = RowLimit
            }.Clamped();

            foreach (RecordRow record in _attendance.QueryRecords(query))
            {
                rows.Add(new[]
                {
                    "تردد",
                    JalaliText.Format(record.CheckTime),
                    record.CheckTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    record.CheckTypeFa,
                    record.IsValid ? "معتبر" : "نامعتبر",
                    record.MachineName
                });
            }

            foreach (LeaveGrantRow grant in _leave.GetGrants(badgeNumber, null, 500))
            {
                if (grant.AssignDate.Date < from.Date || grant.AssignDate.Date > to.Date)
                {
                    continue;
                }
                rows.Add(new[]
                {
                    "مرخصی",
                    JalaliText.Format(grant.AssignDate),
                    grant.Days.ToString("0.##", CultureInfo.InvariantCulture) + " روز",
                    grant.LeaveTypeName,
                    grant.DocumentNumber,
                    grant.Descriptions
                });
            }

            rows = rows.OrderBy(r => r[1], StringComparer.Ordinal).ToList();
            return new ReportResult
            {
                TitleFa = "کارتکس پرسنل",
                Columns = new[] { "نوع", "تاریخ", "جزئیات", "شرح", "سند", "توضیح" },
                Rows = rows,
                Truncated = rows.Count >= RowLimit,
                NoteFa = "گردش تردد و مرخصی یک نفر، مرتب‌شده بر تاریخ."
            };
        }

        private ReportResult Personnel(int? departmentId, bool withStatus)
        {
            IReadOnlyList<PersonnelRow> people = _admin.SearchPersonnel(string.Empty,
                departmentId.HasValue && departmentId.Value > 0 ? departmentId : null, true, 2000);

            return new ReportResult
            {
                TitleFa = withStatus ? "وضعیت پرسنل" : "فهرست پرسنل",
                Columns = withStatus
                    ? new[] { "شماره", "نام", "واحد", "نوع استخدام", "فعال", "دستگاه", "کاربر وب" }
                    : new[] { "شماره", "نام", "واحد", "نوع استخدام", "تلفن", "استخدام" },
                Rows = people.Select(p => (IReadOnlyList<string>)(withStatus
                        ? new[]
                        {
                            p.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                            p.FullName,
                            p.DeptName,
                            p.EmpTypeName,
                            p.IsFormer ? "خیر" : "بله",
                            p.DeviceEnabled ? "فعال" : "غیرفعال",
                            p.IsWebUser ? "بله" : "خیر"
                        }
                        : new[]
                        {
                            p.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                            p.FullName,
                            p.DeptName,
                            p.EmpTypeName,
                            p.Phone,
                            JalaliText.Format(p.HiredDate)
                        })).ToList(),
                Truncated = people.Count >= 2000
            };
        }

        private ReportResult RemainedLeaves(DateTime to, long? badgeNumber)
        {
            int year = new PersianCalendar().GetYear(to);
            IReadOnlyList<LeaveBalanceRow> balances = _leave.GetBalances(year, null, null, RowLimit);
            if (badgeNumber.HasValue && badgeNumber.Value > 0)
            {
                balances = balances.Where(b => b.BadgeNumber == badgeNumber.Value).ToList();
            }

            return new ReportResult
            {
                TitleFa = "مانده مرخصی",
                Columns = new[] { "شماره", "نام", "نوع مرخصی", "مانده آغاز", "اضافه‌شده", "مصرف", "مانده پایان" },
                Rows = balances.Select(b => (IReadOnlyList<string>)new[]
                {
                    b.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                    b.PersonName,
                    b.LeaveTypeName,
                    b.OpeningDays.ToString("0.##", CultureInfo.InvariantCulture),
                    b.AddedDays.ToString("0.##", CultureInfo.InvariantCulture),
                    b.UsedDays.ToString("0.##", CultureInfo.InvariantCulture),
                    b.ClosingDays.ToString("0.##", CultureInfo.InvariantCulture)
                }).ToList(),
                Truncated = balances.Count >= RowLimit
            };
        }

        /// <summary>
        /// "لیست مرخصی‌ها" - the desktop's frmLeavesReport.
        ///
        /// The legacy web portal had no such screen at all, so this is one of the few
        /// reports the rewrite *added* rather than ported (docs/10, wave 1). It reads the
        /// grants the engine already stores; nothing is recalculated.
        /// </summary>
        private ReportResult Leaves(DateTime from, DateTime to, long? badgeNumber, int? departmentId)
        {
            IReadOnlyList<PersonnelRow> people = _admin.SearchPersonnel(string.Empty,
                departmentId.HasValue && departmentId.Value > 0 ? departmentId : null, true, 2000);

            var names = new Dictionary<long, string>();
            foreach (PersonnelRow person in people)
            {
                if (!names.ContainsKey(person.BadgeNumber))
                {
                    names[person.BadgeNumber] = person.FullName;
                }
            }

            bool scoped = departmentId.HasValue && departmentId.Value > 0;
            List<LeaveGrantRow> grants = _leave.GetGrants(badgeNumber, null, RowLimit)
                .Where(g => g.AssignDate.Date >= from.Date && g.AssignDate.Date <= to.Date)
                .Where(g => !scoped || names.ContainsKey(g.BadgeNumber))
                .OrderBy(g => g.AssignDate)
                .ThenBy(g => g.BadgeNumber)
                .ToList();

            return new ReportResult
            {
                TitleFa = "لیست مرخصی‌ها",
                Columns = new[] { "شماره", "نام", "نوع مرخصی", "تاریخ ثبت", "روز", "سند", "توضیح" },
                Rows = grants.Select(g => (IReadOnlyList<string>)new[]
                {
                    g.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                    names.ContainsKey(g.BadgeNumber) ? names[g.BadgeNumber] : string.Empty,
                    g.LeaveTypeName,
                    JalaliText.Format(g.AssignDate),
                    g.Days.ToString("0.##", CultureInfo.InvariantCulture),
                    g.DocumentNumber,
                    g.Descriptions
                }).ToList(),
                Truncated = grants.Count >= RowLimit,
                NoteFa = "مرخصی‌های ثبت‌شده در بازه؛ مانده و مصرف محاسبه نمی‌شود."
            };
        }

        private ReportResult Requests(DateTime from, DateTime to, long? badgeNumber)
        {
            IReadOnlyList<RequestRow> rows = _attendance.QueryRequests(false, badgeNumber, 2000)
                .Where(r => r.ForDate.Date >= from.Date && r.ForDate.Date <= to.Date)
                .ToList();

            return new ReportResult
            {
                TitleFa = "درخواست‌ها",
                Columns = new[] { "شناسه", "شماره", "نام", "نوع", "تاریخ", "ساعت", "مرحله", "وضعیت" },
                Rows = rows.Select(r => (IReadOnlyList<string>)new[]
                {
                    r.RequestId.ToString(CultureInfo.InvariantCulture),
                    r.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                    r.PersonName,
                    r.LeaveName,
                    JalaliText.Format(r.ForDate),
                    r.Hours.ToString("0.##", CultureInfo.InvariantCulture),
                    r.AcceptedCount + " / " + r.StepCount,
                    r.StateFa
                }).ToList(),
                Truncated = rows.Count >= 2000
            };
        }

        private ReportResult RawPunches(string title, string legacyReport, DateTime from, DateTime to,
                                        long? badgeNumber)
        {
            var query = new RecordQuery
            {
                FromDate = from,
                ToDate = to,
                BadgeNumber = badgeNumber.HasValue && badgeNumber.Value > 0 ? badgeNumber : null,
                Take = RowLimit
            }.Clamped();

            IReadOnlyList<RecordRow> rows = _attendance.QueryRecords(query);
            return new ReportResult
            {
                TitleFa = title,
                Columns = new[] { "شماره", "نام", "تاریخ", "ساعت", "نوع", "دستگاه" },
                Rows = rows.Select(r => (IReadOnlyList<string>)new[]
                {
                    r.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                    r.PersonName,
                    JalaliText.Format(r.CheckTime),
                    r.CheckTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    r.CheckTypeFa,
                    r.MachineName
                }).ToList(),
                Truncated = rows.Count >= RowLimit,
                NoteFa = "این گزارش در نسخه دسکتاپ با قالب " + legacyReport +
                         " و محاسبه موتور حضور تولید می‌شود. اعداد زیر «تردد خام» است و محاسبه‌شده نیست؛ " +
                         "برای رقم رسمی، همان قالب را در برنامه دسکتاپ اجرا کنید."
            };
        }
    }
}
