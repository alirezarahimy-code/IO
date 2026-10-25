using System;

namespace TWF.Domain.Models
{
    /// <summary>
    /// Strongly-typed application settings model representing all TWF configuration values in Web.config.
    /// Fixes BUG-02 (CookiesSecure read as bool, CookiesTime respected in token expiry),
    /// BUG-03 (ChangePassForce read from "ChangePassForce" instead of "ChangePassLimit"),
    /// and BUG-05 (ExportReport and ReportExport keys synchronized).
    /// </summary>
    public sealed class TwfAppSettings
    {
        // --- Connection & Branding ---
        public string T5CnnStr { get; set; } = string.Empty;
        public string PersonnelCnnStr { get; set; } = string.Empty;
        public string LogoPath { get; set; } = string.Empty;

        // --- Request & Attendance Limits ---
        public int RequestRecordsDailyLimit { get; set; } = -1;
        public int RequestRecordsMonthlyLimit { get; set; } = -1;
        public bool ValidLateEarly { get; set; } = true;
        public bool ValidHourlyFree { get; set; } = false;
        public int HoursOfDays { get; set; } = 8;
        public int MaxRequestAgeDays { get; set; } = 60;
        public int LastDayForRequest { get; set; } = 31;
        public int ShowDays { get; set; } = 30;
        public double RequestsLockOADate { get; set; } = 0.0;
        public int DefaultLeaveId { get; set; } = 1;
        public int Limit1LeaveId { get; set; } = -1;
        public string HideLeaveTypes { get; set; } = "0";

        // --- Module Visibility & Permissions ---
        public bool ViewDailyDuty { get; set; } = true;
        public bool ViewHourlyDuty { get; set; } = true;
        public bool ViewDailyLeave { get; set; } = true;
        public bool ViewHourlyLeave { get; set; } = true;
        public bool ViewOverTimeRequest { get; set; } = true;
        public bool ViewRewardRequest { get; set; } = true;
        public bool ViewConstOT { get; set; } = true;
        public bool RequestForUsers { get; set; } = false;
        public bool RequestRecord { get; set; } = true;
        public bool ExportReport { get; set; } = true;

        // --- Password & Security Policy ---
        public bool ChangePassRestriction { get; set; } = false;
        public bool ChangePassMonthly { get; set; } = false;
        public int ChangePassLimit { get; set; } = 30;
        public bool ChangePassForce { get; set; } = false;

        // --- Session Cookie & Login Security ---
        public bool CookiesSecure { get; set; } = false;
        public int CookiesTime { get; set; } = 12;
        public bool Captcha { get; set; } = false;
        public string LockIP { get; set; } = string.Empty;

        /// <summary>
        /// Returns the configured session lifetime as a <see cref="TimeSpan"/> (minimum 1 hour, default 12 hours).
        /// </summary>
        public TimeSpan GetSessionLifetime()
        {
            int hours = CookiesTime > 0 ? CookiesTime : 12;
            return TimeSpan.FromHours(hours);
        }

        /// <summary>
        /// Returns the global requests lock cutoff date (if RequestsLockOADate > 0), or null if unlocked.
        /// </summary>
        public DateTime? GetRequestsLockDate()
        {
            if (RequestsLockOADate <= 0.0)
                return null;
            try
            {
                return DateTime.FromOADate(RequestsLockOADate);
            }
            catch
            {
                return null;
            }
        }

        public TwfAppSettings Clone()
        {
            return (TwfAppSettings)MemberwiseClone();
        }
    }
}
