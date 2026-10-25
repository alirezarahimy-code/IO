using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
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
            List<MyTime3.Models.Request> list = new List<MyTime3.Models.Request>();
            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                if (loggedInBadgeNumber != -1)
                {
                    var adapter = DBInitializer.TestBackendAdapter;
                    double oaLock = HomeController.RequestLockValuee();
                    DateTime minDate = oaLock > 0 ? DateTime.FromOADate(oaLock).Date : DateTime.Now.Date.AddDays(-60.0);
                    foreach (var kv in adapter.GetRequestIdsForUndo(loggedInBadgeNumber, minDate))
                    {
                        var dto = adapter.GetRequestById(kv.Key);
                        if (dto == null)
                            continue;
                        var modelReq = EditRequestController.MapDtoToModelRequest(dto);
                        modelReq.IsCanceled = kv.Value;
                        list.Add(modelReq);
                    }
                }
                return View(list);
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();

            if (loggedInBadgeNumber != -1)
            {
                DateTime lockDate = DateTime.FromOADate(HomeController.RequestLockValuee());
                foreach (KeyValuePair<long, bool> item in t5DBInstance.RequestAcceptDA.GetRequestIDsForUndo(loggedInBadgeNumber, lockDate.Date))
                {
                    T5V3.Model.Request reqT5 = t5DBInstance.RequestDA.GetByID(item.Key);
                    if (reqT5 == null)
                    {
                        continue;
                    }

                    MyTime3.Models.Request request = new MyTime3.Models.Request
                    {
                        ForDeputy = false,
                        RequestT5 = reqT5,
                        IsCanceled = item.Value
                    };

                    Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(reqT5.BadgeNumber);
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
            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                if (loggedInBadgeNumber != -1)
                {
                    var adapter = DBInitializer.TestBackendAdapter;
                    var dto = adapter.GetRequestById(requestID);
                    if (dto != null)
                    {
                        if (dto.IsSuspended)
                        {
                            adapter.DeleteRequestAcceptSteps(requestID, loggedInBadgeNumber);
                        }
                        else if (adapter.DeleteRequestAcceptSteps(requestID, loggedInBadgeNumber))
                        {
                            adapter.DeleteApprovedOperationalData(dto, loggedInBadgeNumber);
                            dto.IsSuspended = true;
                            dto.IsSuspend = true;
                            dto.Descriptions = (dto.Descriptions ?? "") + " - بازگشت داده شده";
                            adapter.UpdateRequest(dto, out _);
                        }
                    }
                }
                return RedirectToAction("Index");
            }

            DBInitializer.GetPersonnelSystem();
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();

            if (loggedInBadgeNumber != -1)
            {
                T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(requestID);
                // Fixed legacy bug: check byID != null before dereferencing byID.IsSuspend
                if (byID != null)
                {
                    if (byID.IsSuspend)
                    {
                        t5DBInstance.RequestAcceptDA.Delete(requestID, loggedInBadgeNumber);
                    }
                    else if (t5DBInstance.RequestAcceptDA.Delete(requestID, loggedInBadgeNumber))
                    {
                        DeleteData(requestID, out string err);
                        byID.IsSuspend = true;
                        byID.Descriptions += " - بازگشت داده شده";
                        t5DBInstance.RequestDA.Edit(byID, out err);
                    }
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
            if (byID == null)
            {
                return false;
            }

            if (byID.WorkflowType == WorkflowOperations.Duty || byID.WorkflowType == WorkflowOperations.Leave)
            {
                if (byID.IsDaily)
                {
                    DateTime currentDay = byID.ForDate.Date;
                    while (currentDay <= byID.ToTime.Date)
                    {
                        // Fixed legacy bug: guard against null if DailyLeaveDA.GetByID returns null
                        DailyLeave dl = t5DBInstance.DailyLeaveDA.GetByID(byID.BadgeNumber, currentDay);
                        if (dl != null)
                        {
                            flag = t5DBInstance.DailyLeaveDA.Delete(dl, out err);
                        }
                        currentDay = currentDay.AddDays(1.0);
                    }
                }
                else
                {
                    HourlyLeaveDA hourlyLeaveDA = t5DBInstance.HourlyLeaveDA;
                    DateTime fromTime = byID.ForDate.Date.Add(byID.FromTime.TimeOfDay);
                    DateTime toTime = byID.ForDate.Date.Add(byID.ToTime.TimeOfDay);
                    flag = hourlyLeaveDA.Delete(byID.BadgeNumber, fromTime, toTime);
                }
            }

            if (byID.WorkflowType == WorkflowOperations.LateEarly)
            {
                EditedValue editedValue = new EditedValue
                {
                    BadgeNumber = byID.BadgeNumber,
                    ForDate = byID.ForDate.Date,
                    ValidDuration = new TimeSpan(0L),
                    ValueType = (byID.OverTimeAllowType == AllowType.Early) ? EditValueType.Early : EditValueType.Late
                };
                flag = t5DBInstance.EditedValueDA.Delete(editedValue, out err);
            }

            if (byID.WorkflowType == WorkflowOperations.RewardAndPenalty)
            {
                EditedValue editedValue = new EditedValue
                {
                    BadgeNumber = byID.BadgeNumber,
                    ForDate = byID.ForDate.Date,
                    ValidDuration = byID.AllowValue,
                    ValueType = (byID.OverTimeAllowType == AllowType.Early) ? EditValueType.Penalty : EditValueType.Reward
                };
                flag = t5DBInstance.EditedValueDA.Delete(editedValue, out err);
            }

            if (byID.WorkflowType == WorkflowOperations.AllowOverTimes)
            {
                AllowedValue allowValue = new AllowedValue(byID.BadgeNumber, byID.ForDate.Date, byID.OverTimeAllowType, byID.AllowValue);
                flag = t5DBInstance.AllowValueDA.Delete(allowValue);
            }

            if (byID.WorkflowType == WorkflowOperations.InsertRecord)
            {
                T5V3.Model.Record record = new T5V3.Model.Record
                {
                    BadgeNumber = byID.BadgeNumber,
                    CheckTime = byID.RecordTime,
                    CheckType = 0,
                    InsertTime = DateTime.Now,
                    InsertUser = GlobalHelper.GetLoggedInBadgeNumber(HttpContext),
                    IsValid = true,
                    MachineNumber = -1,
                    OriginalSCheckType = 0,
                    SourceType = T5V3.Model.RecordSource.Manual,
                    VerifyMode = T5V3.Model.RecordVerifyStyle.FP_OR_PW_OR_RF,
                    WorkCode = 0
                };
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
