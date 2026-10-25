using Microsoft.Reporting.WebForms;
using MyTime3.Authorization;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;
using T5V3.ReportGenerator;

namespace MyTime3.Controllers
{
	public class ReportController : Controller
	{
		public class DayResult
		{
			public string ForDateHijri;

			public int ForDateDayOfWeek;

			public string[] Records;

			public string Present;

			public string Work;

			public string Duty;

			public string Leave;

			public string Late;

			public string Early;

			public string Less;

			public string Rest;

			public string OverTime;

			public string Name;

			public string State;

			public DayResult(ADayResult init)
			{
				Name = init.UserInfo.ToString();
				ForDateHijri = DateConverter.ToHijriDate(init.ForDate);
				DateTime dateTime = init.ForDate;
				ForDateDayOfWeek = (int)dateTime.DayOfWeek;
				if (ForDateDayOfWeek == 0)
				{
					ForDateDayOfWeek = 7;
				}
				Records = new string[6];
				ProcessedRecord orderRecord = init.GetOrderRecord(true, 1, false);
				ProcessedRecord orderRecord2 = init.GetOrderRecord(true, 2, false);
				ProcessedRecord orderRecord3 = init.GetOrderRecord(true, 3, false);
				ProcessedRecord orderRecord4 = init.GetOrderRecord(false, 1, false);
				ProcessedRecord orderRecord5 = init.GetOrderRecord(false, 2, false);
				ProcessedRecord orderRecord6 = init.GetOrderRecord(false, 3, false);
				string[] records = Records;
				object obj;
				if (orderRecord == null)
				{
					obj = "";
				}
				else
				{
					dateTime = orderRecord.CheckTime;
					obj = dateTime.ToString("HH:mm") + ((orderRecord.Source == T5V3.Model.RecordSource.Manual) ? "*" : "");
				}
				records[0] = (string)obj;
				string[] records2 = Records;
				object obj2;
				if (orderRecord2 == null)
				{
					obj2 = "";
				}
				else
				{
					dateTime = orderRecord2.CheckTime;
					obj2 = dateTime.ToString("HH:mm") + ((orderRecord2.Source == T5V3.Model.RecordSource.Manual) ? "*" : "");
				}
				records2[1] = (string)obj2;
				string[] records3 = Records;
				object obj3;
				if (orderRecord3 == null)
				{
					obj3 = "";
				}
				else
				{
					dateTime = orderRecord3.CheckTime;
					obj3 = dateTime.ToString("HH:mm") + ((orderRecord3.Source == T5V3.Model.RecordSource.Manual) ? "*" : "");
				}
				records3[2] = (string)obj3;
				string[] records4 = Records;
				object obj4;
				if (orderRecord4 == null)
				{
					obj4 = "";
				}
				else
				{
					dateTime = orderRecord4.CheckTime;
					obj4 = dateTime.ToString("HH:mm") + ((orderRecord4.Source == T5V3.Model.RecordSource.Manual) ? "*" : "");
				}
				records4[3] = (string)obj4;
				string[] records5 = Records;
				object obj5;
				if (orderRecord5 == null)
				{
					obj5 = "";
				}
				else
				{
					dateTime = orderRecord5.CheckTime;
					obj5 = dateTime.ToString("HH:mm") + ((orderRecord5.Source == T5V3.Model.RecordSource.Manual) ? "*" : "");
				}
				records5[4] = (string)obj5;
				string[] records6 = Records;
				object obj6;
				if (orderRecord6 == null)
				{
					obj6 = "";
				}
				else
				{
					dateTime = orderRecord6.CheckTime;
					obj6 = dateTime.ToString("HH:mm") + ((orderRecord6.Source == T5V3.Model.RecordSource.Manual) ? "*" : "");
				}
				records6[5] = (string)obj6;
				Present = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.PerecentTime + init.GetFreeOverTimesValidLen(), "hhh\\:mm");
				Work = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.WorkTime, "hhh\\:mm");
				Duty = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.TotalDutyHours(), "hhh\\:mm");
				Leave = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.TotalLeaveHours(), "hhh\\:mm");
				Late = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.LateLast, "hh\\:mm");
				Early = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.EarlyLast, "hh\\:mm");
				Less = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.LessWorkTotalLast, "hh\\:mm");
				Rest = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.LaunchTime, "hh\\:mm");
				OverTime = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.OverTimeAfterLast + init.GetFreeOverTimesValidLen() + init.ProcessedSchedule.OverTimeBeforeLast, "hhh\\:mm");
				string state = PersianString.WOShift;
				if (init.CurrentShift.ActiveSchedule != null)
				{
					state = "حاضر";
				}
				if (init.ProcessedSchedule.Absent)
				{
					state = PersianString.Absent;
				}
				if (init.IsHoliday)
				{
					state = PersianString.Holiday;
				}
				if (init.IsDailyLeave)
				{
					state = init.DailyLeaveValue.DailyLeaveType.LeaveName;
				}
				State = state;
			}
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index()
		{
			ReportModel reportModel = new ReportModel();
			reportModel.IsSelf = true;
			AddReportsToViewBag();
			AddPersonnelToViewBag();
			return View(reportModel);
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Report(ReportModel rpt, string action, string exportType)
		{
			//IL_009e: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00de: Unknown result type (might be due to invalid IL or missing references)
			bool flag = false;
			string data = "";
			if (rpt.ReportID <= 0)
			{
				flag = true;
				data = PersianString.NoReportSelected;
			}
			DateTime? nullable = DateConverter.ToChristian(rpt.FromDateHijri);
			DateTime? nullable2 = DateConverter.ToChristian(rpt.ToDateHijri);
			if (!nullable.HasValue || !nullable2.HasValue || nullable2.Value < nullable.Value)
			{
				flag = true;
				data = PersianString.BadDateFormat;
			}
			if (flag)
			{
				return Json(data);
			}
			if (action == PersianString.DownloadExportFile)
			{
				string requestData = base.Request.get_Params().ToString();
				ReportViewer reportViewer = new ReportViewer();
				reportViewer.LocalReport.LoadReportDefinition(GetSchema(base.Request.get_Cookies().Get("mytimeauth").get_Value(), requestData));
				reportViewer.LocalReport.DataSources.Add(new ReportDataSource("DataSet1", GetData(base.Request.get_Cookies().Get("mytimeauth").get_Value(), requestData)));
				byte[] fileContents = reportViewer.LocalReport.Render(exportType);
				string contentType = "";
				string arg = "";
				if (exportType == "EXCEL")
				{
					contentType = "application/vnd.ms-excel";
					arg = "xls";
				}
				else if (exportType == "PDF")
				{
					contentType = "application/pdf";
					arg = "pdf";
				}
				else if (exportType == "WORD")
				{
					contentType = "application/msword";
					arg = "doc";
				}
				return File(fileContents, contentType, $"report.{arg}");
			}
			return View();
		}

		[HttpPost]
		public JsonResult GetPersonnel(LoginModel model)
		{
			if (!GlobalHelper.WithTimeApp())
			{
				return null;
			}
			List<PersonnelModel> list = new List<PersonnelModel>();
			DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long num = personnelSystem.DB.UserInfos.CheckUserPass(model.UserName, model.Password, true);
			if (num > 0)
			{
				int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(num);
				List<Personnel.Model.TitleChart> source = new List<Personnel.Model.TitleChart>();
				if (userCurrentTitleID != -1)
				{
					source = personnelSystem.DB.TitlesCharts.GetAll(userCurrentTitleID);
				}
				foreach (Personnel.Model.UserInfo item in personnelSystem.DB.UserInfos.GetAllByTitle((from t in source
				select t.TitleID).ToList()))
				{
					if (item.DeptID >= 0)
					{
						list.Add(new PersonnelModel(item.BadgeNumber, item.FirstName, item.LastName));
					}
				}
			}
			return Json(list);
		}

		[HttpPost]
		public JsonResult TotalCalcsReport(string userName, string password, string badgeNumbers, string fromDateHijri, string toDateHijri)
		{
			if (!GlobalHelper.WithTimeApp())
			{
				return null;
			}
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			List<TotalCalcsModel> list = new List<TotalCalcsModel>();
			long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				if (badgeNumbers == null || badgeNumbers == string.Empty)
				{
					badgeNumbers = num.ToString();
				}
				List<long> list2 = new List<long>();
				long result = 0L;
				string[] array = badgeNumbers.Split(new char[1]
				{
					';'
				}, StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < array.Length; i++)
				{
					if (long.TryParse(array[i], out result))
					{
						list2.Add(result);
					}
				}
				DateTime? nullable = DateConverter.ToChristian(fromDateHijri);
				DateTime? nullable2 = DateConverter.ToChristian(toDateHijri);
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				PersonnelSystem personnelSystem2 = DBInitializer.GetPersonnelSystem();
				if (nullable.HasValue && nullable2.HasValue)
				{
					foreach (long item in list2)
					{
						Personnel.Model.UserInfo byID = personnelSystem2.DB.UserInfos.GetByID(item);
						List<ADayResult> list3 = new Attendance(t5DBInstance, personnelSystem2).Calculate(item, nullable.Value, nullable2.Value);
						double num2 = 0.0;
						double num3 = 0.0;
						double num4 = 0.0;
						double num5 = 0.0;
						double num6 = 0.0;
						double num7 = 0.0;
						double num8 = 0.0;
						TimeSpan t = new TimeSpan(0L);
						TimeSpan t2 = new TimeSpan(0L);
						TimeSpan t3 = new TimeSpan(0L);
						TimeSpan t4 = new TimeSpan(0L);
						TimeSpan timeSpan = new TimeSpan(0L);
						TimeSpan timeSpan2 = new TimeSpan(0L);
						TimeSpan t5 = new TimeSpan(0L);
						TimeSpan t6 = new TimeSpan(0L);
						TimeSpan timeSpan3 = new TimeSpan(0L);
						TimeSpan t7 = new TimeSpan(0L);
						TimeSpan t8 = new TimeSpan(0L);
						TimeSpan t9 = new TimeSpan(0L);
						foreach (ADayResult item2 in list3)
						{
							num2 += 1.0;
							if (item2.CurrentShift.ActiveSchedule != null)
							{
								num3 += 1.0;
							}
							if (!item2.ProcessedSchedule.Absent)
							{
								if (item2.CurrentShift.ActiveSchedule != null && !item2.IsDailyLeave && !item2.IsHoliday)
								{
									num4 += 1.0;
								}
							}
							else
							{
								num5 += 1.0;
							}
							if (item2.IsHoliday)
							{
								num6 += 1.0;
							}
							if (item2.IsDailyLeave)
							{
								if (item2.IsHoliday || item2.CurrentShift.ActiveSchedule == null)
								{
									if (item2.DailyLeaveValue.DailyLeaveType.GetInHolidays)
									{
										if (item2.DailyLeaveValue.DailyLeaveType.LeaveType1 == LeaveTypeEnum.Duty)
										{
											num7 += 1.0;
										}
										else
										{
											num8 += 1.0;
										}
									}
								}
								else if (item2.DailyLeaveValue.DailyLeaveType.LeaveType1 == LeaveTypeEnum.Duty)
								{
									num7 += 1.0;
								}
								else
								{
									num8 += 1.0;
								}
							}
							t += item2.ProcessedSchedule.FullTime;
							t2 += item2.ProcessedSchedule.WorkTime;
							t3 += item2.ProcessedSchedule.TotalDutyHours();
							t4 += item2.ProcessedSchedule.TotalLeaveHours();
							t5 += item2.ProcessedSchedule.LateLast;
							t6 += item2.ProcessedSchedule.EarlyLast;
							t7 += item2.ProcessedSchedule.LessWorkTotalLast;
							t9 += item2.ProcessedSchedule.LaunchTime;
							timeSpan += item2.ConstOverTime;
							timeSpan3 += item2.ProcessedSchedule.OverTimeAfterLast + item2.ProcessedSchedule.OverTimeBeforeLast;
							foreach (FreeOverTime freeOverTime in item2.FreeOverTimes)
							{
								t8 += freeOverTime.ValidLen;
							}
						}
						TotalCalcsModel totalCalcsModel = new TotalCalcsModel();
						totalCalcsModel.BadgeNumber = item;
						totalCalcsModel.FullName = byID.ToString();
						totalCalcsModel.FirstName = byID.FirstName;
						totalCalcsModel.LastName = byID.LastName;
						totalCalcsModel.TotalDays = num2;
						totalCalcsModel.ShiftDays = num3;
						totalCalcsModel.PresentDays = num4;
						totalCalcsModel.AbsentDays = num5;
						totalCalcsModel.OverTimeReal = timeSpan3.TotalHours;
						totalCalcsModel.FreeOverTime = t8.TotalHours;
						totalCalcsModel.TotalOverTime = (t8 + timeSpan + timeSpan3).TotalHours;
						totalCalcsModel.Late = t5.TotalHours;
						totalCalcsModel.Early = t6.TotalHours;
						totalCalcsModel.TotalLess = t7.TotalHours;
						totalCalcsModel.DutyHourly = t3.TotalHours;
						totalCalcsModel.LeaveHourly = t4.TotalHours;
						totalCalcsModel.DutyDaily = num7;
						totalCalcsModel.LeaveDaily = num8;
						totalCalcsModel.OverTimeManager = timeSpan.TotalHours;
						totalCalcsModel.OverTimeConst = timeSpan2.TotalHours;
						totalCalcsModel.WorkDays = num2 - num5;
						list.Add(totalCalcsModel);
					}
				}
			}
			return Json(list);
		}

		[HttpPost]
		public JsonResult DailyReport(string userName, string password, string badgeNumbers, string fromDateHijri, string toDateHijri)
		{
			if (!GlobalHelper.WithTimeApp())
			{
				return null;
			}
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			List<DayResult> list = new List<DayResult>();
			long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				if (badgeNumbers == null || badgeNumbers == string.Empty)
				{
					badgeNumbers = num.ToString();
				}
				DateTime? nullable = DateConverter.ToChristian(fromDateHijri);
				DateTime? nullable2 = DateConverter.ToChristian(toDateHijri);
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				PersonnelSystem personnelSystem2 = DBInitializer.GetPersonnelSystem();
				List<long> list2 = new List<long>();
				long result = 0L;
				string[] array = badgeNumbers.Split(new char[1]
				{
					';'
				}, StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < array.Length; i++)
				{
					if (long.TryParse(array[i], out result))
					{
						list2.Add(result);
					}
				}
				if (nullable.HasValue && nullable2.HasValue)
				{
					foreach (long item in list2)
					{
						personnelSystem2.DB.UserInfos.GetByID(item);
						foreach (ADayResult item2 in new Attendance(t5DBInstance, personnelSystem2).Calculate(item, nullable.Value, nullable2.Value))
						{
							list.Add(new DayResult(item2));
						}
					}
				}
			}
			return Json(list);
		}

		private void AddPersonnelToViewBag()
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			List<PersonnelModel> list = new List<PersonnelModel>();
			int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
			List<Personnel.Model.TitleChart> source = new List<Personnel.Model.TitleChart>();
			if (loggedInUserInfoTitleID != -1)
			{
				source = personnelSystem.DB.TitlesCharts.GetAll(loggedInUserInfoTitleID);
			}
			List<Personnel.Model.UserInfo>.Enumerator enumerator = personnelSystem.DB.UserInfos.GetAllByTitle((from t in source
			select t.TitleID).ToList()).GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					Personnel.Model.UserInfo current = enumerator.Current;
					list.Add(new PersonnelModel(current.BadgeNumber, current.FirstName, current.LastName));
				}
			}
			finally
			{
				((IDisposable)enumerator).Dispose();
			}
			foreach (long item in t5DBInstance.PersonnelDeputyDA.IsDeputy(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext)))
			{
				int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
				if (userCurrentTitleID != -1)
				{
					source = personnelSystem.DB.TitlesCharts.GetAll(userCurrentTitleID);
					enumerator = personnelSystem.DB.UserInfos.GetAllByTitle((from t in source
					select t.TitleID).ToList()).GetEnumerator();
					try
					{
						while (enumerator.MoveNext())
						{
							Personnel.Model.UserInfo current3 = enumerator.Current;
							list.Add(new PersonnelModel(current3.BadgeNumber, current3.FirstName, current3.LastName));
						}
					}
					finally
					{
						((IDisposable)enumerator).Dispose();
					}
				}
			}
			base.ViewBag.Personnel = list;
		}

		private void AddReportsToViewBag()
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<SelectListItem> list = new List<SelectListItem>();
			bool selected = true;
			int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
			foreach (Reportmdl item in t5DBInstance.ReportDA.GetAll())
			{
				if (t5DBInstance.ReportAccessDA.GetAll(item.ReportID).Contains(loggedInUserInfoTitleID))
				{
					SelectListItem selectListItem = new SelectListItem();
					selectListItem.Selected = selected;
					selected = false;
					selectListItem.Text = item.ReportName;
					selectListItem.Value = item.ReportID.ToString();
					list.Add(selectListItem);
				}
			}
			base.ViewBag.Reports = list;
		}

		public static MemoryStream GetSchema(string authCookie, string requestData)
		{
			try
			{
				Thread.CurrentThread.CurrentCulture = new CultureInfo("fa-IR");
				Thread.CurrentThread.CurrentUICulture = new CultureInfo("fa-IR");
				Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator = ".";
				Thread.CurrentThread.CurrentUICulture.NumberFormat.NumberDecimalSeparator = ".";
			}
			catch
			{
			}
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			MyTime3.Authorization.Encryption.Decrypt(authCookie).Split(new char[1]
			{
				';'
			}, StringSplitOptions.RemoveEmptyEntries);
			int reportID = 1;
			string[] array = requestData.Split(new char[1]
			{
				'&'
			});
			for (int i = 0; i < array.Length; i++)
			{
				string[] array2 = array[i].Split(new char[1]
				{
					'='
				});
				if (array2.Length == 2 && array2[0] == "ReportID")
				{
					reportID = Convert.ToInt32(array2[1]);
				}
			}
			return ReportSchemaGenerator.GetSchema(t5DBInstance.ReportDA.GetByID(reportID), t5DBInstance);
		}

		public static DataTable GetData(string authCookie, string requestData)
		{
			try
			{
				Thread.CurrentThread.CurrentCulture = new CultureInfo("fa-IR");
				Thread.CurrentThread.CurrentUICulture = new CultureInfo("fa-IR");
				Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator = ".";
				Thread.CurrentThread.CurrentUICulture.NumberFormat.NumberDecimalSeparator = ".";
			}
			catch
			{
			}
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			string[] array = MyTime3.Authorization.Encryption.Decrypt(authCookie).Split(new char[1]
			{
				';'
			}, StringSplitOptions.RemoveEmptyEntries);
			long badgeNumber = long.Parse(array[1]);
			List<Personnel.Model.TitleChart> all = personnelSystem.DB.TitlesCharts.GetAll(personnelSystem.DB.UserInfos.GetUserCurrentTitleID(badgeNumber));
			List<Personnel.Model.UserInfo> allByTitle = personnelSystem.DB.UserInfos.GetAllByTitle((from t in all
			select t.TitleID).ToList());
			foreach (long item in t5DBInstance.PersonnelDeputyDA.IsDeputy(badgeNumber))
			{
				int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
				if (userCurrentTitleID != -1)
				{
					all = personnelSystem.DB.TitlesCharts.GetAll(userCurrentTitleID);
					allByTitle.AddRange(personnelSystem.DB.UserInfos.GetAllByTitle((from t in all
					select t.TitleID).ToList()));
				}
			}
			int reportID = 1;
			bool flag = true;
			DateTime? nullable = DateTime.Now.AddMonths(-1);
			DateTime? nullable2 = DateTime.Now;
			List<long> list = new List<long>();
			string[] array2 = requestData.Split(new char[1]
			{
				'&'
			});
			for (int i = 0; i < array2.Length; i++)
			{
				string[] array3 = array2[i].Split(new char[1]
				{
					'='
				});
				if (array3.Length == 2)
				{
					if (array3[0] == "ReportID")
					{
						reportID = Convert.ToInt32(array3[1]);
					}
					else if (array3[0] == "FromDateHijri")
					{
						nullable = DateConverter.ToChristian(HttpUtility.UrlDecode(array3[1]));
					}
					else if (array3[0] == "ToDateHijri")
					{
						nullable2 = DateConverter.ToChristian(HttpUtility.UrlDecode(array3[1]));
					}
					else if (array3[0] == "SelectedBadgeNumbers")
					{
						long num = long.Parse(array3[1]);
						if ((from u in allByTitle
						select u.BadgeNumber).Contains(num))
						{
							list.Add(num);
						}
					}
					else if (array3[0].ToLower() == "isself")
					{
						flag = bool.Parse(array3[1]);
					}
				}
			}
			if (flag)
			{
				list = new List<long>
				{
					long.Parse(array[1])
				};
			}
			Reportmdl byID = t5DBInstance.ReportDA.GetByID(reportID);
			byID.ReportDatas = t5DBInstance.ReportDataDA.GetAll(byID.ReportID);
			new DataGenerator
			{
				LeaveTypes = t5DBInstance.LeaveTypeDA.GetAll(),
				Schedules = t5DBInstance.ScheduleDA.GetAll(),
				UserParameters = new List<GeneratedParameter>()
			};
			List<ADayResult> t5Results = new List<ADayResult>();
			return new ReportDataGenerator().CalculateData(t5DBInstance, personnelSystem, list, nullable.Value, nullable2.Value, byID, out t5Results);
		}
	}
}
