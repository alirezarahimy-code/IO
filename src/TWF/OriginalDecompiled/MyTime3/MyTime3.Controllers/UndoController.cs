using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.DB.EntityManagers;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class UndoController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index()
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<MyTime3.Models.Request> list = new List<MyTime3.Models.Request>();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			if (loggedInBadgeNumber != -1)
			{
				DateTime dateTime = DateTime.FromOADate(HomeController.RequestLockValuee());
				foreach (KeyValuePair<long, bool> item in t5DBInstance.RequestAcceptDA.GetRequestIDsForUndo(loggedInBadgeNumber, dateTime.Date))
				{
					MyTime3.Models.Request request = new MyTime3.Models.Request();
					request.ForDeputy = false;
					request.RequestT5 = t5DBInstance.RequestDA.GetByID(item.Key);
					request.IsCanceled = item.Value;
					Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(request.RequestT5.BadgeNumber);
					if (byID != null)
					{
						request.RequestT5.RequesterName = byID.ToString();
					}
					list.Add(request);
				}
			}
			return View(list);
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Undo(long requestID)
		{
			DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			if (loggedInBadgeNumber != -1)
			{
				T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(requestID);
				if (byID.IsSuspend)
				{
					t5DBInstance.RequestAcceptDA.Delete(requestID, loggedInBadgeNumber);
				}
				else if (t5DBInstance.RequestAcceptDA.Delete(requestID, loggedInBadgeNumber))
				{
					DeleteData(requestID, out string err);
					byID.IsSuspend = true;
					T5V3.Model.Request request = byID;
					request.Descriptions += " - بازگشت داده شده";
					t5DBInstance.RequestDA.Edit(byID, out err);
				}
			}
			return RedirectToAction("Index");
		}

		private bool DeleteData(long requestID, out string err)
		{
			bool flag = false;
			DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			err = "";
			T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(requestID);
			DateTime dateTime;
			if (byID.WorkflowType == WorkflowOperations.Duty || byID.WorkflowType == WorkflowOperations.Leave)
			{
				if (byID.IsDaily)
				{
					dateTime = byID.ForDate;
					DateTime dateTime2 = dateTime.Date;
					while (true)
					{
						DateTime t = dateTime2;
						dateTime = byID.ToTime;
						if (!(t <= dateTime.Date))
						{
							break;
						}
						flag = t5DBInstance.DailyLeaveDA.Delete(t5DBInstance.DailyLeaveDA.GetByID(byID.BadgeNumber, dateTime2), out err);
						dateTime2 = dateTime2.AddDays(1.0);
					}
				}
				else
				{
					HourlyLeaveDA hourlyLeaveDA = t5DBInstance.HourlyLeaveDA;
					long badgeNumber = byID.BadgeNumber;
					dateTime = byID.ForDate;
					dateTime = dateTime.Date;
					DateTime dateTime3 = byID.FromTime;
					DateTime fromTime = dateTime.Add(dateTime3.TimeOfDay);
					dateTime = byID.ForDate;
					dateTime = dateTime.Date;
					dateTime3 = byID.ToTime;
					flag = hourlyLeaveDA.Delete(badgeNumber, fromTime, dateTime.Add(dateTime3.TimeOfDay));
				}
			}
			if (byID.WorkflowType == WorkflowOperations.LateEarly)
			{
				EditedValue editedValue = new EditedValue();
				editedValue.BadgeNumber = byID.BadgeNumber;
				EditedValue editedValue2 = editedValue;
				dateTime = byID.ForDate;
				editedValue2.ForDate = dateTime.Date;
				editedValue.ValidDuration = new TimeSpan(0L);
				editedValue.ValueType = ((byID.OverTimeAllowType == AllowType.Early) ? EditValueType.Early : EditValueType.Late);
				flag = t5DBInstance.EditedValueDA.Delete(editedValue, out err);
			}
			if (byID.WorkflowType == WorkflowOperations.RewardAndPenalty)
			{
				EditedValue editedValue3 = new EditedValue();
				editedValue3.BadgeNumber = byID.BadgeNumber;
				EditedValue editedValue4 = editedValue3;
				dateTime = byID.ForDate;
				editedValue4.ForDate = dateTime.Date;
				editedValue3.ValidDuration = byID.AllowValue;
				editedValue3.ValueType = ((byID.OverTimeAllowType == AllowType.Early) ? EditValueType.Penalty : EditValueType.Reward);
				flag = t5DBInstance.EditedValueDA.Delete(editedValue3, out err);
			}
			if (byID.WorkflowType == WorkflowOperations.AllowOverTimes)
			{
				long badgeNumber2 = byID.BadgeNumber;
				dateTime = byID.ForDate;
				AllowedValue allowValue = new AllowedValue(badgeNumber2, dateTime.Date, byID.OverTimeAllowType, byID.AllowValue);
				flag = t5DBInstance.AllowValueDA.Delete(allowValue);
			}
			if (byID.WorkflowType == WorkflowOperations.InsertRecord)
			{
				T5V3.Model.Record record = new T5V3.Model.Record();
				record.BadgeNumber = byID.BadgeNumber;
				record.CheckTime = byID.RecordTime;
				record.CheckType = 0;
				record.InsertTime = DateTime.Now;
				record.InsertUser = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
				record.IsValid = true;
				record.MachineNumber = -1;
				record.OriginalSCheckType = 0;
				record.SourceType = T5V3.Model.RecordSource.Manual;
				record.VerifyMode = T5V3.Model.RecordVerifyStyle.FP_OR_PW_OR_RF;
				record.WorkCode = 0;
				flag = t5DBInstance.RecordDA.Delete(record, out err);
			}
			if (flag)
			{
				byID.IsSuspend = true;
				flag = t5DBInstance.RequestDA.Edit(byID, out err);
			}
			return flag;
		}
	}
}
