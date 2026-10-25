using MyTime3.Authorization;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class RequestController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult New(GeneralRequest request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<int> validIDs = t5DBInstance.EmpLeavesDA.GetAll(GlobalHelper.GetLoggedInUserInfo(base.HttpContext).EmpTypeID);
			base.ViewBag.LeaveTypes = (from l in t5DBInstance.LeaveTypeDA.GetAll()
			where validIDs.Contains(l.LeaveTypeID)
			select l).ToList();
			base.ViewBag.LeaveDescriptions = t5DBInstance.LeaveDescriptionDA.GetAll();
			base.ViewBag.Personnel = AddPersonnelToViewBag(base.HttpContext);
			return View(request);
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult FromUser(long badge = -1L)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<int> validIDs = t5DBInstance.EmpLeavesDA.GetAll(GlobalHelper.GetLoggedInUserInfo(base.HttpContext).EmpTypeID);
			base.ViewBag.LeaveTypes = (from l in t5DBInstance.LeaveTypeDA.GetAll()
			where validIDs.Contains(l.LeaveTypeID)
			select l).ToList();
			base.ViewBag.LeaveDescriptions = t5DBInstance.LeaveDescriptionDA.GetAll();
			base.ViewBag.Personnel = AddPersonnelToViewBag(base.HttpContext);
			if (badge > 0)
			{
				dynamic viewBag = base.ViewBag;
				DateTime dateTime = DateTime.Now;
				dateTime = dateTime.Date;
				DateTime fromDate = dateTime.AddDays(-40.0);
				dateTime = DateTime.Now;
				viewBag.Rep = HomeController.CalculateDailyDetailsReport(badge, fromDate, dateTime.Date);
			}
			List<PersonnelModel> list = AddPersonnelToViewBag(base.HttpContext);
			if (list.Count > 0)
			{
				list.RemoveAt(0);
				base.ViewBag.Personnel = list;
			}
			return View(badge);
		}

		[HttpPost]
		public JsonResult SendPosition(GeneralRequest request)
		{
			string text = AddLocationRequest(request);
			return Json(new
			{
				Message = text,
				OK = (text == PersianString.InsertSuccess)
			});
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Edit(GeneralRequest request)
		{
			request.Message = AddRequest(request, base.HttpContext, false);
			return RedirectToAction("New", (object)request);
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public JsonResult GeneralRequestWeb(GeneralRequest request)
		{
			string message;
			try
			{
				if (request.LeaveDescriptionsID < 0)
				{
					T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
					if (request.RequestType == 0 || request.RequestType == 1)
					{
						request.LeaveDescriptionsID = t5DBInstance.LeaveDescriptionDA.GetAll().FirstOrDefault((LeaveDescription rr) => rr.ForLeaveType == LeaveTypeEnum.Leave).ID;
					}
					else
					{
						request.LeaveDescriptionsID = t5DBInstance.LeaveDescriptionDA.GetAll().FirstOrDefault((LeaveDescription rr) => rr.ForLeaveType == LeaveTypeEnum.Duty).ID;
					}
				}
				message = AddRequest(request, base.HttpContext, false);
			}
			catch (Exception ex)
			{
				message = ex.Message;
			}
			return Json(new
			{
				Message = message
			});
		}

		[HttpPost]
		public JsonResult GeneralRequest(GeneralRequest request)
		{
			if (!GlobalHelper.WithTimeApp())
			{
				return null;
			}
			string message;
			try
			{
				message = AddRequest(request, base.HttpContext, true);
			}
			catch (Exception ex)
			{
				message = ex.Message;
			}
			return Json(new
			{
				Message = message
			});
		}

		private static string AddLocationRequest(GeneralRequest request)
		{
			string result = "";
			long num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(request.UserName, request.Password, true);
			if (num > 0)
			{
				UserPositionStep step = new UserPositionStep();
				step.BadgeNumber = num;
				step.Descriptions = request.Descriptions;
				step.ImageData = request.GetAppExtraData().ImageData;
				step.Latitude = request.Latitude;
				step.Longitude = request.Longitude;
				step.PositionID = request.RequestType;
				step.StepIndex = request.LeaveTypeID;
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				PositionStep positionStep = (from s in (from p in t5DBInstance.PositionDA.GetAll()
				where p.PositionID == step.PositionID
				select p).First().Steps
				where s.OrderIndex == step.StepIndex
				select s).First();
				result = ((positionStep.Descriptions && (request.Descriptions == null || request.Descriptions == string.Empty)) ? PersianString.DescriptionsRequired : ((positionStep.Photo && (request.ImageData == null || request.ImageData == string.Empty || request.ImageData.Length < 2)) ? PersianString.PhotoRequired : ((positionStep.Location && (request.Latitude == 0.0 || request.Longitude == 0.0)) ? PersianString.LocationRequired : ((!t5DBInstance.UserPositionDataDA.InsertNew(step)) ? PersianString.ErrorInInsertingData : PersianString.InsertSuccess))));
			}
			return result;
		}

		private static string AddRequest(GeneralRequest request, HttpContextBase httpContext, bool fromApp)
		{
			string result = "";
			long result2 = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(request.UserName, request.Password, true);
			if (result2 < 0 && long.TryParse(LoginManager.GetBadgeNumber(httpContext), out result2) && request.FromUser.HasValue && (from p in AddPersonnelToViewBag(httpContext)
			where p.BadgeNumber == request.FromUser.Value
			select p).Count() > 0)
			{
				result2 = request.FromUser.Value;
			}
			if (result2 > 0)
			{
				Rule rule = GlobalHelper.GetRule(result2, DateTime.Now);
				if (rule != null && rule.AppRules != null)
				{
					AppRuleModel appRuleModel = (from a in rule.AppRules
					where a.WorkFlow == request.RequestType
					select a).FirstOrDefault();
					if (appRuleModel != null)
					{
						if (fromApp)
						{
							if (!appRuleModel.App)
							{
								return PersianString.RulesNotPermitApp;
							}
							if (appRuleModel.Photo && (request.ImageData == null || request.ImageData.Length < 10))
							{
								return PersianString.HaveToPhoto;
							}
							if (appRuleModel.Location && request.Latitude == 0.0 && request.Longitude == 0.0)
							{
								return PersianString.HaveToLocation;
							}
						}
						else if (!appRuleModel.Web)
						{
							return PersianString.RulesNotPermitWeb;
						}
					}
				}
				if (request.IsNew)
				{
					if (request.RequestType == 0)
					{
						DailyExitRequest dailyExitRequest = new DailyExitRequest();
						dailyExitRequest.Descriptions = request.Descriptions;
						dailyExitRequest.ForDateHijri = request.FromDateHijri;
						dailyExitRequest.LeaveDescriptionID = request.LeaveDescriptionsID;
						dailyExitRequest.LeaveID = request.LeaveTypeID;
						dailyExitRequest.ToDateHijri = request.ToDateHijri;
						result = DailyLeaveController.AddRequest(result2, dailyExitRequest, request.GetAppExtraData());
					}
					if (request.RequestType == 1)
					{
						HourlyExitRequest hourlyExitRequest = new HourlyExitRequest();
						hourlyExitRequest.Descriptions = request.Descriptions;
						hourlyExitRequest.ForDateHijri = request.ForDateHijri;
						hourlyExitRequest.LeaveDescriptionID = request.LeaveDescriptionsID;
						hourlyExitRequest.LeaveID = request.LeaveTypeID;
						hourlyExitRequest.FromTimeStr = request.FromTime;
						hourlyExitRequest.ToTimeStr = request.ToTime;
						result = HourlyLeaveController.AddRequest(result2, hourlyExitRequest);
					}
					if (request.RequestType == 2)
					{
						DailyExitRequest dailyExitRequest2 = new DailyExitRequest();
						dailyExitRequest2.Descriptions = request.Descriptions;
						dailyExitRequest2.ForDateHijri = request.FromDateHijri;
						dailyExitRequest2.LeaveDescriptionID = request.LeaveDescriptionsID;
						dailyExitRequest2.LeaveID = request.LeaveTypeID;
						dailyExitRequest2.ToDateHijri = request.ToDateHijri;
						result = DailyDutyController.AddRequest(result2, dailyExitRequest2);
					}
					if (request.RequestType == 3)
					{
						HourlyExitRequest hourlyExitRequest2 = new HourlyExitRequest();
						hourlyExitRequest2.Descriptions = request.Descriptions;
						hourlyExitRequest2.ForDateHijri = request.ForDateHijri;
						hourlyExitRequest2.LeaveDescriptionID = request.LeaveDescriptionsID;
						hourlyExitRequest2.LeaveID = request.LeaveTypeID;
						hourlyExitRequest2.FromTimeStr = request.FromTime;
						hourlyExitRequest2.ToTimeStr = request.ToTime;
						result = HourlyDutyController.AddRequest(result2, hourlyExitRequest2);
					}
					else if (request.RequestType == 4)
					{
						result = RecordController.RequestForRecord(new RecordRequest
						{
							Descriptions = request.Descriptions,
							ForDateStr = request.ForDateHijri,
							RecordTimeStr = request.ForTime
						}, result2);
					}
					else if (request.RequestType == 5)
					{
						LateEarlyRequest lateEarlyRequest = new LateEarlyRequest();
						lateEarlyRequest.Descriptions = request.Descriptions;
						lateEarlyRequest.ForDateStr = request.ForDateHijri;
						result = LateController.RequestLate(result2, lateEarlyRequest);
					}
					else if (request.RequestType == 6)
					{
						LateEarlyRequest lateEarlyRequest2 = new LateEarlyRequest();
						lateEarlyRequest2.Descriptions = request.Descriptions;
						lateEarlyRequest2.ForDateStr = request.ForDateHijri;
						result = EarlyController.RequestEarly(result2, lateEarlyRequest2);
					}
					else if (request.RequestType == 7)
					{
						OverTimeRequest overTimeRequest = new OverTimeRequest();
						int overTimeType = 0;
						if (request.LeaveTypeID == -1)
						{
							overTimeType = 1;
						}
						if (request.LeaveTypeID == -3)
						{
							overTimeType = 2;
						}
						overTimeRequest.Descriptions = request.Descriptions;
						overTimeRequest.ForDateHijri = request.ForDateHijri;
						overTimeRequest.ValidValue = request.OvertimeValue;
						overTimeRequest.OverTimeType = overTimeType;
						result = OverTimeController.RequestOverTime(result2, overTimeRequest);
					}
				}
				else if (DBInitializer.GetT5DBInstance().RequestDA.GetByID(request.RequestID) != null)
				{
					if (request.RequestType == 0)
					{
						DailyExitRequest dailyExitRequest3 = new DailyExitRequest();
						dailyExitRequest3.RequestID = request.RequestID;
						dailyExitRequest3.Descriptions = request.Descriptions;
						dailyExitRequest3.ForDateHijri = request.FromDateHijri;
						dailyExitRequest3.LeaveDescriptionID = request.LeaveDescriptionsID;
						dailyExitRequest3.LeaveID = request.LeaveTypeID;
						dailyExitRequest3.ToDateHijri = request.ToDateHijri;
						result = DailyLeaveController.EditRequestDaily(result2, dailyExitRequest3);
					}
					if (request.RequestType == 1)
					{
						HourlyExitRequest hourlyExitRequest3 = new HourlyExitRequest();
						hourlyExitRequest3.RequestID = request.RequestID;
						hourlyExitRequest3.Descriptions = request.Descriptions;
						hourlyExitRequest3.ForDateHijri = request.ForDateHijri;
						hourlyExitRequest3.LeaveDescriptionID = request.LeaveDescriptionsID;
						hourlyExitRequest3.LeaveID = request.LeaveTypeID;
						hourlyExitRequest3.FromTimeStr = request.FromTime;
						hourlyExitRequest3.ToTimeStr = request.ToTime;
						result = HourlyLeaveController.EditRequestHourly(result2, hourlyExitRequest3);
					}
					if (request.RequestType == 2)
					{
						DailyExitRequest dailyExitRequest4 = new DailyExitRequest();
						dailyExitRequest4.RequestID = request.RequestID;
						dailyExitRequest4.Descriptions = request.Descriptions;
						dailyExitRequest4.ForDateHijri = request.FromDateHijri;
						dailyExitRequest4.LeaveDescriptionID = request.LeaveDescriptionsID;
						dailyExitRequest4.LeaveID = request.LeaveTypeID;
						dailyExitRequest4.ToDateHijri = request.ToDateHijri;
						result = DailyDutyController.EditRequestDaily(result2, dailyExitRequest4);
					}
					if (request.RequestType == 3)
					{
						HourlyExitRequest hourlyExitRequest4 = new HourlyExitRequest();
						hourlyExitRequest4.RequestID = request.RequestID;
						hourlyExitRequest4.Descriptions = request.Descriptions;
						hourlyExitRequest4.ForDateHijri = request.ForDateHijri;
						hourlyExitRequest4.LeaveDescriptionID = request.LeaveDescriptionsID;
						hourlyExitRequest4.LeaveID = request.LeaveTypeID;
						hourlyExitRequest4.FromTimeStr = request.FromTime;
						hourlyExitRequest4.ToTimeStr = request.ToTime;
						result = HourlyDutyController.EditRequestHourly(result2, hourlyExitRequest4);
					}
					else if (request.RequestType == 4)
					{
						RecordRequest recordRequest = new RecordRequest();
						recordRequest.RequestID = request.RequestID;
						recordRequest.Descriptions = request.Descriptions;
						recordRequest.ForDateStr = request.ForDateHijri;
						recordRequest.RecordTimeStr = request.ForTime;
						result = RecordController.EditRequestRecord(result2, recordRequest);
					}
					else if (request.RequestType == 5)
					{
						result = LateController.EditRequestLate(new LateEarlyRequest
						{
							RequestID = request.RequestID,
							Descriptions = request.Descriptions,
							ForDateStr = request.ForDateHijri
						});
					}
					else if (request.RequestType == 6)
					{
						result = EarlyController.EditRequestEarly(new LateEarlyRequest
						{
							RequestID = request.RequestID,
							Descriptions = request.Descriptions,
							ForDateStr = request.ForDateHijri
						});
					}
					else if (request.RequestType == 7)
					{
						OverTimeRequest overTimeRequest2 = new OverTimeRequest();
						int overTimeType2 = 0;
						if (request.LeaveTypeID == -1)
						{
							overTimeType2 = 1;
						}
						if (request.LeaveTypeID == -3)
						{
							overTimeType2 = 2;
						}
						overTimeRequest2.Descriptions = request.Descriptions;
						overTimeRequest2.ForDateHijri = request.ForDateHijri;
						overTimeRequest2.ValidValue = request.OvertimeValue;
						overTimeRequest2.OverTimeType = overTimeType2;
						result = OverTimeController.RequestOverTime(result2, overTimeRequest2);
					}
				}
				else
				{
					result = PersianString.ErrorInInsertingData;
				}
			}
			return result;
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public FileResult ShowPhoto(int id)
		{
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), id);
			int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(loggedInBadgeNumber);
			List<long> list = t5DBInstance.PersonnelDeputyDA.IsDeputy(loggedInBadgeNumber);
			List<int> list2 = new List<int>();
			foreach (long item in list)
			{
				int userCurrentTitleID2 = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
				if (userCurrentTitleID2 != -1)
				{
					list2.Add(userCurrentTitleID2);
				}
			}
			if ((userCurrentTitleID != -1 || list2.Count > 0) && requestAccepters.Count == 1)
			{
				KeyValuePair<long, int> keyValuePair = requestAccepters[0];
				if (keyValuePair.Value != userCurrentTitleID)
				{
					List<int> list3 = list2;
					keyValuePair = requestAccepters[0];
					if (!list3.Contains(keyValuePair.Value))
					{
						goto IL_011c;
					}
				}
				return File(t5DBInstance.RequestDA.GetByID(id).ImageData, "image/png");
			}
			goto IL_011c;
			IL_011c:
			return null;
		}

		public static List<PersonnelModel> AddPersonnelToViewBag(HttpContextBase httpContext)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			List<PersonnelModel> list = new List<PersonnelModel>();
			int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(httpContext);
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
			foreach (long item in t5DBInstance.PersonnelDeputyDA.IsDeputy(GlobalHelper.GetLoggedInBadgeNumber(httpContext)))
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
			list.Insert(0, new PersonnelModel(GlobalHelper.GetLoggedInBadgeNumber(httpContext), PersianString.Myself, ""));
			return list;
		}
	}
}
