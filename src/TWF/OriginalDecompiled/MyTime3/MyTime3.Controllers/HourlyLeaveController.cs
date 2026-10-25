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
using TD5.Controllers;
using TD5.Model;

namespace MyTime3.Controllers
{
	public class HourlyLeaveController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(string forDate = null, string fromTime = null, string toTime = null)
		{
			GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
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
			string text = "";
			if (request.LeaveDescriptionID != 0)
			{
				if (request.LeaveID != 0)
				{
					DateTime? nullable = DateConverter.ToChristian(request.ForDateHijri);
					if (nullable.HasValue)
					{
						DateTime result = default(DateTime);
						DateTime result2 = default(DateTime);
						DateTime.TryParse(request.FromTimeStr, out result);
						DateTime.TryParse(request.ToTimeStr, out result2);
						LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
						DateTime d = result;
						DateTime d2 = default(DateTime);
						if (d != d2)
						{
							DateTime d3 = result2;
							d2 = default(DateTime);
							if (d3 != d2 && result < result2)
							{
								if (result2 - result < t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID).MaxHoursInADay)
								{
									if (t5DBInstance.DailyLeaveDA.GetByID(badgeNumber, nullable.Value) == null)
									{
										bool flag = false;
										PersianCalendar persianCalendar = new PersianCalendar();
										int year = persianCalendar.GetYear(nullable.Value);
										int month = persianCalendar.GetMonth(nullable.Value);
										persianCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
										persianCalendar.ToDateTime(year, month, persianCalendar.GetDaysInMonth(year, month), 23, 59, 0, 0);
										DateTime dateTime = persianCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
										DateTime dateTime2 = (month >= 12) ? persianCalendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0) : persianCalendar.ToDateTime(year, month + 1, 1, 0, 0, 0, 0);
										TimeSpan timeSpan = default(TimeSpan);
										foreach (TD5.Model.Request item in TD5.Controllers.RequestController.GetRequest("*").BadgeNumber(badgeNumber).FromTime(dateTime)
											.ToTime(dateTime2)
											.LeaveId(request.LeaveID)
											.IsSuspend(false)
											.WorkflowType(1)
											.Tolist())
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
										bool num = (bool)SettingsController.LoadValue("ValidHourlyFree", typeof(bool), true);
										int num2 = (int)SettingsController.LoadValue("HoursOfDays", typeof(int), 8);
										if (num)
										{
											IEnumerable<T5V3.Model.Request> source = t5DBInstance.RequestDA.GetAll(badgeNumber, dateTime, dateTime2).Where(delegate(T5V3.Model.Request r)
											{
												if (r.IsDaily)
												{
													return r.WorkflowType == WorkflowOperations.Leave;
												}
												return false;
											});
											timeSpan += new TimeSpan(0, source.Count() * num2, 0, 0);
										}
										timeSpan += result2 - result;
										if (timeSpan > byID.MaxHoursInAMonth)
										{
											flag = true;
										}
										T5V3.Model.Request byID2 = t5DBInstance.RequestDA.GetByID(request.RequestID);
										if (!flag)
										{
											if (byID2 != null)
											{
												byID2.LeaveID = request.LeaveID;
												byID2.LeaveDescriptionID = request.LeaveDescriptionID;
												byID2.ForDate = nullable.Value;
												byID2.Descriptions = request.Descriptions;
												byID2.FromTime = result;
												byID2.ToTime = result2;
												if (t5DBInstance.RequestDA.Edit(byID2, out string _))
												{
													text = PersianString.InsertSuccess;
													NotificationManager.NewRequestAdded(byID2);
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
											text = string.Format(PersianString.HourlyLeavePerMonthLimit, byID.MaxHoursInAMonth.TotalHours.ToString("0.0"));
										}
									}
									else
									{
										text = PersianString.DailyLeaveExists;
									}
								}
								else
								{
									text = PersianString.MaxHourlyInDayLimit;
								}
								goto IL_040a;
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
			goto IL_040a;
			IL_040a:
			return text;
		}

		private static bool CheckDailyLeave()
		{
			DailyExitRequest request = new DailyExitRequest
			{
				LeaveDescriptionID = 1,
				LeaveID = 1
			};
			return new HourlyLeaveController().RequestDailyTest(request);
		}

		private bool RequestDailyTest(DailyExitRequest request)
		{
			return AddRequestTest(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request, new AppExtraDataForRequest());
		}

		public static bool AddRequestTest(long badgeNumber, DailyExitRequest request, AppExtraDataForRequest appExtraData)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			if (request.Descriptions == null)
			{
				request.Descriptions = "";
			}
			bool result = false;
			if (request.LeaveDescriptionID != 0)
			{
				if (request.LeaveID != 0)
				{
					DateTime? forDate = DateTime.Now;
					DateTime? nullable = DateTime.Now;
					if (forDate.HasValue && nullable.HasValue)
					{
						DateTime value = nullable.Value;
						DateTime date = value.Date;
						value = forDate.Value;
						if (date >= value.Date)
						{
							if (t5DBInstance.DailyLeaveDA.GetByID(badgeNumber, forDate.Value) == null)
							{
								if (EditRequestController.CheckRequestTime(forDate.Value) == "")
								{
									PersianCalendar pc = new PersianCalendar();
									LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
									int year = pc.GetYear(forDate.Value);
									int month = pc.GetMonth(forDate.Value);
									bool flag = false;
									Attendance attendance = new Attendance(t5DBInstance, personnelSystem);
									DateTime fromDate = pc.ToDateTime(year, 1, 1, 0, 0, 0, 0);
									DateTime toDate = pc.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);
									List<ADayResult> list = attendance.Calculate(badgeNumber, fromDate, toDate);
									List<LeaveType> all = t5DBInstance.LeaveTypeDA.GetAll();
									List<Schedule> all2 = t5DBInstance.ScheduleDA.GetAll();
									List<Paramtr> all3 = t5DBInstance.ParameterDA.GetAll();
									ADayRemain aDayRemain = (from rc in new Remain(list, all, t5DBInstance.LeavesAddedDA.GetAll(new List<long>
									{
										badgeNumber
									}, fromDate, toDate), t5DBInstance.YearRemainDA.GetAll(badgeNumber), all2, all3).CalculateRemain(personnelSystem.DB.UserInfos.GetByID(badgeNumber), byID.LeaveTypeID, year)
									where rc.ForDate == forDate.Value
									select rc).FirstOrDefault();
									if (aDayRemain != null && aDayRemain.Remain > 0.0 - byID.MaxMinus)
									{
										flag = true;
									}
									bool flag2 = false;
									double num = 0.0;
									int i;
									TimeSpan timeSpan;
									for (i = 1; i < 13; i++)
									{
										double num2 = 0.0;
										foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(badgeNumber, pc.ToDateTime(year, i, 1, 0, 0, 0, 0), pc.ToDateTime(year, i, pc.GetDaysInMonth(year, i), 0, 0, 0, 0)))
										{
											new MyTime3.Models.Request().RequestT5 = item;
											if (item.IsSuspend && item.WorkflowType == WorkflowOperations.Leave)
											{
												if (item.LeaveID == request.LeaveID)
												{
													num2 += 1.0;
												}
												LeaveType leaveType = (from lt2 in all
												where lt2.LeaveTypeID == item.LeaveID
												select lt2).FirstOrDefault();
												if (leaveType != null)
												{
													if (!item.IsDaily)
													{
														if (leaveType.DecreaseFromLeaveID.HasValue && leaveType.DecreaseFromLeaveID.Value == request.LeaveID)
														{
															double num3 = num2;
															timeSpan = item.ToTime - item.FromTime;
															double totalHours = timeSpan.TotalHours;
															timeSpan = byID.EachXHoursADay;
															num2 = num3 + totalHours / timeSpan.TotalHours;
														}
														if (leaveType.LeaveTypeID == request.LeaveID)
														{
															double num4 = num2;
															timeSpan = item.ToTime - item.FromTime;
															double totalHours2 = timeSpan.TotalHours;
															timeSpan = byID.EachXHoursADay;
															num2 = num4 + totalHours2 / timeSpan.TotalHours;
														}
													}
													else if (leaveType.DecreaseFromLeaveID.HasValue && leaveType.DecreaseFromLeaveID.Value == byID.LeaveTypeID)
													{
														num2 += 1.0;
													}
												}
											}
										}
										TimeSpan t = new TimeSpan(0L);
										int num5 = pc.GetDaysInMonth(year, i) + 1;
										int j;
										for (j = 1; j < num5; j++)
										{
											ADayResult aDayResult = (from ya in list
											where ya.ForDate == pc.ToDateTime(year, i, j, 0, 0, 0, 0)
											select ya).First();
											if (aDayResult.IsDailyLeave && ((!aDayResult.IsHoliday && aDayResult.CurrentShift.ActiveSchedule != null) || byID.GetInHolidays))
											{
												string symbol = aDayResult.DailyLeaveValue.DailyLeaveType.Symbol;
												if (aDayResult.DailyLeaveValue.LeaveTypeID == byID.LeaveTypeID)
												{
													num2 += 1.0;
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
										num += num2;
										if (i == month)
										{
											double num6 = num2;
											value = nullable.Value;
											DateTime date2 = value.Date;
											value = forDate.Value;
											timeSpan = date2 - value.Date;
											if (num6 + (timeSpan.TotalDays + 1.0) > byID.MaxDailyInMonth)
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
									double num7 = num;
									value = nullable.Value;
									DateTime date3 = value.Date;
									value = forDate.Value;
									timeSpan = date3 - value.Date;
									if (num7 + (timeSpan.TotalDays + 1.0) > byID.MaxDailyInYear)
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
											return true;
										}
										return result;
									}
									return result;
								}
								return result;
							}
							return result;
						}
					}
					return result;
				}
				return result;
			}
			return result;
		}

		private string RequestHourly(HourlyExitRequest request)
		{
			return AddRequest(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
		}

		public static string AddRequest(long badgeNumber, HourlyExitRequest request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			string text = "";
			if (request.Descriptions == null)
			{
				request.Descriptions = "";
			}
			if (request.LeaveDescriptionID != 0)
			{
				if (request.LeaveID != 0)
				{
					LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
					DateTime? forDate = DateConverter.ToChristian(request.ForDateHijri);
					if (forDate.HasValue)
					{
						DateTime result = default(DateTime);
						DateTime result2 = default(DateTime);
						DateTime.TryParse(request.FromTimeStr, out result);
						DateTime.TryParse(request.ToTimeStr, out result2);
						if (result2 < result)
						{
							result2 = result2.AddDays(1.0);
						}
						DateTime d = result;
						DateTime d2 = default(DateTime);
						if (d != d2)
						{
							DateTime d3 = result2;
							d2 = default(DateTime);
							if (d3 != d2)
							{
								TimeSpan timeSpan2;
								if (result2 - result < byID.MaxHoursInADay)
								{
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
									if (result2.TimeOfDay < result.TimeOfDay)
									{
										d2 = forDate.Value;
										d2 = d2.Date;
										DateTime fromTime2 = d2.Add(result.TimeOfDay);
										d2 = forDate.Value;
										d2 = d2.Date;
										d2 = d2.Add(result2.TimeOfDay);
										i = new TimeInterval(fromTime2, d2.AddDays(1.0));
									}
									foreach (T5V3.Model.Request item in list)
									{
										d2 = item.ForDate;
										DateTime dateTime = item.FromTime;
										DateTime fromTime3 = d2.Add(dateTime.TimeOfDay);
										d2 = item.ForDate;
										dateTime = item.ToTime;
										TimeInterval i2 = new TimeInterval(fromTime3, d2.Add(dateTime.TimeOfDay));
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
											if (byID.DecreaseFromLeaveID.HasValue)
											{
												PersianCalendar persianCalendar = new PersianCalendar();
												LeaveType byID2 = t5DBInstance.LeaveTypeDA.GetByID(byID.DecreaseFromLeaveID.Value);
												int year = persianCalendar.GetYear(forDate.Value);
												persianCalendar.GetMonth(forDate.Value);
												Attendance attendance = new Attendance(t5DBInstance, personnelSystem);
												DateTime fromDate = persianCalendar.ToDateTime(year, 1, 1, 0, 0, 0, 0);
												DateTime toDate = persianCalendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);
												ADayRemain aDayRemain = (from rc in new Remain(attendance.Calculate(badgeNumber, fromDate, toDate), t5DBInstance.LeaveTypeDA.GetAll(), schedules: t5DBInstance.ScheduleDA.GetAll(), userParameters: t5DBInstance.ParameterDA.GetAll(), leavesAdded: t5DBInstance.LeavesAddedDA.GetAll(new List<long>
												{
													badgeNumber
												}, fromDate, toDate), yearLeaveRemain: t5DBInstance.YearRemainDA.GetAll(badgeNumber)).CalculateRemain(personnelSystem.DB.UserInfos.GetByID(badgeNumber), byID2.LeaveTypeID, year)
												where rc.ForDate == forDate.Value
												select rc).FirstOrDefault();
												if (aDayRemain != null && aDayRemain.Remain < 0.0 - byID2.MaxMinus)
												{
													flag2 = true;
												}
											}
											bool flag3 = false;
											PersianCalendar persianCalendar2 = new PersianCalendar();
											int year2 = persianCalendar2.GetYear(forDate.Value);
											int month = persianCalendar2.GetMonth(forDate.Value);
											persianCalendar2.ToDateTime(year2, month, 1, 0, 0, 0, 0);
											persianCalendar2.ToDateTime(year2, month, persianCalendar2.GetDaysInMonth(year2, month), 23, 59, 0, 0);
											DateTime dateTime2 = persianCalendar2.ToDateTime(year2, month, 1, 0, 0, 0, 0);
											DateTime dateTime3 = (month >= 12) ? persianCalendar2.ToDateTime(year2 + 1, 1, 1, 0, 0, 0, 0) : persianCalendar2.ToDateTime(year2, month + 1, 1, 0, 0, 0, 0);
											TimeSpan timeSpan = default(TimeSpan);
											foreach (TD5.Model.Request item2 in TD5.Controllers.RequestController.GetRequest("*").BadgeNumber(badgeNumber).FromTime(dateTime2)
												.ToTime(dateTime3)
												.LeaveId(request.LeaveID)
												.IsSuspend(false)
												.WorkflowType(1)
												.Tolist())
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
											bool num = (bool)SettingsController.LoadValue("ValidHourlyFree", typeof(bool), true);
											int num2 = (int)SettingsController.LoadValue("HoursOfDays", typeof(int), 8);
											if (num)
											{
												IEnumerable<T5V3.Model.Request> source = t5DBInstance.RequestDA.GetAll(badgeNumber, dateTime2, dateTime3).Where(delegate(T5V3.Model.Request r)
												{
													if (r.IsDaily)
													{
														return r.WorkflowType == WorkflowOperations.Leave;
													}
													return false;
												});
												timeSpan += new TimeSpan(0, source.Count() * num2, 0, 0);
											}
											timeSpan += result2 - result;
											if (timeSpan > byID.MaxHoursInAMonth)
											{
												flag3 = true;
											}
											if (!flag3)
											{
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
													request2.WorkflowType = WorkflowOperations.Leave;
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
													text = PersianString.RemainLeaveError;
												}
											}
											else
											{
												string hourlyLeavePerMonthLimit = PersianString.HourlyLeavePerMonthLimit;
												timeSpan2 = byID.MaxHoursInAMonth;
												text = string.Format(hourlyLeavePerMonthLimit, timeSpan2.TotalHours.ToString("0.0"));
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
								}
								else
								{
									string maxHourlyInDayLimit = PersianString.MaxHourlyInDayLimit;
									timeSpan2 = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID).MaxHoursInADay;
									text = string.Format(maxHourlyInDayLimit, timeSpan2.ToString());
								}
								goto IL_07a8;
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
			goto IL_07a8;
			IL_07a8:
			return text;
		}
	}
}
