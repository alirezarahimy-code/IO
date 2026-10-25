using MyTime3.Content;
using MyTime3.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class HourlyDutyController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(string forDate = null, string fromTime = null, string toTime = null)
		{
			GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
			GlobalHelper.AddLeaveDescriptions(base.ViewBag);
			if (forDate != null || fromTime != null || toTime != null)
			{
				HourlyExitRequest hourlyExitRequest = new HourlyExitRequest();
				hourlyExitRequest.ForDateHijri = forDate;
				hourlyExitRequest.FromTimeStr = fromTime;
				hourlyExitRequest.ToTimeStr = toTime;
				return View(hourlyExitRequest);
			}
			return View();
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(HourlyExitRequest request)
		{
			base.ViewBag.Message = RequestHourly(request);
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
					HourlyExitRequest hourlyExitRequest = new HourlyExitRequest();
					hourlyExitRequest.RequestID = byID.RequestID;
					HourlyExitRequest hourlyExitRequest2 = hourlyExitRequest;
					DateTime dateTime = byID.FromTime;
					hourlyExitRequest2.FromTimeStr = dateTime.ToString("HH:mm");
					HourlyExitRequest hourlyExitRequest3 = hourlyExitRequest;
					dateTime = byID.ToTime;
					hourlyExitRequest3.ToTimeStr = dateTime.ToString("HH:mm");
					hourlyExitRequest.Descriptions = byID.Descriptions;
					hourlyExitRequest.ForDateHijri = DateConverter.ToHijriDate(byID.ForDate);
					hourlyExitRequest.LeaveDescriptionID = byID.LeaveDescriptionID;
					hourlyExitRequest.LeaveID = byID.LeaveID;
					return View(hourlyExitRequest);
				}
				return View("Error", "", PersianString.AccessDenied);
			}
			return RedirectToAction("Add");
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Edit(HourlyExitRequest request)
		{
			base.ViewBag.Message = EditRequestHourly(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
			GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
			GlobalHelper.AddLeaveDescriptions(base.ViewBag);
			return View("Edit", request);
		}

		public static string EditRequestHourly(long badgeNumber, HourlyExitRequest request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			if (request.Descriptions == null)
			{
				request.Descriptions = "";
			}
			string result = "";
			if (request.LeaveDescriptionID != 0)
			{
				if (request.LeaveID != 0)
				{
					DateTime? nullable = DateConverter.ToChristian(request.ForDateHijri);
					if (nullable.HasValue)
					{
						DateTime result2 = default(DateTime);
						DateTime result3 = default(DateTime);
						DateTime.TryParse(request.FromTimeStr, out result2);
						DateTime.TryParse(request.ToTimeStr, out result3);
						DateTime d = result2;
						DateTime d2 = default(DateTime);
						if (d != d2)
						{
							DateTime d3 = result3;
							d2 = default(DateTime);
							if (d3 != d2 && result2 < result3)
							{
								LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
								if (t5DBInstance.DailyLeaveDA.GetByID(badgeNumber, nullable.Value) == null)
								{
									bool flag = false;
									PersianCalendar persianCalendar = new PersianCalendar();
									int year = persianCalendar.GetYear(nullable.Value);
									int month = persianCalendar.GetMonth(nullable.Value);
									DateTime fromDate = persianCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
									DateTime toDate = persianCalendar.ToDateTime(year, month, persianCalendar.GetDaysInMonth(year, month), 23, 59, 0, 0);
									TimeSpan timeSpan = default(TimeSpan);
									foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(badgeNumber, fromDate, toDate).Where(delegate(T5V3.Model.Request r)
									{
										if (r.RequestID != request.RequestID && r.LeaveID == request.LeaveID && r.IsSuspend)
										{
											return r.WorkflowType == WorkflowOperations.Leave;
										}
										return false;
									}))
									{
										if (item.FromTime > item.ToTime)
										{
											TimeSpan t = timeSpan;
											d2 = item.ToTime;
											timeSpan = t + (d2.AddHours(24.0) - item.FromTime);
										}
										else
										{
											timeSpan += item.ToTime - item.FromTime;
										}
									}
									foreach (HourlyLeave item2 in from r in t5DBInstance.HourlyLeaveDA.GetAll(new List<long>
									{
										badgeNumber
									}, fromDate, toDate)
									where r.LeaveTypeID == request.LeaveID
									select r)
									{
										if (item2.FromTime > item2.ToTime)
										{
											TimeSpan t2 = timeSpan;
											d2 = item2.ToTime;
											timeSpan = t2 + (d2.AddHours(24.0) - item2.FromTime);
										}
										else
										{
											timeSpan += item2.ToTime - item2.FromTime;
										}
									}
									timeSpan += result3 - result2;
									if (timeSpan > byID.MaxHoursInAMonth)
									{
										flag = true;
									}
									if (!(bool)SettingsController.LoadValue("ValidHourlyFree", typeof(bool), true))
									{
										if (CheckRemainDays())
										{
											T5V3.Model.Request byID2 = t5DBInstance.RequestDA.GetByID(request.RequestID);
											if (!flag)
											{
												if (byID2 != null)
												{
													byID2.LeaveID = request.LeaveID;
													byID2.LeaveDescriptionID = request.LeaveDescriptionID;
													byID2.ForDate = nullable.Value;
													byID2.Descriptions = request.Descriptions;
													byID2.FromTime = result2;
													byID2.ToTime = result3;
													if (t5DBInstance.RequestDA.Edit(byID2, out string _))
													{
														result = PersianString.InsertSuccess;
														NotificationManager.NewRequestAdded(byID2);
													}
													else
													{
														result = PersianString.ErrorInInsertingData;
													}
												}
												else
												{
													result = PersianString.ErrorInInsertingData;
												}
											}
											else
											{
												result = string.Format(PersianString.HourlyLeavePerMonthLimit, byID.MaxHoursInAMonth.TotalHours.ToString("0.0"));
											}
										}
										else
										{
											result = PersianString.RemainLeaveError;
										}
									}
								}
								else
								{
									result = PersianString.DailyLeaveExists;
								}
								goto IL_042b;
							}
						}
						result = PersianString.BadTimeFormat;
					}
					else
					{
						result = PersianString.BadDateFormat;
					}
				}
				else
				{
					result = PersianString.BadExitType;
				}
			}
			else
			{
				result = PersianString.BadDescriptions;
			}
			goto IL_042b;
			IL_042b:
			return result;
		}

		private static bool CheckRemainDays()
		{
			DBInitializer.GetT5DBInstance();
			DBInitializer.GetPersonnelSystem();
			return true;
		}

		private string RequestHourly(HourlyExitRequest request)
		{
			return AddRequest(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
		}

		public static string AddRequest(long badgeNumber, HourlyExitRequest request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			string text = "";
			if (request.Descriptions == null)
			{
				request.Descriptions = "";
			}
			if (request.LeaveDescriptionID != 0)
			{
				if (request.LeaveID != 0)
				{
					DateTime? forDate = DateConverter.ToChristian(request.ForDateHijri);
					if (forDate.HasValue)
					{
						DateTime result = default(DateTime);
						DateTime result2 = default(DateTime);
						DateTime.TryParse(request.FromTimeStr, out result);
						DateTime.TryParse(request.ToTimeStr, out result2);
						DateTime d = result;
						DateTime d2 = default(DateTime);
						if (d != d2)
						{
							DateTime d3 = result2;
							d2 = default(DateTime);
							if (d3 != d2 && result < result2)
							{
								LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
								bool flag = false;
								List<T5V3.Model.Request> list = t5DBInstance.RequestDA.GetAll(badgeNumber, true).Where(delegate(T5V3.Model.Request r)
								{
									DateTime forDate3 = r.ForDate;
									DateTime? d5 = forDate;
									if ((DateTime?)forDate3 == d5)
									{
										return !r.IsDaily;
									}
									return false;
								}).ToList();
								list.AddRange(t5DBInstance.RequestDA.GetAll(badgeNumber, false).Where(delegate(T5V3.Model.Request r)
								{
									DateTime forDate2 = r.ForDate;
									DateTime? d4 = forDate;
									if ((DateTime?)forDate2 == d4)
									{
										return !r.IsDaily;
									}
									return false;
								}).ToList());
								d2 = forDate.Value;
								d2 = d2.Date;
								DateTime fromTime = d2.Add(result.TimeOfDay);
								d2 = forDate.Value;
								d2 = d2.Date;
								TimeInterval i = new TimeInterval(fromTime, d2.Add(result2.TimeOfDay));
								foreach (T5V3.Model.Request item in list)
								{
									d2 = item.ForDate;
									DateTime dateTime = item.FromTime;
									DateTime fromTime2 = d2.Add(dateTime.TimeOfDay);
									d2 = item.ForDate;
									dateTime = item.ToTime;
									TimeInterval i2 = new TimeInterval(fromTime2, d2.Add(dateTime.TimeOfDay));
									if (TimeInterval.HaveIntersection(i, i2))
									{
										flag = true;
									}
								}
								string text2 = EditRequestController.CheckRequestTime(forDate.Value);
								if (text2 == "")
								{
									if (!flag)
									{
										bool flag2 = false;
										PersianCalendar persianCalendar = new PersianCalendar();
										int year = persianCalendar.GetYear(forDate.Value);
										int month = persianCalendar.GetMonth(forDate.Value);
										DateTime fromDate = persianCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
										DateTime toDate = persianCalendar.ToDateTime(year, month, persianCalendar.GetDaysInMonth(year, month), 23, 59, 0, 0);
										TimeSpan timeSpan = default(TimeSpan);
										foreach (T5V3.Model.Request item2 in t5DBInstance.RequestDA.GetAll(badgeNumber, fromDate, toDate).Where(delegate(T5V3.Model.Request r)
										{
											if (r.LeaveID == request.LeaveID && r.IsSuspend)
											{
												return r.WorkflowType == WorkflowOperations.Duty;
											}
											return false;
										}))
										{
											if (item2.FromTime > item2.ToTime)
											{
												TimeSpan t = timeSpan;
												d2 = item2.ToTime;
												timeSpan = t + (d2.AddHours(24.0) - item2.FromTime);
											}
											else
											{
												timeSpan += item2.ToTime - item2.FromTime;
											}
										}
										foreach (HourlyLeave item3 in from r in t5DBInstance.HourlyLeaveDA.GetAll(new List<long>
										{
											badgeNumber
										}, fromDate, toDate)
										where r.LeaveTypeID == request.LeaveID
										select r)
										{
											if (item3.FromTime > item3.ToTime)
											{
												TimeSpan t2 = timeSpan;
												d2 = item3.ToTime;
												timeSpan = t2 + (d2.AddHours(24.0) - item3.FromTime);
											}
											else
											{
												timeSpan += item3.ToTime - item3.FromTime;
											}
										}
										timeSpan += result2 - result;
										if (timeSpan > byID.MaxHoursInAMonth)
										{
											flag2 = true;
										}
										if (!flag2)
										{
											T5V3.Model.Request request2 = new T5V3.Model.Request();
											request2.AllowValue = new TimeSpan(0L);
											request2.BadgeNumber = badgeNumber;
											request2.CreateTime = DateTime.Now;
											request2.ForDate = forDate.Value;
											request2.FromTime = result;
											request2.IsSuspend = true;
											request2.IsDaily = false;
											request2.LeaveID = request.LeaveID;
											request2.LeaveDescriptionID = request.LeaveDescriptionID;
											request2.OverTimeAllowType = AllowType.Early;
											T5V3.Model.Request request3 = request2;
											d2 = DateTime.Now;
											request3.RecordTime = d2.Date;
											request2.ToTime = result2;
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
											text = string.Format(PersianString.HourlyLeavePerMonthLimit, byID.MaxHoursInAMonth.TotalHours.ToString("0.0"));
										}
									}
									else
									{
										text = PersianString.RequestDailyForDateExists;
									}
								}
								else
								{
									text = text2;
								}
								goto IL_058b;
							}
						}
						text = PersianString.BadTimeFormat;
					}
					else
					{
						text = PersianString.BadDateFormat;
					}
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
			goto IL_058b;
			IL_058b:
			return text;
		}
	}
}
