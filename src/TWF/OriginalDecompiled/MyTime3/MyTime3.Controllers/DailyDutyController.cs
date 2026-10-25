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
	public class DailyDutyController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(string forDate = null)
		{
			DailyExitRequest dailyExitRequest = new DailyExitRequest();
			GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
			GlobalHelper.AddLeaveDescriptions(base.ViewBag);
			if (forDate != null)
			{
				dailyExitRequest.ForDateHijri = forDate;
				dailyExitRequest.ToDateHijri = forDate;
			}
			return View(dailyExitRequest);
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(DailyExitRequest request)
		{
			base.ViewBag.Message = RequestDaily(request);
			GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
			GlobalHelper.AddLeaveDescriptions(base.ViewBag);
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
					GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
					GlobalHelper.AddLeaveDescriptions(base.ViewBag);
					DailyExitRequest dailyExitRequest = new DailyExitRequest();
					dailyExitRequest.RequestID = byID.RequestID;
					dailyExitRequest.Descriptions = byID.Descriptions;
					dailyExitRequest.ForDateHijri = DateConverter.ToHijriDate(byID.ForDate);
					dailyExitRequest.ToDateHijri = DateConverter.ToHijriDate(byID.ToTime);
					dailyExitRequest.LeaveDescriptionID = byID.LeaveDescriptionID;
					dailyExitRequest.LeaveID = byID.LeaveID;
					return View(dailyExitRequest);
				}
				return View("Error", "", PersianString.AccessDenied);
			}
			return RedirectToAction("Add");
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Edit(DailyExitRequest request)
		{
			base.ViewBag.Message = EditRequestDaily(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
			GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
			GlobalHelper.AddLeaveDescriptions(base.ViewBag);
			return View("Edit", request);
		}

		public static string EditRequestDaily(long badgeNumber, DailyExitRequest request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			if (request.Descriptions == null)
			{
				request.Descriptions = "";
			}
			string text = "";
			if (request.LeaveDescriptionID != 0)
			{
				if (request.LeaveID != 0)
				{
					DateTime? nullable = DateConverter.ToChristian(request.ForDateHijri);
					DateTime? nullable2 = DateConverter.ToChristian(request.ToDateHijri);
					if (nullable.HasValue && nullable2.HasValue)
					{
						DateTime value = nullable2.Value;
						DateTime date = value.Date;
						value = nullable.Value;
						if (date >= value.Date)
						{
							if (t5DBInstance.DailyLeaveDA.GetByID(badgeNumber, nullable.Value) == null)
							{
								T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(request.RequestID);
								if (byID != null)
								{
									byID.LeaveID = request.LeaveID;
									byID.LeaveDescriptionID = request.LeaveDescriptionID;
									byID.ForDate = nullable.Value;
									byID.ToTime = nullable2.Value;
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
								text = PersianString.DailyLeaveExists;
							}
							goto IL_014f;
						}
					}
					text = PersianString.BadDateFormat;
				}
				else
				{
					text = PersianString.BadExitType;
				}
			}
			else
			{
				text = PersianString.BadDescriptions;
			}
			goto IL_014f;
			IL_014f:
			return text;
		}

		private string RequestDaily(DailyExitRequest request)
		{
			return AddRequest(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
		}

		public static string AddRequest(long badgeNumber, DailyExitRequest request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			if (request.Descriptions == null)
			{
				request.Descriptions = "";
			}
			string text = "";
			if (request.LeaveDescriptionID != 0)
			{
				if (request.LeaveID != 0)
				{
					DateTime? forDate = DateConverter.ToChristian(request.ForDateHijri);
					DateTime? toDate = DateConverter.ToChristian(request.ToDateHijri);
					if (toDate.HasValue && forDate.HasValue)
					{
						DateTime dateTime = toDate.Value;
						DateTime date = dateTime.Date;
						dateTime = forDate.Value;
						if (date >= dateTime.Date)
						{
							if (t5DBInstance.DailyLeaveDA.GetByID(badgeNumber, forDate.Value) == null)
							{
								List<T5V3.Model.Request> source = (from r in t5DBInstance.RequestDA.GetAll(badgeNumber, true)
								where r.IsDaily
								select r).ToList();
								if (source.Where(delegate(T5V3.Model.Request r)
								{
									if (r.ForDate <= forDate.Value)
									{
										return r.ToTime >= forDate.Value;
									}
									return false;
								}).Count() == 0 && source.Where(delegate(T5V3.Model.Request r)
								{
									if (r.ForDate <= toDate.Value)
									{
										return r.ToTime >= toDate.Value;
									}
									return false;
								}).Count() == 0)
								{
									string text2 = EditRequestController.CheckRequestTime(forDate.Value);
									if (text2 == "")
									{
										T5V3.Model.Request request2 = new T5V3.Model.Request();
										request2.AllowValue = new TimeSpan(0L);
										request2.BadgeNumber = badgeNumber;
										request2.CreateTime = DateTime.Now;
										request2.ForDate = forDate.Value;
										T5V3.Model.Request request3 = request2;
										dateTime = DateTime.Now;
										request3.FromTime = dateTime.Date;
										request2.IsSuspend = true;
										request2.IsDaily = true;
										request2.LeaveID = request.LeaveID;
										request2.LeaveDescriptionID = request.LeaveDescriptionID;
										request2.OverTimeAllowType = AllowType.Early;
										T5V3.Model.Request request4 = request2;
										dateTime = DateTime.Now;
										request4.RecordTime = dateTime.Date;
										request2.ToTime = toDate.Value;
										request2.WorkflowType = WorkflowOperations.Duty;
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
										text = text2;
									}
								}
								else
								{
									text = PersianString.RequestDailyForDateExists;
								}
							}
							else
							{
								text = PersianString.DailyLeaveExists;
							}
							goto IL_0279;
						}
					}
					text = PersianString.BadDateFormat;
				}
				else
				{
					text = PersianString.BadExitType;
				}
			}
			else
			{
				text = PersianString.BadDescriptions;
			}
			goto IL_0279;
			IL_0279:
			return text;
		}
	}
}
