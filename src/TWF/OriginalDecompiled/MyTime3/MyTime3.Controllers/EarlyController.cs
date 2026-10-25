using MyTime3.Content;
using MyTime3.Models;
using System;
using System.Linq;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class EarlyController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(string earlyDate = null)
		{
			if (earlyDate != null)
			{
				LateEarlyRequest lateEarlyRequest = new LateEarlyRequest();
				lateEarlyRequest.ForDateStr = earlyDate;
				return View(lateEarlyRequest);
			}
			return View();
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(LateEarlyRequest request)
		{
			base.ViewBag.Message = RequestEarly(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
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
					LateEarlyRequest lateEarlyRequest = new LateEarlyRequest();
					lateEarlyRequest.RequestID = byID.RequestID;
					lateEarlyRequest.Descriptions = byID.Descriptions;
					lateEarlyRequest.ForDateStr = DateConverter.ToHijriDate(byID.ForDate);
					return View(lateEarlyRequest);
				}
				return View("Error", "", PersianString.AccessDenied);
			}
			return RedirectToAction("Add");
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Edit(LateEarlyRequest request)
		{
			base.ViewBag.Message = EditRequestEarly(request);
			return View("Edit", request);
		}

		public static string EditRequestEarly(LateEarlyRequest request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			string text = "";
			if (request.Descriptions == null)
			{
				request.Descriptions = "";
			}
			DateTime? nullable = DateConverter.ToChristian(request.ForDateStr);
			if (nullable.HasValue)
			{
				T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(request.RequestID);
				if (byID != null)
				{
					byID.ForDate = nullable.Value;
					byID.Descriptions = request.Descriptions;
					if (t5DBInstance.RequestDA.Edit(byID, out string _))
					{
						text = PersianString.InsertSuccess;
						NotificationManager.NewRequestAdded(byID);
					}
					else
					{
						text = PersianString.ErrorInInsertingData;
					}
				}
				else
				{
					text = PersianString.ErrorInInsertingData;
				}
			}
			else
			{
				text = PersianString.BadDateFormat;
			}
			return text;
		}

		public static string RequestEarly(long badgeNumber, LateEarlyRequest request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			string text = "";
			if (request.Descriptions == null)
			{
				request.Descriptions = "";
			}
			DateTime? forDate = DateConverter.ToChristian(request.ForDateStr);
			if (forDate.HasValue)
			{
				bool flag = false;
				foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(badgeNumber, true).Where(delegate(T5V3.Model.Request r)
				{
					DateTime forDate2 = r.ForDate;
					DateTime? d = forDate;
					if ((DateTime?)forDate2 == d)
					{
						return r.WorkflowType == WorkflowOperations.LateEarly;
					}
					return false;
				}).ToList())
				{
					if (item.OverTimeAllowType == AllowType.Early)
					{
						flag = true;
					}
				}
				string text2 = EditRequestController.CheckRequestTime(forDate.Value);
				if (text2 == "")
				{
					if (!flag)
					{
						T5V3.Model.Request request2 = new T5V3.Model.Request();
						request2.AllowValue = new TimeSpan(0L);
						request2.BadgeNumber = badgeNumber;
						request2.CreateTime = DateTime.Now;
						request2.ForDate = forDate.Value;
						T5V3.Model.Request request3 = request2;
						DateTime now = DateTime.Now;
						request3.FromTime = now.Date;
						request2.IsSuspend = true;
						request2.IsDaily = false;
						request2.LeaveID = -1;
						request2.LeaveDescriptionID = -1;
						request2.OverTimeAllowType = AllowType.Early;
						T5V3.Model.Request request4 = request2;
						now = DateTime.Now;
						request4.RecordTime = now.Date;
						T5V3.Model.Request request5 = request2;
						now = DateTime.Now;
						request5.ToTime = now.Date;
						request2.WorkflowType = WorkflowOperations.LateEarly;
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
						text = PersianString.RequestExists;
					}
				}
				else
				{
					text = text2;
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
