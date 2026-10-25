using MyTime3.Authorization;
using MyTime3.Content;
using MyTime3.Controllers;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.Model;

namespace MyTime3
{
	public static class GlobalHelper
	{
		public static bool LockIsOK
		{
			get;
			set;
		}

		public static int PersonnelCount
		{
			get;
			set;
		}

		public static bool FulLogoSize
		{
			get;
			set;
		}

		public static string TimeApp
		{
			get;
			set;
		}

		public static bool WithTimeApp()
		{
			bool result = false;
			try
			{
				if (TimeApp == null)
				{
					return result;
				}
				if (!(TimeApp != ""))
				{
					return result;
				}
				string[] array = TimeApp.Split(new char[1]
				{
					':'
				}, StringSplitOptions.RemoveEmptyEntries);
				if (array.Length != 2)
				{
					return result;
				}
				PersianCalendar persianCalendar = new PersianCalendar();
				string text = array[1];
				if (int.Parse(text) != 0)
				{
					if (!(persianCalendar.ToDateTime(int.Parse(text.Substring(0, 4)), int.Parse(text.Substring(4, 2)), int.Parse(text.Substring(6, 2)), 1, 1, 1, 1) < DateTime.Now))
					{
						return result;
					}
					result = true;
					return result;
				}
				result = true;
				return result;
			}
			catch
			{
				return result;
			}
		}

		public static string GetLoggedInUserName(HttpContextBase httpContext)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			string result = "";
			string badgeNumber = LoginManager.GetBadgeNumber(httpContext);
			long result2 = 0L;
			if (long.TryParse(badgeNumber, out result2))
			{
				result = personnelSystem.DB.UserInfos.GetByID(result2).ToString();
			}
			return result;
		}

		public static long GetLoggedInBadgeNumber(HttpContextBase httpContext)
		{
			long result = -1L;
			string badgeNumber = LoginManager.GetBadgeNumber(httpContext);
			long result2 = 0L;
			if (long.TryParse(badgeNumber, out result2))
			{
				result = result2;
			}
			return result;
		}

