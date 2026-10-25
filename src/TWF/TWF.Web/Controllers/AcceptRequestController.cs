using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.LeaveCalcs;
using T5V3.Model;
using TWF.Domain.Models;

namespace MyTime3.Controllers
{
    public class AcceptRequestController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index(long forBadgeNumberFilter = -1L, bool fromHome = true, int sort = -1, bool g = true)
        {
            List<MyTime3.Models.Request> list = new List<MyTime3.Models.Request>();

            ViewBag.SortByDate = sort;
            ViewBag.GroupByName = g;
            if (forBadgeNumberFilter != -1)
            {
                ViewBag.requestBadgeFilter = forBadgeNumberFilter;
                ViewBag.PrePageLink = fromHome ? "/Home/Index" : "/AcceptRequest/Index";
            }

            int titleID = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);
            long loggedInBadge = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                var pendingAccepters = adapter.GetPendingRequestAccepterTitles();

                if (titleID != -1)
                {
                    foreach (long reqId in pendingAccepters.Where(r => r.Value == titleID).Select(r => r.Key))
                    {
                        var dto = adapter.GetRequestById(reqId);
                        if (dto == null)
                            continue;
                        if (forBadgeNumberFilter == -1 || forBadgeNumberFilter == dto.BadgeNumber)
                        {
                            var modelReq = EditRequestController.MapDtoToModelRequest(dto);
                            modelReq.ForDeputy = false;
                            list.Add(modelReq);
                        }
                    }
                }

                foreach (long deputyForBadge in adapter.GetPrincipalsWhereEmployeeIsDeputy(loggedInBadge))
                {
                    int deputyTitleID = adapter.GetCurrentTitleIdForEmployee(deputyForBadge);
                    if (deputyTitleID != -1)
                    {
                        var depChart = adapter.GetTitleNodeById(deputyTitleID);
                        string depTitleName = depChart != null ? depChart.TitleName : string.Empty;

                        foreach (long reqId in pendingAccepters.Where(r => r.Value == deputyTitleID).Select(r => r.Key))
                        {
                            var dto = adapter.GetRequestById(reqId);
                            if (dto == null)
                                continue;
                            if (forBadgeNumberFilter == -1 || forBadgeNumberFilter == dto.BadgeNumber)
                            {
                                var modelReq = EditRequestController.MapDtoToModelRequest(dto);
                                modelReq.ForDeputy = true;
                                modelReq.DeputyTitleName = depTitleName;
                                list.Add(modelReq);
                            }
                        }
                    }
                }

                return View(list);
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();

            List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(
                personnelSystem.DB.UserInfos.GetAllUserInfoTitles(),
                personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly());

            if (titleID != -1)
            {
                foreach (long reqId in requestAccepters.Where(r => r.Value == titleID).Select(r => r.Key))
                {
                    T5V3.Model.Request reqT5 = t5DBInstance.RequestDA.GetByID(reqId);
                    if (reqT5 == null)
                    {
                        continue;
                    }

                    MyTime3.Models.Request request = new MyTime3.Models.Request
                    {
                        ForDeputy = false,
                        RequestT5 = reqT5
                    };

                    Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(reqT5.BadgeNumber);
                    if (byID != null)
                    {
                        request.RequestT5.RequesterName = byID.ToString();
                    }

                    if (forBadgeNumberFilter == -1 || (byID != null && forBadgeNumberFilter == byID.BadgeNumber))
                    {
                        list.Add(request);
                    }
                }
            }

