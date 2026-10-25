using MyTime3.Content;
using MyTime3.Models;
using System;
using System.Configuration;
using System.Web.Configuration;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.Model;

namespace MyTime3.Controllers
{
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
		public ActionResult Change(SettingsModel model)
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			//IL_0025: Unknown result type (might be due to invalid IL or missing references)
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			//IL_003a: Unknown result type (might be due to invalid IL or missing references)
			//IL_003b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0040: Unknown result type (might be due to invalid IL or missing references)
			//IL_004f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0050: Unknown result type (might be due to invalid IL or missing references)
			//IL_0055: Unknown result type (might be due to invalid IL or missing references)
			//IL_0064: Unknown result type (might be due to invalid IL or missing references)
			//IL_0065: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0079: Unknown result type (might be due to invalid IL or missing references)
			//IL_007a: Unknown result type (might be due to invalid IL or missing references)
			//IL_007f: Unknown result type (might be due to invalid IL or missing references)
			//IL_008e: Unknown result type (might be due to invalid IL or missing references)
			//IL_008f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0094: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00be: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
			//IL_010c: Unknown result type (might be due to invalid IL or missing references)
			//IL_010d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0112: Unknown result type (might be due to invalid IL or missing references)
			//IL_0121: Unknown result type (might be due to invalid IL or missing references)
			//IL_0122: Unknown result type (might be due to invalid IL or missing references)
			//IL_0127: Unknown result type (might be due to invalid IL or missing references)
			//IL_0136: Unknown result type (might be due to invalid IL or missing references)
			//IL_0137: Unknown result type (might be due to invalid IL or missing references)
			//IL_013c: Unknown result type (might be due to invalid IL or missing references)
			//IL_014b: Unknown result type (might be due to invalid IL or missing references)
			//IL_014c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0151: Unknown result type (might be due to invalid IL or missing references)
			//IL_0160: Unknown result type (might be due to invalid IL or missing references)
			//IL_0161: Unknown result type (might be due to invalid IL or missing references)
			//IL_0166: Unknown result type (might be due to invalid IL or missing references)
			//IL_0175: Unknown result type (might be due to invalid IL or missing references)
			//IL_0176: Unknown result type (might be due to invalid IL or missing references)
			//IL_017b: Unknown result type (might be due to invalid IL or missing references)
			//IL_018a: Unknown result type (might be due to invalid IL or missing references)
			//IL_018b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0190: Unknown result type (might be due to invalid IL or missing references)
			//IL_019f: Unknown result type (might be due to invalid IL or missing references)
			//IL_01a0: Unknown result type (might be due to invalid IL or missing references)
			//IL_01a5: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b4: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b5: Unknown result type (might be due to invalid IL or missing references)
			//IL_01ba: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_01de: Unknown result type (might be due to invalid IL or missing references)
			//IL_01df: Unknown result type (might be due to invalid IL or missing references)
			//IL_01e4: Unknown result type (might be due to invalid IL or missing references)
			//IL_0201: Unknown result type (might be due to invalid IL or missing references)
			//IL_0202: Unknown result type (might be due to invalid IL or missing references)
			//IL_0207: Unknown result type (might be due to invalid IL or missing references)
			//IL_0224: Unknown result type (might be due to invalid IL or missing references)
			//IL_0225: Unknown result type (might be due to invalid IL or missing references)
			//IL_022a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0247: Unknown result type (might be due to invalid IL or missing references)
			//IL_0248: Unknown result type (might be due to invalid IL or missing references)
			//IL_024d: Unknown result type (might be due to invalid IL or missing references)
			//IL_026a: Unknown result type (might be due to invalid IL or missing references)
			//IL_026b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0270: Unknown result type (might be due to invalid IL or missing references)
			//IL_028d: Unknown result type (might be due to invalid IL or missing references)
			//IL_028e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0293: Unknown result type (might be due to invalid IL or missing references)
			//IL_02b0: Unknown result type (might be due to invalid IL or missing references)
			//IL_02b1: Unknown result type (might be due to invalid IL or missing references)
			//IL_02b6: Unknown result type (might be due to invalid IL or missing references)
			//IL_02d3: Unknown result type (might be due to invalid IL or missing references)
			//IL_02d4: Unknown result type (might be due to invalid IL or missing references)
			//IL_02d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_02f6: Unknown result type (might be due to invalid IL or missing references)
			//IL_02f7: Unknown result type (might be due to invalid IL or missing references)
			//IL_02fc: Unknown result type (might be due to invalid IL or missing references)
			//IL_0319: Unknown result type (might be due to invalid IL or missing references)
			//IL_031a: Unknown result type (might be due to invalid IL or missing references)
			//IL_031f: Unknown result type (might be due to invalid IL or missing references)
			//IL_033c: Unknown result type (might be due to invalid IL or missing references)
			//IL_033d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0342: Unknown result type (might be due to invalid IL or missing references)
			//IL_035f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0360: Unknown result type (might be due to invalid IL or missing references)
			//IL_0365: Unknown result type (might be due to invalid IL or missing references)
			//IL_0382: Unknown result type (might be due to invalid IL or missing references)
			//IL_0383: Unknown result type (might be due to invalid IL or missing references)
			//IL_0388: Unknown result type (might be due to invalid IL or missing references)
			//IL_03a5: Unknown result type (might be due to invalid IL or missing references)
			//IL_03a6: Unknown result type (might be due to invalid IL or missing references)
			//IL_03ab: Unknown result type (might be due to invalid IL or missing references)
			//IL_03c8: Unknown result type (might be due to invalid IL or missing references)
			//IL_03c9: Unknown result type (might be due to invalid IL or missing references)
			//IL_03ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_03eb: Unknown result type (might be due to invalid IL or missing references)
			//IL_03ec: Unknown result type (might be due to invalid IL or missing references)
			//IL_03f1: Unknown result type (might be due to invalid IL or missing references)
			//IL_040e: Unknown result type (might be due to invalid IL or missing references)
			//IL_040f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0414: Unknown result type (might be due to invalid IL or missing references)
			//IL_0431: Unknown result type (might be due to invalid IL or missing references)
			//IL_0432: Unknown result type (might be due to invalid IL or missing references)
			//IL_0437: Unknown result type (might be due to invalid IL or missing references)
			//IL_0454: Unknown result type (might be due to invalid IL or missing references)
			//IL_0455: Unknown result type (might be due to invalid IL or missing references)
			//IL_045a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0477: Unknown result type (might be due to invalid IL or missing references)
			//IL_0478: Unknown result type (might be due to invalid IL or missing references)
			//IL_047d: Unknown result type (might be due to invalid IL or missing references)
			//IL_049a: Unknown result type (might be due to invalid IL or missing references)
			//IL_049b: Unknown result type (might be due to invalid IL or missing references)
			//IL_04a0: Unknown result type (might be due to invalid IL or missing references)
			string text = "";
			try
			{
				Configuration val = WebConfigurationManager.OpenWebConfiguration("~");
				val.get_AppSettings().get_Settings().Remove("RequestRecordsDailyLimit");
				val.get_AppSettings().get_Settings().Remove("RequestRecordsMonthlyLimit");
				val.get_AppSettings().get_Settings().Remove("ValidLateEarly");
				val.get_AppSettings().get_Settings().Remove("ValidHourlyFree");
				val.get_AppSettings().get_Settings().Remove("ViewDailyDuty");
				val.get_AppSettings().get_Settings().Remove("RequestRecord");
				val.get_AppSettings().get_Settings().Remove("ViewHourlyDuty");
				val.get_AppSettings().get_Settings().Remove("ViewDailyLeave");
				val.get_AppSettings().get_Settings().Remove("ViewHourlyLeave");
				val.get_AppSettings().get_Settings().Remove("ViewRewPen");
				val.get_AppSettings().get_Settings().Remove("ViewConstOT");
				val.get_AppSettings().get_Settings().Remove("MaxRequestAgeDays");
				val.get_AppSettings().get_Settings().Remove("LastDayForRequest");
				val.get_AppSettings().get_Settings().Remove("ViewOverTimeRequest");
				val.get_AppSettings().get_Settings().Remove("RequestForUsers");
				val.get_AppSettings().get_Settings().Remove("ExportReport");
				val.get_AppSettings().get_Settings().Remove("HoursOfDays");
				val.get_AppSettings().get_Settings().Remove("ChangePassRestriction");
				val.get_AppSettings().get_Settings().Remove("ChangePassMonthly");
				val.get_AppSettings().get_Settings().Remove("ChangePassLimit");
				val.get_AppSettings().get_Settings().Remove("ChangePassForce");
				val.Save();
			}
			catch
			{
			}
			try
			{
				Configuration val2 = WebConfigurationManager.OpenWebConfiguration("~");
				KeyValueConfigurationCollection settings = val2.get_AppSettings().get_Settings();
				int num = model.RequestRecordsDailyLimit;
				settings.Add("RequestRecordsDailyLimit", num.ToString());
				KeyValueConfigurationCollection settings2 = val2.get_AppSettings().get_Settings();
				num = model.RequestRecordsMonthlyLimit;
				settings2.Add("RequestRecordsMonthlyLimit", num.ToString());
				KeyValueConfigurationCollection settings3 = val2.get_AppSettings().get_Settings();
				bool flag = model.AllowValidLate;
				settings3.Add("ValidLateEarly", flag.ToString());
				KeyValueConfigurationCollection settings4 = val2.get_AppSettings().get_Settings();
				flag = model.ViewDailyDuty;
				settings4.Add("ViewDailyDuty", flag.ToString());
				KeyValueConfigurationCollection settings5 = val2.get_AppSettings().get_Settings();
				flag = model.ViewHourlyDuty;
				settings5.Add("ViewHourlyDuty", flag.ToString());
				KeyValueConfigurationCollection settings6 = val2.get_AppSettings().get_Settings();
				flag = model.AllowValidHourlyFree;
				settings6.Add("ValidHourlyFree", flag.ToString());
				KeyValueConfigurationCollection settings7 = val2.get_AppSettings().get_Settings();
				num = model.HoursOfDays;
				settings7.Add("HoursOfDays", num.ToString());
				KeyValueConfigurationCollection settings8 = val2.get_AppSettings().get_Settings();
				flag = model.ViewDailyLeave;
				settings8.Add("ViewDailyLeave", flag.ToString());
				KeyValueConfigurationCollection settings9 = val2.get_AppSettings().get_Settings();
				flag = model.ViewHourlyLeave;
				settings9.Add("ViewHourlyLeave", flag.ToString());
				KeyValueConfigurationCollection settings10 = val2.get_AppSettings().get_Settings();
				num = model.MaxRequestAgeDays;
				settings10.Add("MaxRequestAgeDays", num.ToString());
				KeyValueConfigurationCollection settings11 = val2.get_AppSettings().get_Settings();
				flag = model.ViewRewardRequest;
				settings11.Add("ViewRewPen", flag.ToString());
				KeyValueConfigurationCollection settings12 = val2.get_AppSettings().get_Settings();
				flag = model.RequestRecord;
				settings12.Add("RequestRecord", flag.ToString());
				KeyValueConfigurationCollection settings13 = val2.get_AppSettings().get_Settings();
				flag = model.ViewConstOT;
				settings13.Add("ViewConstOT", flag.ToString());
				KeyValueConfigurationCollection settings14 = val2.get_AppSettings().get_Settings();
				num = model.LastDayForRequest;
				settings14.Add("LastDayForRequest", num.ToString());
				KeyValueConfigurationCollection settings15 = val2.get_AppSettings().get_Settings();
				flag = model.ViewOverTimeRequest;
				settings15.Add("ViewOverTimeRequest", flag.ToString());
				KeyValueConfigurationCollection settings16 = val2.get_AppSettings().get_Settings();
				flag = model.RequestForUser;
				settings16.Add("RequestForUsers", flag.ToString());
				KeyValueConfigurationCollection settings17 = val2.get_AppSettings().get_Settings();
				flag = model.ExportReport;
				settings17.Add("ExportReport", flag.ToString());
				KeyValueConfigurationCollection settings18 = val2.get_AppSettings().get_Settings();
				flag = model.ChangePassRestriction;
				settings18.Add("ChangePassRestriction", flag.ToString());
				KeyValueConfigurationCollection settings19 = val2.get_AppSettings().get_Settings();
				flag = model.ChangePassMonthly;
				settings19.Add("ChangePassMonthly", flag.ToString());
				KeyValueConfigurationCollection settings20 = val2.get_AppSettings().get_Settings();
				num = model.ChangePassLimit;
				settings20.Add("ChangePassLimit", num.ToString());
				KeyValueConfigurationCollection settings21 = val2.get_AppSettings().get_Settings();
				flag = model.ChangePassForce;
				settings21.Add("ChangePassForce", flag.ToString());
				val2.Save();
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				foreach (Rule item in t5DBInstance.RuleDA.GetAll())
				{
					item.LimitRecReqInDay = model.RequestRecordsDailyLimit;
					item.LimitRecReqInMonth = model.RequestRecordsMonthlyLimit;
					string err = "";
					t5DBInstance.RuleDA.Edit(item, out err);
				}
				text = PersianString.InsertSuccess;
			}
			catch (Exception ex)
			{
				text = ex.Message;
				model = LoadSettings();
			}
			model.OverAllMessage = text;
			return View("Index", model);
		}

		private SettingsModel LoadSettings()
		{
			SettingsModel settingsModel = new SettingsModel();
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
				settingsModel.ExportReport = (bool)LoadValue("ExportReport", typeof(bool), true);
				settingsModel.ChangePassRestriction = (bool)LoadValue("ChangePassRestriction", typeof(bool), false);
				settingsModel.ChangePassMonthly = (bool)LoadValue("ChangePassMonthly", typeof(bool), false);
				settingsModel.ChangePassLimit = (int)LoadValue("ChangePassLimit", typeof(int), 3);
				settingsModel.ChangePassForce = (bool)LoadValue("ChangePassLimit", typeof(bool), false);
				return settingsModel;
			}
			catch
			{
				return settingsModel;
			}
		}

		public static object LoadValue(string name, Type type, object defaultValue)
		{
			object result = defaultValue;
			try
			{
				if (!(type == typeof(bool)))
				{
					if (!(type == typeof(int)))
					{
						return result;
					}
					result = int.Parse(WebConfigurationManager.get_AppSettings()[name]);
					return result;
				}
				result = bool.Parse(WebConfigurationManager.get_AppSettings()[name]);
				return result;
			}
			catch
			{
				return result;
			}
		}
	}
}