		public static Personnel.Model.UserInfo GetLoggedInUserInfo(HttpContextBase httpContext)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			Personnel.Model.UserInfo result = null;
			string badgeNumber = LoginManager.GetBadgeNumber(httpContext);
			long result2 = 0L;
			if (long.TryParse(badgeNumber, out result2))
			{
				result = personnelSystem.DB.UserInfos.GetByID(result2);
			}
			return result;
		}

		public static Personnel.Model.UserInfo GetUserInfo(long badgeNumber)
		{
			return DBInitializer.GetPersonnelSystem().DB.UserInfos.GetByID(badgeNumber);
		}

		public static int GetLoggedInUserInfoTitleID(HttpContextBase httpContext)
		{
			int result = -1;
			string badgeNumber = LoginManager.GetBadgeNumber(httpContext);
			long result2 = 0L;
			if (long.TryParse(badgeNumber, out result2))
			{
				result = DBInitializer.GetPersonnelSystem().DB.UserInfos.GetUserCurrentTitleID(result2);
			}
			return result;
		}

		public static void AddLeaveTypes(HttpContextBase httpContext, dynamic ViewBag)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			bool selected = true;
			List<SelectListItem> list = new List<SelectListItem>();
			List<SelectListItem> list2 = new List<SelectListItem>();
			List<int> all = t5DBInstance.EmpLeavesDA.GetAll(GetLoggedInUserInfo(httpContext).EmpTypeID);
			foreach (LeaveType item in from lt in t5DBInstance.LeaveTypeDA.GetAll()
			where lt.LeaveType1 == LeaveTypeEnum.Leave
			select lt)
			{
				if (all.Contains(item.LeaveTypeID))
				{
					int leaveTypeID;
					if (item.IsHourly)
					{
						SelectListItem selectListItem = new SelectListItem();
						selectListItem.Text = item.LeaveName;
						SelectListItem selectListItem2 = selectListItem;
						leaveTypeID = item.LeaveTypeID;
						selectListItem2.Value = leaveTypeID.ToString();
						selectListItem.Selected = selected;
						selected = false;
						list2.Add(selectListItem);
					}
					else
					{
						SelectListItem selectListItem3 = new SelectListItem();
						selectListItem3.Text = item.LeaveName;
						SelectListItem selectListItem4 = selectListItem3;
						leaveTypeID = item.LeaveTypeID;
						selectListItem4.Value = leaveTypeID.ToString();
						selectListItem3.Selected = selected;
						selected = false;
						list.Add(selectListItem3);
					}
				}
			}
			ViewBag.DailyLeaveTypes = list;
			ViewBag.HourlyLeaveTypes = list2;
		}

		public static void AddOverTimeTypes(HttpContextBase httpContext, dynamic ViewBag)
		{
			List<SelectListItem> list = new List<SelectListItem>();
			SelectListItem selectListItem = new SelectListItem();
			selectListItem.Text = PersianString.OverTimeAfter;
			SelectListItem selectListItem2 = selectListItem;
			int num = 1;
			selectListItem2.Value = num.ToString();
			selectListItem.Selected = false;
			list.Add(selectListItem);
			SelectListItem selectListItem3 = new SelectListItem();
			selectListItem3.Text = PersianString.OverTimeBefore;
			SelectListItem selectListItem4 = selectListItem3;
			num = 0;
			selectListItem4.Value = num.ToString();
			selectListItem3.Selected = false;
			list.Add(selectListItem3);
			SelectListItem selectListItem5 = new SelectListItem();
			selectListItem5.Text = PersianString.FreeOverTime;
			SelectListItem selectListItem6 = selectListItem5;
			num = 2;
			selectListItem6.Value = num.ToString();
			selectListItem5.Selected = false;
			list.Add(selectListItem5);
			ViewBag.OverTimeTypes = list;
		}

		public static void AddRewardPenaltyTypes(HttpContextBase httpContext, dynamic ViewBag)
		{
			List<SelectListItem> list = new List<SelectListItem>();
			SelectListItem selectListItem = new SelectListItem();
			selectListItem.Text = PersianString.Reward;
			SelectListItem selectListItem2 = selectListItem;
			int num = 7;
			selectListItem2.Value = num.ToString();
			selectListItem.Selected = false;
			list.Add(selectListItem);
			SelectListItem selectListItem3 = new SelectListItem();
			selectListItem3.Text = PersianString.Penalty;
			SelectListItem selectListItem4 = selectListItem3;
			num = 6;
			selectListItem4.Value = num.ToString();
			selectListItem3.Selected = false;
			list.Add(selectListItem3);
			ViewBag.RewardPenaltyTypes = list;
		}

		public static void AddDutyTypes(HttpContextBase httpContext, dynamic ViewBag)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			bool selected = true;
			List<SelectListItem> list = new List<SelectListItem>();
			List<SelectListItem> list2 = new List<SelectListItem>();
			List<int> all = t5DBInstance.EmpLeavesDA.GetAll(GetLoggedInUserInfo(httpContext).EmpTypeID);
			foreach (LeaveType item in from lt in t5DBInstance.LeaveTypeDA.GetAll()
			where lt.LeaveType1 == LeaveTypeEnum.Duty
			select lt)
			{
				if (all.Contains(item.LeaveTypeID))
				{
					int leaveTypeID;
					if (item.IsHourly)
					{
						SelectListItem selectListItem = new SelectListItem();
						selectListItem.Text = item.LeaveName;
						SelectListItem selectListItem2 = selectListItem;
						leaveTypeID = item.LeaveTypeID;
						selectListItem2.Value = leaveTypeID.ToString();
						selectListItem.Selected = selected;
						selected = false;
						list2.Add(selectListItem);
					}
					else
					{
						SelectListItem selectListItem3 = new SelectListItem();
						selectListItem3.Text = item.LeaveName;
						SelectListItem selectListItem4 = selectListItem3;
						leaveTypeID = item.LeaveTypeID;
						selectListItem4.Value = leaveTypeID.ToString();
						selectListItem3.Selected = selected;
						selected = false;
						list.Add(selectListItem3);
					}
				}
			}
			ViewBag.DailyDutyTypes = list;
			ViewBag.HourlyDutyTypes = list2;
		}

		public static void AddLeaveDescriptions(dynamic ViewBag)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			bool selected = true;
			List<SelectListItem> list = new List<SelectListItem>();
			List<SelectListItem> list2 = new List<SelectListItem>();
			foreach (LeaveDescription item in t5DBInstance.LeaveDescriptionDA.GetAll())
			{
				int iD;
				if (item.ForLeaveType == LeaveTypeEnum.Duty)
				{
					SelectListItem selectListItem = new SelectListItem();
					selectListItem.Text = item.Description;
					SelectListItem selectListItem2 = selectListItem;
					iD = item.ID;
					selectListItem2.Value = iD.ToString();
					selectListItem.Selected = selected;
					selected = false;
					list.Add(selectListItem);
				}
				else
				{
					SelectListItem selectListItem3 = new SelectListItem();
					selectListItem3.Text = item.Description;
					SelectListItem selectListItem4 = selectListItem3;
					iD = item.ID;
					selectListItem4.Value = iD.ToString();
					selectListItem3.Selected = selected;
					selected = false;
					list2.Add(selectListItem3);
				}
			}
			ViewBag.DutyDescriptions = list;
			ViewBag.LeaveDescriptions = list2;
		}

		public static Rule GetRule(long badgeNumber, DateTime forDate)
		{
			Rule result = null;
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			UserRule userRule = t5DBInstance.UserRuleDA.GetAll(new List<long>
			{
				badgeNumber
			}).Where(delegate(UserRule r)
			{
				if (r.RuleType == RuleType.Personnal && r.FromDate <= forDate)
				{
					return r.ToDate >= forDate;
				}
				return false;
			}).FirstOrDefault();
			if (userRule != null)
			{
				result = t5DBInstance.RuleDA.GetByID(userRule.RuleID);
			}
			else
			{
				List<DepartmentRule> all = t5DBInstance.DepartmentRuleDA.GetAll();
				List<Personnel.Model.Department> all2 = personnelSystem.DB.Departments.GetAll();
				int deptID;
				Personnel.Model.Department department;
				for (deptID = personnelSystem.DB.UserInfos.GetByID(badgeNumber).DeptID; deptID > 0; deptID = department.UpperDeptID)
				{
					DepartmentRule departmentRule = all.Where(delegate(DepartmentRule r)
					{
						if (r.DepartmentID == deptID && r.FromDate <= forDate)
						{
							return r.ToDate >= forDate;
						}
						return false;
					}).FirstOrDefault();
					if (departmentRule != null)
					{
						result = t5DBInstance.RuleDA.GetByID(departmentRule.RuleID);
						break;
					}
					department = (from d in all2
					where d.DepartmentID == deptID
					select d).FirstOrDefault();
					if (department == null)
					{
						break;
					}
				}
			}
			return result;
		}
	}
}
