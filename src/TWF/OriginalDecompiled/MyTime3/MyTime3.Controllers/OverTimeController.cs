using MyTime3.Content;
using MyTime3.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class OverTimeController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(string forDate = null, int valueType = -1, string validValue = null)
		{
			OverTimeRequest overTimeRequest = new OverTimeRequest();
			GlobalHelper.AddOverTimeTypes(base.HttpContext, base.ViewBag);
			overTimeRequest.ValidValue = "00:00";
			if (forDate != null)
			{
				overTimeRequest.ForDateHijri = forDate;
			}
			if (validValue != null)
			{
				overTimeRequest.ValidValue = validValue;
			}
			if (valueType != -1)
			{
				overTimeRequest.OverTimeType = valueType;
			}
			return View(overTimeRequest);
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(OverTimeRequest request)
		{
			GlobalHelper.AddOverTimeTypes(base.HttpContext, base.ViewBag);
			base.ViewBag.Message = RequestOverTime(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
			return View("Add", request);
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Edit(long id)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(id);
			if (byID != null)
			{
				if (byID.BadgeNumber == GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext) && t5DBInstance.RequestAcceptDA.IsEditableRequest(id))
				{
					return RedirectToAction("Add", (object)new
					{
						forDate = DateConverter.ToHijriDate(byID.ForDate),
						valueType = (int)byID.OverTimeAllowType,
						validValue = byID.AllowValue.ToString()
					});
				}
				return View("Error", "", PersianString.AccessDenied);
			}
			return RedirectToAction("Add");
		}

		public static string RequestOverTime(long badgeNumber, OverTimeRequest request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			string text = "";
			DateTime? forDate = DateConverter.ToChristian(request.ForDateHijri);
			if (forDate.HasValue)
			{
				TimeSpan result = default(TimeSpan);
				if (TimeSpan.TryParse(request.ValidValue, out result))
				{
					IEnumerable<T5V3.Model.Request> source = t5DBInstance.RequestDA.GetAll(badgeNumber, true).Where(delegate(T5V3.Model.Request r)
					{
						if (r.ForDate == forDate.Value && r.WorkflowType == WorkflowOperations.AllowOverTimes)
						{
							return r.OverTimeAllowType == (AllowType)request.OverTimeType;
						}
						return false;
					});
					if (source.Count() == 0)
					{
						T5V3.Model.Request request2 = new T5V3.Model.Request();
						request2.AllowValue = result;
						request2.BadgeNumber = badgeNumber;
						request2.CreateTime = DateTime.Now;
						request2.ForDate = forDate.Value;
						T5V3.Model.Request request3 = request2;
						DateTime now = DateTime.Now;
						request3.FromTime = now.Date;
						request2.IsSuspend = true;
						request2.IsDaily = true;
						request2.LeaveID = 0;
						request2.LeaveDescriptionID = 0;
						request2.OverTimeAllowType = (AllowType)request.OverTimeType;
						T5V3.Model.Request request4 = request2;
						now = DateTime.Now;
						request4.RecordTime = now.Date;
						T5V3.Model.Request request5 = request2;
						now = DateTime.Now;
						request5.ToTime = now.Date;
						request2.WorkflowType = WorkflowOperations.AllowOverTimes;
						request2.Descriptions = request.Descriptions;
						if (t5DBInstance.RequestDA.AddNew(request2, true, out string _, out long newID))
						{
							text = PersianString.InsertSuccess;
							request2.RequestID = newID;
							NotificationManager.NewRequestAdded(request2);
						}
						else
						{
							text = PersianString.ErrorInInsertingData;
						}
					}
					else
					{
						T5V3.Model.Request request6 = source.First();
						request6.AllowValue = result;
						request6.CreateTime = DateTime.Now;
						if (t5DBInstance.RequestDA.Edit(request6, out string _))
						{
							text = PersianString.InsertSuccess;
							NotificationManager.NewRequestAdded(request6);
						}
						else
						{
							text = PersianString.ErrorInInsertingData;
						}
					}
				}
				else
				{
					text = PersianString.BadTimeFormat;
				}
			}
			else
			{
				text = PersianString.BadDateFormat;
			}
			return text;
		}
	}
}
