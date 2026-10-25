using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.LeaveCalcs;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class DailyLeaveController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(string forDate = null)
		{
			DailyExitRequest dailyExitRequest = new DailyExitRequest();
			GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
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
			GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
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
					GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
					GlobalHelper.AddLeaveDescriptions(base.ViewBag);
					DailyExitRequest dailyExitRequest = new DailyExitRequest();
					dailyExitRequest.RequestID = byID.RequestID;
					dailyExitRequest.Descriptions = byID.Descriptions;
					dailyExitRequest.ForDateHijri = DateConverter.ToHijriDate(byID.ForDate);
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
			GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
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
			return AddRequest(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request, new AppExtraDataForRequest());
		}

		public static string AddRequest(long badgeNumber, DailyExitRequest request, AppExtraDataForRequest appExtraData)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			if (request.Descriptions == null)
			{
				request.Descriptions = "";
			}
			string text = "";
			if (request.LeaveDescriptionID == 0)
			{
				return PersianString.BadDescriptions;
			}
			if (request.LeaveID == 0)
			{
				return PersianString.BadExitType;
			}
			DateTime? forDate = DateConverter.ToChristian(request.ForDateHijri);
			DateTime? toDate = DateConverter.ToChristian(request.ToDateHijri);
			if (forDate.HasValue && toDate.HasValue)
			{
				DateTime dateTime = toDate.Value;
				DateTime date = dateTime.Date;
				dateTime = forDate.Value;
				if (date >= dateTime.Date)
				{
					if (t5DBInstance.DailyLeaveDA.GetByID(badgeNumber, forDate.Value) != null)
					{
						return PersianString.DailyLeaveExists;
					}
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
							PersianCalendar pc = new PersianCalendar();
							LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
							int year = pc.GetYear(forDate.Value);
							int month = pc.GetMonth(forDate.Value);
							bool flag = false;
							Attendance attendance = new Attendance(t5DBInstance, personnelSystem);
							DateTime fromDate = pc.ToDateTime(year, 1, 1, 0, 0, 0, 0);
							DateTime toDate2 = pc.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);
							List<ADayResult> list = attendance.Calculate(badgeNumber, fromDate, toDate2);
							List<LeaveType> all = t5DBInstance.LeaveTypeDA.GetAll();
							List<Schedule> all2 = t5DBInstance.ScheduleDA.GetAll();
							List<Paramtr> all3 = t5DBInstance.ParameterDA.GetAll();
							Remain remain = new Remain(list, all, t5DBInstance.LeavesAddedDA.GetAll(new List<long>
							{
								badgeNumber
							}, fromDate, toDate2), t5DBInstance.YearRemainDA.GetAll(badgeNumber), all2, all3);
							PersianCalendar persianCalendar = new PersianCalendar();
							new RemainLeaveController();
							dateTime = persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), 1, 1, 0, 0, 0, 0);
							dateTime.AddDays(-1.0);
							List<ADayRemain> source2 = remain.CalculateRemain(personnelSystem.DB.UserInfos.GetByID(badgeNumber), byID.LeaveTypeID, year);
							ADayRemain aDayRemain = (from rc in source2
							where rc.ForDate == forDate.Value
							select rc).FirstOrDefault();
							if (aDayRemain != null && aDayRemain.Remain > 0.0 - byID.MaxMinus)
							{
								flag = true;
							}
							bool flag2 = false;
							double num = 0.0;
							int num2 = 0;
							DateTime dateTime2 = forDate.Value;
							while (dateTime2 <= toDate.Value)
							{
								if (HomeController.CalculateDailyAbsence(badgeNumber, dateTime2, byID.AutoInHolidays))
								{
									num2++;
								}
								dateTime2 = dateTime2.AddDays(1.0);
							}
							int i;
							for (i = 1; i < 13; i++)
							{
								double num3 = 0.0;
								foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(badgeNumber, pc.ToDateTime(year, i, 1, 0, 0, 0, 0), pc.ToDateTime(year, i, pc.GetDaysInMonth(year, i), 0, 0, 0, 0)))
								{
									new MyTime3.Models.Request().RequestT5 = item;
									if (item.IsSuspend && item.WorkflowType == WorkflowOperations.Leave)
									{
										if (item.LeaveID == item.LeaveID)
										{
											num3 += 1.0;
										}
										LeaveType leaveType = (from lt2 in all
										where lt2.LeaveTypeID == item.LeaveID
										select lt2).FirstOrDefault();
										if (leaveType != null)
										{
											if (!item.IsDaily)
											{
												TimeSpan timeSpan;
												if (leaveType.DecreaseFromLeaveID.HasValue && leaveType.DecreaseFromLeaveID.Value == item.LeaveID)
												{
													double num4 = num3;
													timeSpan = item.ToTime - item.FromTime;
													double totalHours = timeSpan.TotalHours;
													timeSpan = byID.EachXHoursADay;
													num3 = num4 + totalHours / timeSpan.TotalHours;
												}
												if (leaveType.LeaveTypeID == item.LeaveID)
												{
													double num5 = num3;
													timeSpan = item.ToTime - item.FromTime;
													double totalHours2 = timeSpan.TotalHours;
													timeSpan = byID.EachXHoursADay;
													num3 = num5 + totalHours2 / timeSpan.TotalHours;
												}
											}
											else if (leaveType.DecreaseFromLeaveID.HasValue && leaveType.DecreaseFromLeaveID.Value == byID.LeaveTypeID)
											{
												num3 += 1.0;
											}
										}
									}
								}
								TimeSpan t = new TimeSpan(0L);
								int daysCount = pc.GetDaysInMonth(year, i) + 1;
								int j;
								for (j = 1; j < daysCount; j++)
								{
									ADayResult aDayResult = (from ya in list
									where ya.ForDate == pc.ToDateTime(year, i, j, 0, 0, 0, 0)
									select ya).First();
									if (aDayResult.IsDailyLeave && ((!aDayResult.IsHoliday && aDayResult.CurrentShift.ActiveSchedule != null) || byID.GetInHolidays))
									{
										string symbol = aDayResult.DailyLeaveValue.DailyLeaveType.Symbol;
										if (aDayResult.DailyLeaveValue.LeaveTypeID == byID.LeaveTypeID)
										{
											num3 += 1.0;
										}
									}
									if (aDayResult.ProcessedSchedule != null)
									{
										foreach (T5V3.Attendance.InShiftExit inShiftExit in aDayResult.ProcessedSchedule.InShiftExits)
										{
											if (inShiftExit.lvType != null)
											{
												if (inShiftExit.lvType.DecreaseFromLeaveID.HasValue && inShiftExit.lvType.DecreaseFromLeaveID.Value == byID.LeaveTypeID)
												{
													t += inShiftExit.ValidLen;
												}
												if (inShiftExit.lvType.LeaveTypeID == byID.LeaveTypeID)
												{
													t += inShiftExit.ValidLen;
												}
											}
										}
									}
								}
								num += num3;
								if (i == month && (byID.GetInType == LeaveGetInType.MonthFirst || byID.GetInType == LeaveGetInType.MonthLast))
								{
									double num6 = source2.FirstOrDefault((ADayRemain rc) => rc.ForDate == pc.ToDateTime(year, i, daysCount - 1, 0, 0, 0, 0)).Remain;
									if (byID.GetInType == LeaveGetInType.MonthLast)
									{
										num6 -= byID.MaxDailyInMonth;
									}
									if (num6 + byID.MaxMinus - (double)num2 < 0.0)
									{
										try
										{
											flag2 = true;
										}
										catch
										{
										}
									}
								}
							}
							double num7 = 0.0;
							dateTime = toDate.Value;
							int dayOfYear = dateTime.DayOfYear;
							dateTime = DateTime.Parse("2020-03-21");
							bool num8 = dayOfYear < dateTime.DayOfYear;
							dateTime = DateTime.Now;
							int dayOfYear2 = dateTime.DayOfYear;
							dateTime = DateTime.Parse("2020-03-21");
							bool num9 = dayOfYear2 > dateTime.DayOfYear;
							dateTime = forDate.Value;
							int dayOfYear3 = dateTime.DayOfYear;
							dateTime = DateTime.Parse("2020-03-21");
							num7 = ((!(num8 & (num9 & (dayOfYear3 < dateTime.DayOfYear)))) ? source2.FirstOrDefault(delegate(ADayRemain rc)
							{
								DateTime dateTime4 = rc.ForDate;
								DateTime date3 = dateTime4.Date;
								dateTime4 = DateTime.Now;
								return date3 == dateTime4.Date;
							}).Remain : source2.FirstOrDefault(delegate(ADayRemain rc)
							{
								DateTime dateTime3 = rc.ForDate;
								DateTime date2 = dateTime3.Date;
								dateTime3 = forDate.Value;
								return date2 == dateTime3.Date;
							}).Remain);
							if (num7 + byID.MaxMinus - (double)num2 < 0.0)
							{
								try
								{
									flag2 = true;
								}
								catch
								{
								}
							}
							if (!flag2)
							{
								if (flag)
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
									request2.WorkflowType = WorkflowOperations.Leave;
									request2.Descriptions = request.Descriptions;
									try
									{
										request2.Longitude = appExtraData.longitude;
										request2.Latitude = appExtraData.latitude;
										request2.ImageData = appExtraData.ImageData;
									}
									catch
									{
									}
									if (t5DBInstance.RequestDA.AddNew(request2, true, out string _, out long newID))
									{
										request2.RequestID = newID;
										NotificationManager.NewRequestAdded(request2);
										return PersianString.InsertSuccess;
									}
									return PersianString.ErrorInInsertingData;
								}
								return PersianString.RemainLeaveError;
							}
							return PersianString.DailyLeaveLimit;
						}
						return text2;
					}
					return PersianString.RequestDailyForDateExists;
				}
			}
			return PersianString.BadDateFormat;
		}

		private static double GetRemainD(long badge, LeaveType lt)
		{
			RemainLeaveController remainLeaveController = new RemainLeaveController();
			PersianCalendar persianCalendar = new PersianCalendar();
			DateTime dateTime = persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), persianCalendar.GetMonth(DateTime.Now), persianCalendar.GetDayOfMonth(DateTime.Now), 0, 0, 0, 0);
			return remainLeaveController.GetRemain(badge, lt.LeaveTypeID, dateTime.Date).Remain;
		}
	}
}
