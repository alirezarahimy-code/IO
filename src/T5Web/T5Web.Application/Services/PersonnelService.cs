using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Application.Services
{
    /// <summary>
    /// Personnel search and edit rules.
    ///
    /// The legacy screen posted the whole row back to the database with no validation:
    /// a 300 mm long national number or an empty user name silently overwrote a good
    /// record. Rules here are explicit, testable and independent of the UI.
    /// </summary>
    public sealed class PersonnelService
    {
        private const int MaxNameLength = 50;
        private const int MaxEmailLength = 50;
        private static readonly Regex NationalNoPattern = new Regex(@"^[0-9]{6,12}$", RegexOptions.Compiled);
        private static readonly Regex UserNamePattern = new Regex(@"^[A-Za-z0-9._-]{3,100}$", RegexOptions.Compiled);
        private static readonly Regex EmailPattern = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        private readonly IAdminRepository _repository;

        public PersonnelService(IAdminRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }
            _repository = repository;
        }

        public IReadOnlyList<PersonnelRow> Search(string term, int? departmentId, bool includeFormer, int take = 200)
        {
            string cleaned = (term ?? string.Empty).Trim();
            if (take <= 0 || take > 1000)
            {
                take = 200;
            }
            return _repository.SearchPersonnel(cleaned, departmentId, includeFormer, take);
        }

        public PersonnelRow Get(long badgeNumber)
        {
            return _repository.GetPersonnel(badgeNumber);
        }

        /// <summary>
        /// Validates and saves a person. Returns the first problem instead of throwing,
        /// so the view can show it above the field that caused it.
        /// </summary>
        public OperationResult Save(PersonnelRow row)
        {
            if (row == null)
            {
                return OperationResult.Fail("رکورد پرسنل خالی است.");
            }
            if (row.BadgeNumber <= 0)
            {
                return OperationResult.Fail("شماره پرسنلی نامعتبر است.");
            }
            if (string.IsNullOrWhiteSpace(row.FirstName) || string.IsNullOrWhiteSpace(row.LastName))
            {
                return OperationResult.Fail("نام و نام خانوادگی الزامی است.");
            }
            if (row.FirstName.Length > MaxNameLength || row.LastName.Length > MaxNameLength)
            {
                return OperationResult.Fail("طول نام یا نام خانوادگی بیش از حد مجاز است.");
            }
            if (!string.IsNullOrWhiteSpace(row.NationalNo) && !NationalNoPattern.IsMatch(row.NationalNo.Trim()))
            {
                return OperationResult.Fail("کد ملی باید فقط رقم و بین ۶ تا ۱۲ رقم باشد.");
            }
            if (!string.IsNullOrWhiteSpace(row.UserName) && !UserNamePattern.IsMatch(row.UserName.Trim()))
            {
                return OperationResult.Fail("نام کاربری باید ۳ تا ۱۰۰ کاراکتر و شامل حروف انگلیسی، رقم، نقطه، خط تیره یا زیرخط باشد.");
            }
            if (!string.IsNullOrWhiteSpace(row.Email))
            {
                if (row.Email.Length > MaxEmailLength || !EmailPattern.IsMatch(row.Email.Trim()))
                {
                    return OperationResult.Fail("پست الکترونیک نامعتبر است.");
                }
            }
            if (row.HiredDate.HasValue && row.FiredDate.HasValue && row.FiredDate.Value < row.HiredDate.Value)
            {
                return OperationResult.Fail("تاریخ پایان کار نمی‌تواند پیش از تاریخ استخدام باشد.");
            }
            if (row.DeptId <= 0)
            {
                return OperationResult.Fail("واحد سازمانی را انتخاب کنید.");
            }

            return _repository.UpdatePersonnel(row);
        }

        /// <summary>
        /// Normalises text for display: trims, collapses inner whitespace and strips
        /// the Arabic/Persian character mix that breaks search in the legacy grid
        /// (ي/ك vs ی/ک).
        /// </summary>
        public static string NormalizeDisplay(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            string trimmed = Regex.Replace(value.Trim(), @"\s{2,}", " ");
            return trimmed.Replace('ي', 'ی').Replace('ك', 'ک');
        }

        /// <summary>
        /// Formats a date for the Persian UI without any online dependency:
        /// Gregorian -> Jalali via a pure arithmetic conversion.
        /// </summary>
        public static string ToJalali(DateTime? date)
        {
            if (!date.HasValue)
            {
                return "—";
            }
            int year, month, day;
            ToJalaliParts(date.Value.Year, date.Value.Month, date.Value.Day, out year, out month, out day);
            return string.Format(CultureInfo.InvariantCulture, "{0:0000}/{1:00}/{2:00}", year, month, day);
        }

        /// <summary>
        /// Gregorian -> Jalali using the framework's own PersianCalendar.
        ///
        /// A hand-rolled arithmetic conversion was tried first and was off by one day
        /// around the 1403/1404 leap boundary; PersianCalendar has been part of the
        /// .NET Framework since 2.0, needs no download and no culture install, and is
        /// accurate for the whole supported range.
        /// </summary>
        public static void ToJalaliParts(int gy, int gm, int gd, out int jy, out int jm, out int jd)
        {
            if (gy < 622 || gy > 9999)
            {
                // PersianCalendar throws outside its range; fall back to an explicit
                // marker instead of crashing a grid that renders hundreds of dates.
                jy = 0;
                jm = 0;
                jd = 0;
                return;
            }

            var calendar = new System.Globalization.PersianCalendar();
            DateTime date;
            try
            {
                date = new DateTime(gy, gm, gd);
            }
            catch (ArgumentOutOfRangeException)
            {
                jy = 0;
                jm = 0;
                jd = 0;
                return;
            }

            jy = calendar.GetYear(date);
            jm = calendar.GetMonth(date);
            jd = calendar.GetDayOfMonth(date);
        }
    }
}
