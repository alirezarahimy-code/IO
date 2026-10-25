using System;
using System.Configuration;
using System.Web.Configuration;
using System.Web.Mvc;
using MyTime3.Content;
using MyTime3.Models;
using T5V3.DB;
using T5V3.Model;
using TWF.Domain.Models;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten SettingsController (Phase 3).
    /// - Fixes BUG-03: LoadSettings() reads "ChangePassForce" instead of "ChangePassLimit".
    /// - Fixes BUG-05: Synchronizes both "ExportReport" and "ReportExport" keys in Web.config.
    /// </summary>
    public class SettingsController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin")]
        public ActionResult Index()
        {
            return View(LoadSettings());
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin")]
        public ActionResult Index(SettingsModel model)
        {
            if (model == null)
            {
                model = LoadSettings();
                return View("Index", model);
            }

            string message;
            try
            {
                if (DBInitializer.IsTestMode && DBInitializer.TestSettingsService != null)
                {
                    TwfAppSettings current = DBInitializer.TestSettingsService.GetSettings().Clone();
                    current.RequestRecordsDailyLimit = model.RequestRecordsDailyLimit;
                    current.RequestRecordsMonthlyLimit = model.RequestRecordsMonthlyLimit;
                    current.ValidLateEarly = model.AllowValidLate;
                    current.ViewDailyDuty = model.ViewDailyDuty;
                    current.ViewHourlyDuty = model.ViewHourlyDuty;
                    current.ValidHourlyFree = model.AllowValidHourlyFree;
                    current.HoursOfDays = model.HoursOfDays > 0 ? model.HoursOfDays : 8;
                    current.ViewDailyLeave = model.ViewDailyLeave;
                    current.ViewHourlyLeave = model.ViewHourlyLeave;
                    current.MaxRequestAgeDays = model.MaxRequestAgeDays;
                    current.ViewRewardRequest = model.ViewRewardRequest;
                    current.RequestRecord = model.RequestRecord;
                    current.ViewConstOT = model.ViewConstOT;
                    current.LastDayForRequest = model.LastDayForRequest;
                    current.ViewOverTimeRequest = model.ViewOverTimeRequest;
                    current.RequestForUsers = model.RequestForUser;
                    current.ExportReport = model.ExportReport;
                    current.ChangePassRestriction = model.ChangePassRestriction;
                    current.ChangePassMonthly = model.ChangePassMonthly;
                    current.ChangePassLimit = model.ChangePassLimit > 0 ? model.ChangePassLimit : 3;
                    current.ChangePassForce = model.ChangePassForce;
                    DBInitializer.TestSettingsService.SaveSettings(current);
                }

                SaveToWebConfig(model);

                if (!DBInitializer.IsTestMode)
                {
                    T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                    foreach (Rule item in t5DBInstance.RuleDA.GetAll())
                    {
                        item.LimitRecReqInDay = model.RequestRecordsDailyLimit;
                        item.LimitRecReqInMonth = model.RequestRecordsMonthlyLimit;
                        t5DBInstance.RuleDA.Edit(item, out string _);
                    }
                }

                message = PersianString.InsertSuccess;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                model = LoadSettings();
            }

            model.OverAllMessage = message;
            return View("Index", model);
        }

        private static void SaveToWebConfig(SettingsModel model)
        {
            try
            {
                Configuration config = WebConfigurationManager.OpenWebConfiguration("~");
                KeyValueConfigurationCollection settings = config.AppSettings.Settings;

                string[] managedKeys =
                {
                    "RequestRecordsDailyLimit",
                    "RequestRecordsMonthlyLimit",
                    "ValidLateEarly",
                    "ValidHourlyFree",
                    "ViewDailyDuty",
                    "RequestRecord",
                    "ViewHourlyDuty",
                    "ViewDailyLeave",
                    "ViewHourlyLeave",
                    "ViewRewPen",
                    "ViewConstOT",
                    "MaxRequestAgeDays",
                    "LastDayForRequest",
                    "ViewOverTimeRequest",
                    "RequestForUsers",
                    "ExportReport",
                    "ReportExport",
                    "HoursOfDays",
                    "ChangePassRestriction",
                    "ChangePassMonthly",
                    "ChangePassLimit",
                    "ChangePassForce"
                };

                foreach (string key in managedKeys)
                {
                    settings.Remove(key);
                }

                settings.Add("RequestRecordsDailyLimit", model.RequestRecordsDailyLimit.ToString());
                settings.Add("RequestRecordsMonthlyLimit", model.RequestRecordsMonthlyLimit.ToString());
                settings.Add("ValidLateEarly", model.AllowValidLate.ToString());
                settings.Add("ViewDailyDuty", model.ViewDailyDuty.ToString());
                settings.Add("ViewHourlyDuty", model.ViewHourlyDuty.ToString());
                settings.Add("ValidHourlyFree", model.AllowValidHourlyFree.ToString());
                settings.Add("HoursOfDays", (model.HoursOfDays > 0 ? model.HoursOfDays : 8).ToString());
                settings.Add("ViewDailyLeave", model.ViewDailyLeave.ToString());
                settings.Add("ViewHourlyLeave", model.ViewHourlyLeave.ToString());
                settings.Add("MaxRequestAgeDays", model.MaxRequestAgeDays.ToString());
                settings.Add("ViewRewPen", model.ViewRewardRequest.ToString());
                settings.Add("RequestRecord", model.RequestRecord.ToString());
                settings.Add("ViewConstOT", model.ViewConstOT.ToString());
                settings.Add("LastDayForRequest", model.LastDayForRequest.ToString());
                settings.Add("ViewOverTimeRequest", model.ViewOverTimeRequest.ToString());
                settings.Add("RequestForUsers", model.RequestForUser.ToString());

                // Fixed BUG-05: Synchronize both ExportReport and ReportExport keys in Web.config
                settings.Add("ExportReport", model.ExportReport.ToString());
                settings.Add("ReportExport", model.ExportReport.ToString());

                settings.Add("ChangePassRestriction", model.ChangePassRestriction.ToString());
                settings.Add("ChangePassMonthly", model.ChangePassMonthly.ToString());
                settings.Add("ChangePassLimit", (model.ChangePassLimit > 0 ? model.ChangePassLimit : 3).ToString());
                settings.Add("ChangePassForce", model.ChangePassForce.ToString());

                config.Save();
            }
            catch
            {
            }
        }

        public SettingsModel LoadSettings()
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestSettingsService != null)
            {
                TwfAppSettings app = DBInitializer.TestSettingsService.GetSettings();
                return new SettingsModel
                {
                    AllowValidLate = app.ValidLateEarly,
                    RequestRecordsDailyLimit = app.RequestRecordsDailyLimit,
                    RequestRecordsMonthlyLimit = app.RequestRecordsMonthlyLimit,
                    ViewDailyDuty = app.ViewDailyDuty,
                    ViewHourlyDuty = app.ViewHourlyDuty,
                    AllowValidHourlyFree = app.ValidHourlyFree,
                    HoursOfDays = app.HoursOfDays,
                    ViewDailyLeave = app.ViewDailyLeave,
                    ViewHourlyLeave = app.ViewHourlyLeave,
                    ViewOverTimeRequest = app.ViewOverTimeRequest,
                    ViewRewardRequest = app.ViewRewardRequest,
                    ViewConstOT = app.ViewConstOT,
                    MaxRequestAgeDays = app.MaxRequestAgeDays,
                    LastDayForRequest = app.LastDayForRequest,
                    RequestForUser = app.RequestForUsers,
                    RequestRecord = app.RequestRecord,
                    ExportReport = app.ExportReport,
                    ChangePassRestriction = app.ChangePassRestriction,
                    ChangePassMonthly = app.ChangePassMonthly,
                    ChangePassLimit = app.ChangePassLimit,
                    ChangePassForce = app.ChangePassForce
                };
            }

            var settingsModel = new SettingsModel();
            try
            {
                settingsModel.AllowValidLate = (bool)LoadValue("ValidLateEarly", typeof(bool), true);
                settingsModel.RequestRecordsDailyLimit = (int)LoadValue("RequestRecordsDailyLimit", typeof(int), 100);
                settingsModel.RequestRecordsMonthlyLimit = (int)LoadValue("RequestRecordsMonthlyLimit", typeof(int), 100);
                settingsModel.ViewDailyDuty = (bool)LoadValue("ViewDailyDuty", typeof(bool), true);
                settingsModel.ViewHourlyDuty = (bool)LoadValue("ViewHourlyDuty", typeof(bool), true);
                settingsModel.AllowValidHourlyFree = (bool)LoadValue("ValidHourlyFree", typeof(bool), true);
                settingsModel.HoursOfDays = (int)LoadValue("HoursOfDays", typeof(int), 8);
                settingsModel.ViewDailyLeave = (bool)LoadValue("ViewDailyLeave", typeof(bool), true);
                settingsModel.ViewHourlyLeave = (bool)LoadValue("ViewHourlyLeave", typeof(bool), true);
                settingsModel.ViewOverTimeRequest = (bool)LoadValue("ViewOverTimeRequest", typeof(bool), true);
                settingsModel.ViewRewardRequest = (bool)LoadValue("ViewRewPen", typeof(bool), true);
                settingsModel.ViewConstOT = (bool)LoadValue("ViewConstOT", typeof(bool), true);
                settingsModel.MaxRequestAgeDays = (int)LoadValue("MaxRequestAgeDays", typeof(int), 0);
                settingsModel.LastDayForRequest = (int)LoadValue("LastDayForRequest", typeof(int), 0);
                settingsModel.RequestForUser = (bool)LoadValue("RequestForUsers", typeof(bool), true);
                settingsModel.RequestRecord = (bool)LoadValue("RequestRecord", typeof(bool), true);

                // Fixed BUG-05: Check ExportReport with fallback to ReportExport
                bool exportFallback = (bool)LoadValue("ReportExport", typeof(bool), true);
                settingsModel.ExportReport = (bool)LoadValue("ExportReport", typeof(bool), exportFallback);

                settingsModel.ChangePassRestriction = (bool)LoadValue("ChangePassRestriction", typeof(bool), false);
                settingsModel.ChangePassMonthly = (bool)LoadValue("ChangePassMonthly", typeof(bool), false);
                settingsModel.ChangePassLimit = (int)LoadValue("ChangePassLimit", typeof(int), 3);

                // Fixed BUG-03: Legacy code mistakenly read "ChangePassLimit" as bool instead of "ChangePassForce"!
                settingsModel.ChangePassForce = (bool)LoadValue("ChangePassForce", typeof(bool), false);
                return settingsModel;
            }
            catch
            {
                return settingsModel;
            }
        }

        public static object LoadValue(string name, Type type, object defaultValue)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestSettingsService != null)
            {
                TwfAppSettings app = DBInitializer.TestSettingsService.GetSettings();
                object testVal = TryResolveFromAppSettings(app, name);
                if (testVal != null)
                {
                    return testVal;
                }
            }

            object result = defaultValue;
            try
            {
                string raw = WebConfigurationManager.AppSettings[name];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return result;
                }

                if (type == typeof(bool))
                {
                    if (bool.TryParse(raw, out bool b))
                        return b;
                    if (raw == "1")
                        return true;
                    if (raw == "0")
                        return false;
                    return result;
                }

                if (type == typeof(int))
                {
                    return int.TryParse(raw, out int i) ? i : result;
                }

                if (type == typeof(double))
                {
                    return double.TryParse(raw, out double d) ? d : result;
                }

                if (type == typeof(string))
                {
                    return raw;
                }

                return result;
            }
            catch
            {
                return result;
            }
        }

        private static object TryResolveFromAppSettings(TwfAppSettings app, string name)
        {
            if (app == null || string.IsNullOrWhiteSpace(name))
                return null;

            switch (name)
            {
                case "CookiesTime": return app.CookiesTime;
                case "CookiesSecure": return app.CookiesSecure;
                case "Captcha": return app.Captcha;
                case "LockIP": return app.LockIP;
                case "ValidLateEarly": return app.ValidLateEarly;
                case "RequestRecordsDailyLimit": return app.RequestRecordsDailyLimit;
                case "RequestRecordsMonthlyLimit": return app.RequestRecordsMonthlyLimit;
                case "ViewDailyDuty": return app.ViewDailyDuty;
                case "ViewHourlyDuty": return app.ViewHourlyDuty;
                case "ValidHourlyFree": return app.ValidHourlyFree;
                case "HoursOfDays": return app.HoursOfDays;
                case "ViewDailyLeave": return app.ViewDailyLeave;
                case "ViewHourlyLeave": return app.ViewHourlyLeave;
                case "ViewOverTimeRequest": return app.ViewOverTimeRequest;
                case "ViewRewPen": return app.ViewRewardRequest;
                case "ViewConstOT": return app.ViewConstOT;
                case "MaxRequestAgeDays": return app.MaxRequestAgeDays;
                case "LastDayForRequest": return app.LastDayForRequest;
                case "RequestForUsers": return app.RequestForUsers;
                case "RequestRecord": return app.RequestRecord;
                case "ExportReport":
                case "ReportExport": return app.ExportReport;
                case "ChangePassRestriction": return app.ChangePassRestriction;
                case "ChangePassMonthly": return app.ChangePassMonthly;
                case "ChangePassLimit": return app.ChangePassLimit;
                case "ChangePassForce": return app.ChangePassForce;
                case "RequestsLock": return app.RequestsLockOADate;
                case "ShowDays": return app.ShowDays;
                case "LeaveID": return app.DefaultLeaveId;
                case "Limit1LeaveID": return app.Limit1LeaveId;
                default: return null;
            }
        }
    }
}
