using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class RewardController : Controller
	{
		public ActionResult Add(string forDate = null, int valueType = -1, string validValue = null)
		{
			RewardPenaltyRequest rewardPenaltyRequest = new RewardPenaltyRequest();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			GlobalHelper.AddRewardPenaltyTypes(base.HttpContext, base.ViewBag);
			rewardPenaltyRequest.ValidValue = "00:00";
			if (forDate != null)
			{
				rewardPenaltyRequest.ForDateHijri = forDate;
			}
			else
			{
				rewardPenaltyRequest.ForDateHijri = DateConverter.ToHijriDate(DateTime.Now);
			}
			if (validValue != null)
			{
				rewardPenaltyRequest.ValidValue = validValue;
			}
			if (valueType != -1)
			{
				rewardPenaltyRequest.RewardPenaltyType = valueType;
			}
			List<SelectListItem> list = new List<SelectListItem>();
			List<int> titleIDs = (from wf in t5DBInstance.WorkflowDA.GetAll(GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext)).Where(delegate(Workflow wf)
			{
				if (wf.Operation == WorkflowOperations.RewardAndPenalty)
				{
					return wf.OrderIndex == 1;
				}
				return false;
			})
			select wf.TitleID).ToList();
			foreach (Personnel.Model.UserInfo item in personnelSystem.DB.UserInfos.GetAllByTitle(titleIDs))
			{
				SelectListItem selectListItem = new SelectListItem();
				selectListItem.Text = item.ToString();
				selectListItem.Value = item.BadgeNumber.ToString();
				list.Add(selectListItem);
			}
			base.ViewBag.SubUsers = list;
			return View(rewardPenaltyRequest);
		}

		[HttpPost]
		public ActionResult Add(RewardPenaltyRequest request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			GlobalHelper.AddRewardPenaltyTypes(base.HttpContext, base.ViewBag);
			List<SelectListItem> list = new List<SelectListItem>();
			List<int> titleIDs = (from wf in t5DBInstance.WorkflowDA.GetAll(GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext)).Where(delegate(Workflow wf)
			{
				if (wf.Operation == WorkflowOperations.RewardAndPenalty)
				{
					return wf.OrderIndex == 1;
				}
				return false;
			})
			select wf.TitleID).ToList();
			foreach (Personnel.Model.UserInfo item in personnelSystem.DB.UserInfos.GetAllByTitle(titleIDs))
			{
				SelectListItem selectListItem = new SelectListItem();
				selectListItem.Text = item.ToString();
				selectListItem.Value = item.BadgeNumber.ToString();
				list.Add(selectListItem);
			}
			base.ViewBag.SubUsers = list;
			base.ViewBag.Message = RequestReward(request);
			return View(request);
		}

		private string RequestReward(RewardPenaltyRequest request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			if (request.Descriptions == null)
			{
				request.Descriptions = "";
			}
			string text = "";
			int rewardPenaltyType = request.RewardPenaltyType;
			if (request.ForBadgeNumber != 0L)
			{
				DateTime? nullable = DateConverter.ToChristian(request.ForDateHijri);
				if (nullable.HasValue)
				{
					TimeSpan result = default(TimeSpan);
					if (TimeSpan.TryParse(request.ValidValue, out result))
					{
						if (t5DBInstance.WorkflowDA.GetAll(personnelSystem.DB.UserInfos.GetUserCurrentTitleID(request.ForBadgeNumber), WorkflowOperations.RewardAndPenalty).Count == 1)
						{
							EditedValue editedValue = new EditedValue();
							editedValue.BadgeNumber = request.ForBadgeNumber;
							editedValue.ForDate = nullable.Value;
							editedValue.ValidDuration = result;
							editedValue.ValueType = (EditValueType)request.RewardPenaltyType;
							string err = "";
							text = ((!t5DBInstance.EditedValueDA.InsertOrEdit(editedValue, out err)) ? PersianString.ErrorInInsertingData : PersianString.InsertSuccess);
						}
						else
						{
							T5V3.Model.Request request2 = new T5V3.Model.Request();
							request2.AllowValue = result;
							request2.BadgeNumber = request.ForBadgeNumber;
							request2.CreateTime = DateTime.Now;
							request2.ForDate = nullable.Value;
							T5V3.Model.Request request3 = request2;
							DateTime now = DateTime.Now;
							request3.FromTime = now.Date;
							request2.IsSuspend = true;
							request2.IsDaily = true;
							request2.LeaveID = 1;
							request2.LeaveDescriptionID = 1;
							if (request.RewardPenaltyType == 6)
							{
								request2.OverTimeAllowType = AllowType.Early;
							}
							else if (request.RewardPenaltyType == 7)
							{
								request2.OverTimeAllowType = AllowType.Late;
							}
							T5V3.Model.Request request4 = request2;
							now = DateTime.Now;
							request4.RecordTime = now.Date;
							request2.ToTime = nullable.Value;
							request2.WorkflowType = WorkflowOperations.RewardAndPenalty;
							request2.Descriptions = request.Descriptions;
							if (t5DBInstance.RequestDA.AddNew(request2, true, out string err2, out long newID))
							{
								RequestAcceptMdl requestAcceptMdl = new RequestAcceptMdl();
								requestAcceptMdl.AcceptDate = DateTime.Now;
								requestAcceptMdl.AccepterBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
								requestAcceptMdl.AccepterTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
								requestAcceptMdl.IsCancel = false;
								requestAcceptMdl.RequestID = newID;
								t5DBInstance.RequestAcceptDA.AddNew(requestAcceptMdl, out err2);
								text = PersianString.InsertSuccess;
								request2.RequestID = newID;
								NotificationManager.NewRequestAdded(request2);
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
			}
			else
			{
				text = PersianString.BadDescriptions;
			}
			return text;
		}
	}
}