            foreach (long deputyForBadge in t5DBInstance.PersonnelDeputyDA.IsDeputy(loggedInBadge))
            {
                int deputyTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(deputyForBadge);
                if (deputyTitleID != -1)
                {
                    Personnel.Model.TitleChart deputyChart = personnelSystem.DB.TitlesCharts.GetByID(deputyTitleID);
                    string deputyTitleName = deputyChart != null ? deputyChart.TitleName : string.Empty;

                    foreach (long reqId in requestAccepters.Where(r => r.Value == deputyTitleID).Select(r => r.Key))
                    {
                        T5V3.Model.Request reqT5 = t5DBInstance.RequestDA.GetByID(reqId);
                        if (reqT5 == null)
                        {
                            continue;
                        }

                        MyTime3.Models.Request request2 = new MyTime3.Models.Request
                        {
                            ForDeputy = true,
                            DeputyTitleName = deputyTitleName,
                            RequestT5 = reqT5
                        };

                        Personnel.Model.UserInfo byID2 = personnelSystem.DB.UserInfos.GetByID(reqT5.BadgeNumber);
                        if (byID2 != null)
                        {
                            request2.RequestT5.RequesterName = byID2.ToString();
                        }

                        if (forBadgeNumberFilter == -1 || (byID2 != null && forBadgeNumberFilter == byID2.BadgeNumber))
                        {
                            list.Add(request2);
                        }
                    }
                }
            }

            return View(list);
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Accept(long requestID, bool? returnToHome = null)
        {
            long loggedBadge = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var dto = DBInitializer.TestBackendAdapter.GetRequestById(requestID);
                if (dto == null)
                {
                    return RedirectToAction("Index");
                }

                PersianCalendar pc = new PersianCalendar();
                DateTime currentYearStart = pc.ToDateTime(pc.GetYear(DateTime.Now), 1, 1, 0, 0, 0, 0);
                string status = (dto.ForDate >= currentYearStart)
                    ? AcceptByID2(loggedBadge, requestID)
                    : AcceptByID(loggedBadge, requestID);

                if (status != "0")
                {
                    return View("Error", "", status);
                }
                if (returnToHome.HasValue)
                {
                    return RedirectToAction("Index", "Home");
                }
                return RedirectToAction("Index");
            }

            T5V3.Model.Request byID = DBInitializer.GetT5DBInstance().RequestDA.GetByID(requestID);
            if (byID == null)
            {
                return RedirectToAction("Index");
            }

            PersianCalendar persianCal = new PersianCalendar();
            DateTime yearStart = persianCal.ToDateTime(persianCal.GetYear(DateTime.Now), 1, 1, 0, 0, 0, 0);

            string resStatus = (byID.ForDate >= yearStart)
                ? AcceptByID2(loggedBadge, requestID)
                : AcceptByID(loggedBadge, requestID);

            if (resStatus != "0")
            {
                return View("Error", "", resStatus);
            }

            if (returnToHome.HasValue)
            {
                return RedirectToAction("Index", "Home");
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public JsonResult AcceptRequest(string userName, string password, long requestID)
        {
            string result = "";
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                long? badge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    result = AcceptByID2(badge.Value, requestID);
                }
                return Json(new { result });
            }

