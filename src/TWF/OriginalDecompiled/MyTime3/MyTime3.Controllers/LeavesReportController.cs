using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class LeavesReportController : Controller
	{
		public class LV
		{
			public long BadgeNumber
			{
				get;
				set;
			}

			public string Name
			{
				get;
				set;
			}

			public string Type
			{
				get;
				set;
			}

			public string FromTo
			{
				get;
				set;
			}

			public string Descriptions
			{
				get;
				set;
			}
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index()
		{
			List<LV> list = new List<LV>();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			if (isObs(base.HttpContext))
			{
				personnelSystem.DB.Departments.GetAll();
				GlobalHelper.GetLoggedInUserInfo(base.HttpContext);
				int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
				List<int> allGuardAccess = personnelSystem.DB.TitlesCharts.GetAllGuardAccess(loggedInUserInfoTitleID);
				IEnumerable<Personnel.Model.UserInfo> source = from u in personnelSystem.DB.UserInfos.GetAll(allGuardAccess)
				where u.DeptID > 0
				select u;
				foreach (long item in (from r in source
				select r.BadgeNumber).ToList())
				{
					Personnel.Model.UserInfo userInfo = (from u in source
					where u.BadgeNumber == item
					select u).First();
					Attendance attendance = new Attendance(t5DBInstance, personnelSystem);
					long badgeNumber2 = item;
					DateTime dateTime = DateTime.Now;
					DateTime date = dateTime.Date;
					dateTime = DateTime.Now;
					dateTime = dateTime.Date;
					foreach (ADayResult item2 in attendance.Calculate(badgeNumber2, date, dateTime.AddDays(1.0)))
					{
						DateTime forDate = item2.ForDate;
						dateTime = DateTime.Now;
						if (forDate == dateTime.Date)
						{
							if (item2.IsDailyLeave)
							{
								if (item2.DailyLeaveValue.DailyLeaveType.LeaveType1 == LeaveTypeEnum.Duty || item2.DailyLeaveValue.DailyLeaveType.LeaveType1 == LeaveTypeEnum.Leave)
								{
									LV lV = new LV();
									lV.BadgeNumber = item;
									lV.Name = userInfo.ToString();
									lV.Type = item2.DailyLeaveValue.DailyLeaveType.LeaveName;
									LV lV2 = lV;
									dateTime = DateTime.Now;
									lV2.FromTo = DateConverter.ToHijriDate(dateTime.Date);
									LV lV3 = lV;
									long bg = item;
									dateTime = DateTime.Now;
									lV3.Descriptions = DescriptionClass.GetDescriptionInDate(bg, dateTime.Date);
									list.Add(lV);
								}
							}
							else
							{
								foreach (InShiftExit inShiftExit in item2.ProcessedSchedule.InShiftExits)
								{
									if (!inShiftExit.IsLessWork)
									{
										LV lV4 = new LV();
										lV4.Name = userInfo.ToString();
										lV4.BadgeNumber = item;
										lV4.Type = inShiftExit.lvType.LeaveName;
										LV lV5 = lV4;
										dateTime = inShiftExit.FromTime;
										string str = dateTime.ToString("HH:mm");
										dateTime = inShiftExit.ToTime;
										lV5.FromTo = str + " - " + dateTime.ToString("HH:mm");
										lV4.Descriptions = DescriptionClass.GetDescriptionInDate(item, inShiftExit.FromTime);
										list.Add(lV4);
									}
								}
							}
						}
					}
				}
			}
			return View(list);
		}

		public static bool isObs(HttpContextBase con)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(con);
			if (loggedInUserInfoTitleID > 0)
			{
				bool? observer = personnelSystem.DB.TitlesCharts.GetByID(loggedInUserInfoTitleID).Observer;
				if (observer.HasValue && observer.Value)
				{
					return true;
				}
			}
			return false;
		}
	}
}
