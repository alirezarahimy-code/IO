using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Xml.Linq;
using TWF.Domain.Interfaces;
using TWF.Domain.Models;

namespace TWF.Infrastructure.Configuration
{
    /// <summary>
    /// Thread-safe configuration service for reading and saving TWF settings in Web.config.
    /// Fixes BUG-02 (CookiesSecure parsed as bool instead of int),
    /// BUG-03 (ChangePassForce mapped to "ChangePassForce" instead of "ChangePassLimit"),
    /// and BUG-05 (ExportReport and ReportExport keys kept synchronized).
    /// </summary>
    public sealed class AppSettingsService : IAppSettingsService
    {
        private readonly string _webConfigPath;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private TwfAppSettings _cachedSettings;

        public AppSettingsService(string webConfigPath)
        {
            if (string.IsNullOrWhiteSpace(webConfigPath))
                throw new ArgumentNullException(nameof(webConfigPath));

            _webConfigPath = webConfigPath;
        }

        public TwfAppSettings GetSettings()
        {
            _lock.EnterReadLock();
            try
            {
                if (_cachedSettings != null)
                    return _cachedSettings.Clone();
            }
            finally
            {
                _lock.ExitReadLock();
            }

            return ReloadSettings();
        }

        public TwfAppSettings ReloadSettings()
        {
            _lock.EnterWriteLock();
            try
            {
                var raw = ReadRawDictionaryFromXml(_webConfigPath);
                _cachedSettings = MapFromDictionary(raw);
                return _cachedSettings.Clone();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void SaveSettings(TwfAppSettings updatedSettings)
        {
            if (updatedSettings == null)
                throw new ArgumentNullException(nameof(updatedSettings));

            _lock.EnterWriteLock();
            try
            {
                var updates = MapToDictionary(updatedSettings);
                WriteRawDictionaryToXml(_webConfigPath, updates);
                _cachedSettings = updatedSettings.Clone();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IDictionary<string, string> GetRawSettingsDictionary()
        {
            _lock.EnterReadLock();
            try
            {
                return ReadRawDictionaryFromXml(_webConfigPath);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public static TwfAppSettings MapFromDictionary(IDictionary<string, string> raw)
        {
            var s = new TwfAppSettings();
            if (raw == null)
                return s;

            s.T5CnnStr = ParseString(raw, "T5CnnStr", string.Empty);
            s.PersonnelCnnStr = ParseString(raw, "PersonnelCnnStr", string.Empty);
            s.LogoPath = ParseString(raw, "LogoPath", string.Empty);

            s.RequestRecordsDailyLimit = ParseInt(raw, "RequestRecordsDailyLimit", -1);
            s.RequestRecordsMonthlyLimit = ParseInt(raw, "RequestRecordsMonthlyLimit", -1);
            s.ValidLateEarly = ParseBool(raw, "ValidLateEarly", true);
            s.ValidHourlyFree = ParseBool(raw, "ValidHourlyFree", false);
            s.HoursOfDays = ParseInt(raw, "HoursOfDays", 8);
            s.MaxRequestAgeDays = ParseInt(raw, "MaxRequestAgeDays", 60);
            s.LastDayForRequest = ParseInt(raw, "LastDayForRequest", 31);
            s.ShowDays = ParseInt(raw, "ShowDays", 30);

            if (raw.ContainsKey("RequestsLock"))
                s.RequestsLockOADate = ParseDouble(raw, "RequestsLock", 0.0);
            else
                s.RequestsLockOADate = ParseDouble(raw, "RequestsLockOADate", 0.0);

            s.DefaultLeaveId = ParseInt(raw, "LeaveID", 1);
            s.Limit1LeaveId = ParseInt(raw, "Limit1LeaveID", -1);
            s.HideLeaveTypes = ParseString(raw, "HideLeaveTypes", "0");

            s.ViewDailyDuty = ParseBool(raw, "ViewDailyDuty", true);
            s.ViewHourlyDuty = ParseBool(raw, "ViewHourlyDuty", true);
            s.ViewDailyLeave = ParseBool(raw, "ViewDailyLeave", true);
            s.ViewHourlyLeave = ParseBool(raw, "ViewHourlyLeave", true);
            s.ViewOverTimeRequest = ParseBool(raw, "ViewOverTimeRequest", true);
            s.ViewRewardRequest = raw.ContainsKey("ViewRewPen")
                ? ParseBool(raw, "ViewRewPen", true)
                : ParseBool(raw, "ViewRewardRequest", true);
            s.ViewConstOT = ParseBool(raw, "ViewConstOT", true);
            s.RequestForUsers = ParseBool(raw, "RequestForUsers", false);
            s.RequestRecord = ParseBool(raw, "RequestRecord", true);

            // BUG-05 Fix: Read "ExportReport" primarily, fall back to "ReportExport" if present
            if (raw.ContainsKey("ExportReport"))
            {
                s.ExportReport = ParseBool(raw, "ExportReport", true);
            }
            else if (raw.ContainsKey("ReportExport"))
            {
                s.ExportReport = ParseBool(raw, "ReportExport", true);
            }

            s.ChangePassRestriction = ParseBool(raw, "ChangePassRestriction", false);
            s.ChangePassMonthly = ParseBool(raw, "ChangePassMonthly", false);
            s.ChangePassLimit = ParseInt(raw, "ChangePassLimit", 30);

            // BUG-03 Fix: Read ChangePassForce from "ChangePassForce" (NOT "ChangePassLimit")
            s.ChangePassForce = ParseBool(raw, "ChangePassForce", false);

            // BUG-02 Fix: Read CookiesSecure as bool (NOT int!)
            s.CookiesSecure = ParseBool(raw, "CookiesSecure", false);
            s.CookiesTime = ParseInt(raw, "CookiesTime", 12);
            s.Captcha = ParseBool(raw, "Captcha", false);

            return s;
        }

        public static IDictionary<string, string> MapToDictionary(TwfAppSettings s)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["RequestRecordsDailyLimit"] = s.RequestRecordsDailyLimit.ToString(CultureInfo.InvariantCulture),
                ["RequestRecordsMonthlyLimit"] = s.RequestRecordsMonthlyLimit.ToString(CultureInfo.InvariantCulture),
                ["ValidLateEarly"] = FormatBool(s.ValidLateEarly),
                ["ValidHourlyFree"] = FormatBool(s.ValidHourlyFree),
                ["HoursOfDays"] = s.HoursOfDays.ToString(CultureInfo.InvariantCulture),
                ["MaxRequestAgeDays"] = s.MaxRequestAgeDays.ToString(CultureInfo.InvariantCulture),
                ["LastDayForRequest"] = s.LastDayForRequest.ToString(CultureInfo.InvariantCulture),
                ["ShowDays"] = s.ShowDays.ToString(CultureInfo.InvariantCulture),
                ["RequestsLock"] = s.RequestsLockOADate.ToString(CultureInfo.InvariantCulture),
                ["LeaveID"] = s.DefaultLeaveId.ToString(CultureInfo.InvariantCulture),
                ["Limit1LeaveID"] = s.Limit1LeaveId.ToString(CultureInfo.InvariantCulture),
                ["HideLeaveTypes"] = s.HideLeaveTypes ?? "0",
                ["ViewDailyDuty"] = FormatBool(s.ViewDailyDuty),
                ["ViewHourlyDuty"] = FormatBool(s.ViewHourlyDuty),
                ["ViewDailyLeave"] = FormatBool(s.ViewDailyLeave),
                ["ViewHourlyLeave"] = FormatBool(s.ViewHourlyLeave),
                ["ViewOverTimeRequest"] = FormatBool(s.ViewOverTimeRequest),
                ["ViewRewPen"] = FormatBool(s.ViewRewardRequest),
                ["ViewConstOT"] = FormatBool(s.ViewConstOT),
                ["RequestForUsers"] = FormatBool(s.RequestForUsers),
                ["RequestRecord"] = FormatBool(s.RequestRecord),
                // BUG-05 Fix: Keep both "ExportReport" and "ReportExport" synchronized in Web.config
                ["ExportReport"] = FormatBool(s.ExportReport),
                ["ReportExport"] = FormatBool(s.ExportReport),
                ["ChangePassRestriction"] = FormatBool(s.ChangePassRestriction),
                ["ChangePassMonthly"] = FormatBool(s.ChangePassMonthly),
                ["ChangePassLimit"] = s.ChangePassLimit.ToString(CultureInfo.InvariantCulture),
                // BUG-03 Fix: Save ChangePassForce to "ChangePassForce"
                ["ChangePassForce"] = FormatBool(s.ChangePassForce),
                // BUG-02 Fix: Save CookiesSecure as boolean string
                ["CookiesSecure"] = FormatBool(s.CookiesSecure),
                ["CookiesTime"] = s.CookiesTime.ToString(CultureInfo.InvariantCulture),
                ["Captcha"] = FormatBool(s.Captcha)
            };

            if (!string.IsNullOrWhiteSpace(s.LogoPath))
                dict["LogoPath"] = s.LogoPath;

            return dict;
        }

        private static IDictionary<string, string> ReadRawDictionaryFromXml(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return dict;

            XDocument doc = XDocument.Load(path);
            XElement appSettings = doc.Root?.Element("appSettings");
            if (appSettings == null)
                return dict;

            foreach (XElement add in appSettings.Elements("add"))
            {
                string key = add.Attribute("key")?.Value;
                string val = add.Attribute("value")?.Value ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    dict[key.Trim()] = val;
                }
            }
            return dict;
        }

        private static void WriteRawDictionaryToXml(string path, IDictionary<string, string> updates)
        {
            XDocument doc;
            if (File.Exists(path))
            {
                doc = XDocument.Load(path);
            }
            else
            {
                doc = new XDocument(new XElement("configuration", new XElement("appSettings")));
            }

            XElement root = doc.Root;
            if (root == null)
            {
                root = new XElement("configuration");
                doc.Add(root);
            }

            XElement appSettings = root.Element("appSettings");
            if (appSettings == null)
            {
                appSettings = new XElement("appSettings");
                root.Add(appSettings);
            }

            foreach (var kv in updates)
            {
                XElement existing = null;
                foreach (XElement add in appSettings.Elements("add"))
                {
                    string k = add.Attribute("key")?.Value;
                    if (string.Equals(k, kv.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = add;
                        break;
                    }
                }

                if (existing != null)
                {
                    existing.SetAttributeValue("value", kv.Value);
                }
                else
                {
                    appSettings.Add(new XElement("add",
                        new XAttribute("key", kv.Key),
                        new XAttribute("value", kv.Value)));
                }
            }

            doc.Save(path);
        }

        private static string ParseString(IDictionary<string, string> dict, string key, string defaultValue)
        {
            if (dict.TryGetValue(key, out string raw) && raw != null)
                return raw.Trim();
            return defaultValue;
        }

        private static int ParseInt(IDictionary<string, string> dict, string key, int defaultValue)
        {
            if (dict.TryGetValue(key, out string raw) &&
                int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
            {
                return val;
            }
            return defaultValue;
        }

        private static double ParseDouble(IDictionary<string, string> dict, string key, double defaultValue)
        {
            if (dict.TryGetValue(key, out string raw) &&
                double.TryParse(raw?.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double val))
            {
                return val;
            }
            return defaultValue;
        }

        private static bool ParseBool(IDictionary<string, string> dict, string key, bool defaultValue)
        {
            if (!dict.TryGetValue(key, out string raw) || string.IsNullOrWhiteSpace(raw))
                return defaultValue;

            string trimmed = raw.Trim();
            if (bool.TryParse(trimmed, out bool b))
                return b;
            if (trimmed == "1")
                return true;
            if (trimmed == "0")
                return false;

            return defaultValue;
        }

        private static string FormatBool(bool value) => value ? "True" : "False";
    }
}