            long badgeNumber = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
            if (badgeNumber > 0)
            {
                result = AcceptByID2(badgeNumber, requestID);
            }
            return Json(new { result });
        }

        [HttpPost]
        public JsonResult CancelRequest(string userName, string password, long requestID)
        {
            string result = "";
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                long? badge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    result = CancelByID(badge.Value, requestID);
                }
                return Json(new { result });
            }

            long badgeNumber = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
            if (badgeNumber > 0)
            {
                result = CancelByID(badgeNumber, requestID);
            }
            return Json(new { result });
        }

        private string AcceptByID2(long badgeNumber, long requestID)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                var reqDto = adapter.GetRequestById(requestID);
                if (reqDto == null)
                    return "0";

                // Fixed BUG-08: Only validate leave balance when WorkflowType is Leave (not Duty or OverTime!)
                if (reqDto.LeaveTypeId != -1 &&
                    (reqDto.WorkflowType == TwfWorkflowType.DailyLeave || reqDto.WorkflowType == TwfWorkflowType.HourlyLeave))
                {
                    string timeErr = EditRequestController.CheckRequestTime(reqDto.ForDate);
                    if (!string.IsNullOrEmpty(timeErr))
                        return timeErr;

                    var lt = adapter.GetLeaveTypeById(reqDto.LeaveTypeId);
                    if (lt != null)
                    {
                        var remainCtrl = new RemainLeaveController();
                        // Fixed BUG-08: check requester's badge (reqDto.BadgeNumber), NOT manager's badge (badgeNumber)!
                        var rem = remainCtrl.GetRemain(reqDto.BadgeNumber, reqDto.LeaveTypeId, reqDto.ForDate);
                        double maxMinusDays = lt.EachXHoursADayMinutes > 0
                            ? (double)lt.MaxMinusMinutes / lt.EachXHoursADayMinutes
                            : 0.0;
                        if (rem != null && rem.Remain + maxMinusDays < 0.0)
                        {
                            return PersianString.DailyLeaveLimit;
                        }
                    }
                }

                return PerformTestAccept(badgeNumber, requestID, null);
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            T5V3.Model.Request req = t5DBInstance.RequestDA.GetByID(requestID);
            if (req == null)
            {
                return "0";
            }

            // Fixed BUG-08: Only validate leave balance when WorkflowType == WorkflowOperations.Leave and LeaveID != -1
            if (req.LeaveID != -1 && req.WorkflowType == WorkflowOperations.Leave)
            {
                string timeError = EditRequestController.CheckRequestTime(req.ForDate);
                if (!string.IsNullOrEmpty(timeError))
                {
                    return timeError;
                }

                LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(req.LeaveID);
                if (byID != null)
                {
                    PersianCalendar pc = new PersianCalendar();
                    int year = pc.GetYear(req.ForDate);
                    int month = pc.GetMonth(req.ForDate);
                    bool hasRemainOnRequestDay = false;

                    Attendance attendance = new Attendance(t5DBInstance, personnelSystem);
                    DateTime fromDate = pc.ToDateTime(year, 1, 1, 0, 0, 0, 0);
                    DateTime toDate = pc.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);
                    List<ADayResult> yearAttendance = attendance.Calculate(req.BadgeNumber, fromDate, toDate);
                    List<LeaveType> allLeaveTypes = t5DBInstance.LeaveTypeDA.GetAll();
                    List<Schedule> allSchedules = t5DBInstance.ScheduleDA.GetAll();
                    List<Paramtr> allParameters = t5DBInstance.ParameterDA.GetAll();

                    Remain remain = new Remain(
                        yearAttendance,
                        allLeaveTypes,
                        t5DBInstance.LeavesAddedDA.GetAll(new List<long> { req.BadgeNumber }, fromDate, toDate),
                        t5DBInstance.YearRemainDA.GetAll(req.BadgeNumber),
                        allSchedules,
                        allParameters);

                    RemainLeaveController remainLeaveController = new RemainLeaveController();
                    DateTime currentYearStart = pc.ToDateTime(pc.GetYear(DateTime.Now), 1, 1, 0, 0, 0, 0);
                    // Fixed BUG-08: check requester's badge (req.BadgeNumber), not manager's badge (badgeNumber)!
                    remainLeaveController.GetRemain(req.BadgeNumber, req.LeaveID, currentYearStart.AddDays(-1.0).Date);

                    List<ADayRemain> dailyRemains = remain.CalculateRemain(
                        personnelSystem.DB.UserInfos.GetByID(req.BadgeNumber),
                        byID.LeaveTypeID,
                        year);

                    ADayRemain remainOnRequestDate = dailyRemains.FirstOrDefault(rc => rc.ForDate == req.ForDate);
                    if (remainOnRequestDate != null && remainOnRequestDate.Remain > 0.0 - byID.MaxMinus)
                    {
                        hasRemainOnRequestDay = true;
                    }

                    bool exceedsLeaveLimit = false;
                    int requestedAbsenceDays = 0;
                    DateTime currentDay = req.ForDate;
                    while (currentDay <= req.ToTime)
                    {
                        // Fixed BUG-08: pass req.BadgeNumber instead of manager's badgeNumber
                        if (HomeController.CalculateDailyAbsence(req.BadgeNumber, currentDay, byID.AutoInHolidays))
                        {
                            requestedAbsenceDays++;
                        }
                        currentDay = currentDay.AddDays(1.0);
                    }

                    double eachDayHours = byID.EachXHoursADay.TotalHours > 0 ? byID.EachXHoursADay.TotalHours : 8.0;

                    for (int m = 1; m <= 12; m++)
                    {
                        int daysInMonth = pc.GetDaysInMonth(year, m);
                        DateTime monthStart = pc.ToDateTime(year, m, 1, 0, 0, 0, 0);
                        DateTime monthEnd = pc.ToDateTime(year, m, daysInMonth, 0, 0, 0, 0);

                        // Fixed BUG-05 & BUG-08: query req.BadgeNumber and compare against byID.LeaveTypeID
                        foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(req.BadgeNumber, monthStart, monthEnd))
                        {
                            if (item.RequestID == req.RequestID)
                            {
                                continue;
                            }

                            if (item.IsSuspend && item.WorkflowType == WorkflowOperations.Leave)
                            {
                                LeaveType itemLeaveType = allLeaveTypes.FirstOrDefault(lt2 => lt2.LeaveTypeID == item.LeaveID);
                                if (itemLeaveType != null)
                                {
                                    bool matchesTargetLeave =
                                        itemLeaveType.LeaveTypeID == byID.LeaveTypeID ||
                                        (itemLeaveType.DecreaseFromLeaveID.HasValue && itemLeaveType.DecreaseFromLeaveID.Value == byID.LeaveTypeID);

                                    if (matchesTargetLeave)
                                    {
                                        double _ = item.IsDaily ? 1.0 : (item.ToTime - item.FromTime).TotalHours / eachDayHours;
                                    }
                                }
                            }
                        }

                        ADayRemain monthEndRemainObj = dailyRemains.FirstOrDefault(rc => rc.ForDate == monthEnd);
                        double monthEndRemain = monthEndRemainObj != null ? monthEndRemainObj.Remain : 0.0;
                        if (m == month &&
                            (byID.GetInType == LeaveGetInType.MonthFirst || byID.GetInType == LeaveGetInType.MonthLast) &&
                            monthEndRemain + byID.MaxMinus - requestedAbsenceDays < 0.0)
                        {
                            exceedsLeaveLimit = true;
                        }
                    }

                    // Fixed BUG-06: use PersianCalendar instead of hardcoded 2020-03-21
                    ADayRemain targetRemainObj = (req.ForDate.Date < currentYearStart.Date && DateTime.Now.Date >= currentYearStart.Date)
                        ? dailyRemains.FirstOrDefault(rc => rc.ForDate.Date == req.ForDate.Date)
                        : dailyRemains.FirstOrDefault(rc => rc.ForDate.Date == DateTime.Now.Date);

                    double currentRemain = targetRemainObj != null ? targetRemainObj.Remain : 0.0;
                    if (currentRemain + byID.MaxMinus - requestedAbsenceDays < 0.0)
                    {
                        exceedsLeaveLimit = true;
                    }

                    if (exceedsLeaveLimit)
                    {
                        return PersianString.DailyLeaveLimit;
                    }
                    if (!hasRemainOnRequestDay)
                    {
                        return PersianString.RemainLeaveError;
                    }
                }
            }

            return PerformAccept(badgeNumber, requestID);
        }

        private string AcceptByID1(long badgeNumber, long requestID)
        {
            return AcceptByID2(badgeNumber, requestID);
        }

        private string AcceptByID(long badgeNumber, long requestID)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                return PerformTestAccept(badgeNumber, requestID, null);
            }
            return PerformAccept(badgeNumber, requestID);
        }

        private static string PerformTestAccept(long badgeNumber, long requestID, TimeSpan? editedOvertimeDuration)
        {
            var adapter = DBInitializer.TestBackendAdapter;
            var pending = adapter.GetPendingRequestAccepterTitles(requestID);
            int userTitleId = adapter.GetCurrentTitleIdForEmployee(badgeNumber);
            var deputyTitleIds = new List<int>();
            foreach (long depForBadge in adapter.GetPrincipalsWhereEmployeeIsDeputy(badgeNumber))
            {
                int dt = adapter.GetCurrentTitleIdForEmployee(depForBadge);
                if (dt > 0)
                    deputyTitleIds.Add(dt);
            }

            if ((userTitleId > 0 || deputyTitleIds.Count > 0) && pending.Count == 1)
            {
                int reqTitleId = pending[0].Value;
                if (reqTitleId != userTitleId && !deputyTitleIds.Contains(reqTitleId))
                    return "0";

                var step = new RequestAcceptStepDto
                {
                    RequestId = requestID,
                    AccepterBadgeNumber = badgeNumber,
                    AcceptDate = DateTime.Now,
                    IsCancel = false,
                    AccepterTitleId = deputyTitleIds.Contains(reqTitleId) ? reqTitleId : userTitleId,
                    IsDeputy = deputyTitleIds.Contains(reqTitleId)
                };

                if (adapter.AddRequestAcceptStep(step, out string err))
                {
                    var reqDto = adapter.GetRequestById(requestID);
                    if (editedOvertimeDuration.HasValue && reqDto != null)
                    {
                        reqDto.AllowValueDuration = editedOvertimeDuration.Value;
                        reqDto.AllowDurationMinutes = (int)Math.Round(editedOvertimeDuration.Value.TotalMinutes);
                        adapter.UpdateRequest(reqDto, out err);
                    }

                    var nextPending = adapter.GetPendingRequestAccepterTitles(requestID);
                    if (nextPending.Count == 1 && nextPending[0].Value == 0 && reqDto != null)
                    {
                        if (!adapter.InsertOperationalDataForApprovedRequest(reqDto, out string insErr))
                        {
                            return insErr;
                        }
                        reqDto.IsSuspended = false;
                        reqDto.IsSuspend = false;
                        adapter.UpdateRequest(reqDto, out _);
                    }
                }
            }
            return "0";
        }

        private string PerformAccept(long badgeNumber, long requestID)
        {
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(
                personnelSystem.DB.UserInfos.GetAllUserInfoTitles(),
                personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(),
                requestID);

            int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(badgeNumber);
            List<int> deputyTitleIds = new List<int>();
            foreach (long deputyForBadge in t5DBInstance.PersonnelDeputyDA.IsDeputy(badgeNumber))
            {
                int deputyTitleId = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(deputyForBadge);
                if (deputyTitleId != -1)
                {
                    deputyTitleIds.Add(deputyTitleId);
                }
            }

            if ((userCurrentTitleID != -1 || deputyTitleIds.Count > 0) && requestAccepters.Count == 1)
            {
                int requiredTitleId = requestAccepters[0].Value;
                if (requiredTitleId != userCurrentTitleID && !deputyTitleIds.Contains(requiredTitleId))
                {
                    return "0";
                }

                RequestAcceptMdl acceptMdl = new RequestAcceptMdl
                {
                    AcceptDate = DateTime.Now,
                    AccepterBadgeNumber = badgeNumber,
                    IsCancel = false,
                    RequestID = requestID,
                    AccepterTitleID = deputyTitleIds.Contains(requiredTitleId) ? requiredTitleId : userCurrentTitleID,
                    IsDeputy = deputyTitleIds.Contains(requiredTitleId)
                };

                if (t5DBInstance.RequestAcceptDA.AddNew(acceptMdl, out string _))
                {
                    List<KeyValuePair<long, int>> nextAccepters = t5DBInstance.RequestDA.GetRequestAccepters(
                        personnelSystem.DB.UserInfos.GetAllUserInfoTitles(),
                        personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(),
                        requestID);

                    if (nextAccepters.Count == 1 && nextAccepters[0].Value == 0)
                    {
                        if (!InsertData(requestID, out string insertErr))
                        {
                            return insertErr;
                        }
                    }
                }
            }

            return "0";
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Accept(long requestID, string editedValue, bool? returnToHome = null, long forBadgeNumberFilter = -1L)
        {
            if (TimeSpan.TryParse(editedValue, out TimeSpan parsedDuration))
            {
                long loggedInBadge = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    string status = PerformTestAccept(loggedInBadge, requestID, parsedDuration);
                    if (status != "0")
                    {
                        return View("Error", "", status);
                    }
                }
                else
                {
                    PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                    T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                    List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(
                        personnelSystem.DB.UserInfos.GetAllUserInfoTitles(),
                        personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(),
                        requestID);

                    int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);
                    List<int> deputyTitleIds = new List<int>();
                    foreach (long deputyForBadge in t5DBInstance.PersonnelDeputyDA.IsDeputy(loggedInBadge))
                    {
                        int deputyTitleId = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(deputyForBadge);
                        if (deputyTitleId != -1)
                        {
                            deputyTitleIds.Add(deputyTitleId);
                        }
                    }

                    if ((loggedInUserInfoTitleID != -1 || deputyTitleIds.Count > 0) && requestAccepters.Count == 1)
                    {
                        int requiredTitleId = requestAccepters[0].Value;
                        if (requiredTitleId == loggedInUserInfoTitleID || deputyTitleIds.Contains(requiredTitleId))
                        {
                            RequestAcceptMdl acceptMdl = new RequestAcceptMdl
                            {
                                AcceptDate = DateTime.Now,
                                AccepterBadgeNumber = loggedInBadge,
                                IsCancel = false,
                                RequestID = requestID,
                                AccepterTitleID = deputyTitleIds.Contains(requiredTitleId) ? requiredTitleId : loggedInUserInfoTitleID,
                                IsDeputy = deputyTitleIds.Contains(requiredTitleId)
                            };

                            if (t5DBInstance.RequestAcceptDA.AddNew(acceptMdl, out string err))
                            {
                                T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(requestID);
                                if (byID != null)
                                {
                                    byID.AllowValue = parsedDuration;
                                    t5DBInstance.RequestDA.Edit(byID, out err);
                                }

                                List<KeyValuePair<long, int>> nextAccepters = t5DBInstance.RequestDA.GetRequestAccepters(
                                    personnelSystem.DB.UserInfos.GetAllUserInfoTitles(),
                                    personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(),
                                    requestID);

                                if (nextAccepters.Count == 1 && nextAccepters[0].Value == 0)
                                {
                                    if (!InsertData(requestID, out string insertErr))
                                    {
                                        return View("Error", "", insertErr);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (returnToHome.HasValue)
            {
                return RedirectToAction("Index", "Home");
            }
            if (forBadgeNumberFilter != -1)
            {
                return RedirectToAction("Index", new { forBadgeNumberFilter, fromHome = false });
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult AcceptAll(string action, string action2, string[] allOverIDs, string[] requestsChecked, string[] overValues, bool returnToHome = false, long forBadgeNumberFilter = -1L)
        {
            long loggedInBadge = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

            if (requestsChecked != null)
            {
                PersianCalendar pc = new PersianCalendar();
                DateTime currentYearStart = pc.ToDateTime(pc.GetYear(DateTime.Now), 1, 1, 0, 0, 0, 0);

                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    var adapter = DBInitializer.TestBackendAdapter;
                    var reqDtos = new List<WorkflowRequestItemDto>();
                    foreach (string rawId in requestsChecked)
                    {
                        if (long.TryParse(rawId, out long parsedId))
                        {
                            var r = adapter.GetRequestById(parsedId);
                            if (r != null)
                                reqDtos.Add(r);
                        }
                    }

                    if (action2 == "0" || action == "تایید انتخاب شده ها")
                    {
                        foreach (var req in reqDtos.OrderBy(r => r.ForDate))
                        {
                            if (req.ForDate < currentYearStart)
                                AcceptByID(loggedInBadge, req.RequestId);
                            else
                                AcceptByID2(loggedInBadge, req.RequestId);
                        }
                    }
                    else
                    {
                        foreach (var req in reqDtos.OrderBy(r => r.ForDate))
                        {
                            CancelByID(loggedInBadge, req.RequestId);
                        }
                    }
                }
                else
                {
                    T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                    List<T5V3.Model.Request> selectedRequests = new List<T5V3.Model.Request>();
                    foreach (string rawId in requestsChecked)
                    {
                        if (long.TryParse(rawId, out long parsedId))
                        {
                            T5V3.Model.Request req = t5DBInstance.RequestDA.GetByID(parsedId);
                            if (req != null)
                            {
                                selectedRequests.Add(req);
                            }
                        }
                    }

                    if (action2 == "0" || action == "تایید انتخاب شده ها")
                    {
                        foreach (T5V3.Model.Request req in selectedRequests.OrderBy(r => r.ForDate))
                        {
                            if (req.ForDate < currentYearStart)
                            {
                                AcceptByID(loggedInBadge, req.RequestID);
                            }
                            else
                            {
                                AcceptByID2(loggedInBadge, req.RequestID);
                            }
                        }
                    }
                    else
                    {
                        foreach (T5V3.Model.Request req in selectedRequests.OrderBy(r => r.ForDate))
                        {
                            CancelByID(loggedInBadge, req.RequestID);
                        }
                    }
                }
            }

            if (returnToHome)
            {
                return RedirectToAction("Index", "Home");
            }
            if (forBadgeNumberFilter != -1)
            {
                return RedirectToAction("Index", new { forBadgeNumberFilter, fromHome = false });
            }
            return RedirectToAction("Index");
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Cancel(long requestID, bool? returnToHome = null)
        {
            string status = CancelByID(GlobalHelper.GetLoggedInBadgeNumber(HttpContext), requestID);
            if (status != "0")
            {
                return View("Error", "", status);
            }
            if (returnToHome.HasValue)
            {
                return RedirectToAction("Index", "Home");
            }
            return RedirectToAction("Index");
        }

        private string CancelByID(long badgeNumber, long requestID)
        {
            // Fixed BUG-09: use badgeNumber (falling back to session only if badgeNumber <= 0) so Mobile API CancelRequest records the actual accepter badgeNumber!
            long effectiveAccepterBadge = badgeNumber > 0 ? badgeNumber : GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                var pending = adapter.GetPendingRequestAccepterTitles(requestID);
                int userTitleId = adapter.GetCurrentTitleIdForEmployee(effectiveAccepterBadge);
                var deputyTitleIds = new List<int>();
                foreach (long depForBadge in adapter.GetPrincipalsWhereEmployeeIsDeputy(effectiveAccepterBadge))
                {
                    int dt = adapter.GetCurrentTitleIdForEmployee(depForBadge);
                    if (dt > 0)
                        deputyTitleIds.Add(dt);
                }

                if ((userTitleId > 0 || deputyTitleIds.Count > 0) && pending.Count == 1)
                {
                    int reqTitleId = pending[0].Value;
                    if (reqTitleId != userTitleId && !deputyTitleIds.Contains(reqTitleId))
                        return "0";

                    var step = new RequestAcceptStepDto
                    {
                        RequestId = requestID,
                        AccepterBadgeNumber = effectiveAccepterBadge,
                        AcceptDate = DateTime.Now,
                        IsCancel = true,
                        AccepterTitleId = deputyTitleIds.Contains(reqTitleId) ? reqTitleId : userTitleId,
                        IsDeputy = deputyTitleIds.Contains(reqTitleId)
                    };

                    if (adapter.AddRequestAcceptStep(step, out string err))
                    {
                        var reqDto = adapter.GetRequestById(requestID);
                        if (reqDto != null)
                        {
                            reqDto.IsSuspended = false;
                            reqDto.IsSuspend = false;
                            if (!adapter.UpdateRequest(reqDto, out err))
                            {
                                return err;
                            }
                        }
                    }
                }
                return "0";
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(
                personnelSystem.DB.UserInfos.GetAllUserInfoTitles(),
                personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(),
                requestID);

            int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(effectiveAccepterBadge);
            List<int> deputyTitleIdsProd = new List<int>();
            foreach (long deputyForBadge in t5DBInstance.PersonnelDeputyDA.IsDeputy(effectiveAccepterBadge))
            {
                int deputyTitleId = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(deputyForBadge);
                if (deputyTitleId != -1)
                {
                    deputyTitleIdsProd.Add(deputyTitleId);
                }
            }

            if ((userCurrentTitleID != -1 || deputyTitleIdsProd.Count > 0) && requestAccepters.Count == 1)
            {
                int requiredTitleId = requestAccepters[0].Value;
                if (requiredTitleId != userCurrentTitleID && !deputyTitleIdsProd.Contains(requiredTitleId))
                {
                    return "0";
                }

                RequestAcceptMdl acceptMdl = new RequestAcceptMdl
                {
                    AcceptDate = DateTime.Now,
                    AccepterBadgeNumber = effectiveAccepterBadge,
                    IsCancel = true,
                    RequestID = requestID,
                    AccepterTitleID = deputyTitleIdsProd.Contains(requiredTitleId) ? requiredTitleId : userCurrentTitleID,
                    IsDeputy = deputyTitleIdsProd.Contains(requiredTitleId)
                };

                if (t5DBInstance.RequestAcceptDA.AddNew(acceptMdl, out string err))
                {
                    T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(requestID);
                    if (byID != null)
                    {
                        byID.IsSuspend = false;
                        if (!t5DBInstance.RequestDA.Edit(byID, out err))
                        {
                            return err;
                        }
                    }
                }
            }

            return "0";
        }

        [HttpPost]
        public JsonResult AcceptAllRequests(string userName, string password, string requestIDs)
        {
            bool allSucceeded = true;
            long badgeNumber = -1L;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                badgeNumber = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password) ?? -1L;
            }
            else
            {
                badgeNumber = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
            }

            if (badgeNumber > 0 && !string.IsNullOrEmpty(requestIDs))
            {
                foreach (string rawId in requestIDs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (long.TryParse(rawId, out long reqId) && AcceptByID2(badgeNumber, reqId) != "0")
                    {
                        allSucceeded = false;
                    }
                }
            }
            return Json(new { result = allSucceeded ? "0" : "-1" });
        }

        [HttpPost]
        public JsonResult CancelAllRequests(string userName, string password, string requestIDs)
        {
            bool allSucceeded = true;
            long badgeNumber = -1L;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                badgeNumber = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password) ?? -1L;
            }
            else
            {
                badgeNumber = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
            }

            if (badgeNumber > 0 && !string.IsNullOrEmpty(requestIDs))
            {
                foreach (string rawId in requestIDs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (long.TryParse(rawId, out long reqId) && CancelByID(badgeNumber, reqId) != "0")
                    {
                        allSucceeded = false;
                    }
                }
            }
            return Json(new { result = allSucceeded ? "0" : "-1" });
        }

        private bool InsertData(long requestID, out string err)
        {
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            err = "";
            bool succeeded = false;
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
                        DailyLeave dailyLeave = new DailyLeave
                        {
                            BadgeNumber = byID.BadgeNumber,
                            Descriptions = byID.Descriptions,
                            DetailDescriptionID = byID.LeaveDescriptionID,
                            LeaveDate = currentDay,
                            DocumentNumber = byID.RequestID.ToString(),
                            LeaveTypeID = byID.LeaveID
                        };
                        succeeded = t5DBInstance.DailyLeaveDA.AddNew(dailyLeave, out err);
                        currentDay = currentDay.AddDays(1.0);
                    }
                }
                else
                {
                    HourlyLeave hourlyLeave = new HourlyLeave
                    {
                        BadgeNumber = byID.BadgeNumber,
                        Descriptions = byID.Descriptions,
                        LeaveTypeID = byID.LeaveID,
                        DetailDescriptions = new LeaveDescription { ID = byID.LeaveDescriptionID },
                        FromTime = byID.ForDate.Date.Add(byID.FromTime.TimeOfDay),
                        ToTime = (byID.ToTime.TimeOfDay < byID.FromTime.TimeOfDay)
                            ? byID.ForDate.Date.Add(byID.ToTime.TimeOfDay).AddDays(1.0)
                            : byID.ForDate.Date.Add(byID.ToTime.TimeOfDay)
                    };
                    succeeded = t5DBInstance.HourlyLeaveDA.AddNew(hourlyLeave, true, out err, out int _);
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
                succeeded = t5DBInstance.EditedValueDA.InsertOrEdit(editedValue, out err);
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
                succeeded = t5DBInstance.EditedValueDA.InsertOrEdit(editedValue, out err);
            }

            if (byID.WorkflowType == WorkflowOperations.AllowOverTimes)
            {
                AllowedValue allowValue = new AllowedValue(byID.BadgeNumber, byID.ForDate.Date, byID.OverTimeAllowType, byID.AllowValue);
                succeeded = t5DBInstance.AllowValueDA.AddOrEdit(allowValue);
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
                succeeded = t5DBInstance.RecordDA.Insert(record, out err);
            }

            if (succeeded)
            {
                byID.IsSuspend = false;
                succeeded = t5DBInstance.RequestDA.Edit(byID, out err);
            }

            return succeeded;
        }
    }
}
