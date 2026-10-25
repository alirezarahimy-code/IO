using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using T5V3.DB;
using T5V3.DB.EntityManagers;
using T5V3.Misc;
using T5V3.Model;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    public class ShowRequestController : Controller
    {
        public string name { get; set; }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index(string fromDate = null, string toDate = null)
        {
            string message = "";
            ShowRequestModel showRequestModel = new ShowRequestModel();

            if (fromDate != null || toDate != null)
            {
                DateTime? parsedFrom = DailyLeaveController.ParseHijriDate(fromDate);
                DateTime? parsedTo = DailyLeaveController.ParseHijriDate(toDate);

                if (parsedFrom.HasValue && parsedTo.HasValue && parsedFrom.Value <= parsedTo.Value)
                {
                    showRequestModel.fromDate = fromDate;
                    showRequestModel.toDate = toDate;
                    long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

                    if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                    {
                        var adapter = DBInitializer.TestBackendAdapter;
                        var allTitleNodes = adapter.GetAllTitleNodes();
                        foreach (var dto in adapter.GetEmployeeRequestsInPeriod(loggedInBadgeNumber, parsedFrom.Value.Date, parsedTo.Value.Date))
                        {
                            showRequestModel.Requests.Add(BuildTestRequestViewModel(dto, allTitleNodes));
                        }

                        if ((bool)SettingsController.LoadValue("RequestForUsers", typeof(bool), true))
                        {
                            foreach (PersonnelModel item2 in RequestController.AddPersonnelToViewBag(HttpContext))
                            {
                                foreach (var dto in adapter.GetEmployeeRequestsInPeriod(item2.BadgeNumber, parsedFrom.Value.Date, parsedTo.Value.Date))
                                {
                                    if (!showRequestModel.Requests.Any(r => r.RequestID == dto.RequestId))
                                    {
                                        showRequestModel.Requests.Add(BuildTestRequestViewModel(dto, allTitleNodes));
                                    }
                                }
                            }
                        }

                        ViewBag.Message = message;
                        return View(showRequestModel);
                    }

                    T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                    PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                    RequestDA requestDA = t5DBInstance.RequestDA;
                    List<Personnel.Model.TitleChart> allTitles = personnelSystem.DB.TitlesCharts.GetAll();

                    foreach (T5V3.Model.Request current in requestDA.GetAll(loggedInBadgeNumber, parsedFrom.Value.Date, parsedTo.Value.Date))
                    {
                        MyTime3.Models.Request request = BuildRequestViewModel(t5DBInstance, personnelSystem, allTitles, current);
                        showRequestModel.Requests.Add(request);
                    }

                    if ((bool)SettingsController.LoadValue("RequestForUsers", typeof(bool), true))
                    {
                        foreach (PersonnelModel item2 in RequestController.AddPersonnelToViewBag(HttpContext))
                        {
                            foreach (T5V3.Model.Request current4 in requestDA.GetAll(item2.BadgeNumber, parsedFrom.Value.Date, parsedTo.Value.Date))
                            {
                                if (!showRequestModel.Requests.Any(r => r.RequestID == current4.RequestID))
                                {
                                    MyTime3.Models.Request request4 = BuildRequestViewModel(t5DBInstance, personnelSystem, allTitles, current4);
                                    showRequestModel.Requests.Add(request4);
                                }
                            }
                        }
                    }

                    foreach (MyTime3.Models.Request reqItem in showRequestModel.Requests)
                    {
                        Personnel.Model.UserInfo requester = personnelSystem.DB.UserInfos.GetByID(reqItem.BadgeNumber);
                        reqItem.RequestT5.RequesterName = requester != null ? requester.ToString() : reqItem.BadgeNumber.ToString();
                    }
                }
                else
                {
                    message = PersianString.BadDateFormat;
                }
            }

            ViewBag.Message = message;
            return View(showRequestModel);
        }

        private MyTime3.Models.Request BuildTestRequestViewModel(
            WorkflowRequestItemDto dto,
            IReadOnlyList<OrgTitleNodeDto> allTitleNodes)
        {
            var adapter = DBInitializer.TestBackendAdapter;
            MyTime3.Models.Request request = EditRequestController.MapDtoToModelRequest(dto);

            foreach (var acc in adapter.GetRequestAcceptHistory(dto.RequestId))
            {
                var tl = allTitleNodes.FirstOrDefault(t => t.TitleId == acc.AccepterTitleId);
                string titleName = tl != null ? tl.TitleName : acc.AccepterTitleId.ToString();
                if (acc.IsCancel)
                {
                    request.CanceledTitle = titleName;
                }
                else if (string.IsNullOrEmpty(request.AcceptedTitles))
                {
                    request.AcceptedTitles = titleName;
                }
                else
                {
                    request.AcceptedTitles = request.AcceptedTitles + "-" + titleName;
                }
            }

            var waitForTitles = adapter.GetPendingRequestAccepterTitles(dto.RequestId);
            string currentWaitingTitle = "";
            if (waitForTitles.Count > 0 && waitForTitles[0].Value > 0)
            {
                var matchedTitle = allTitleNodes.FirstOrDefault(t => t.TitleId == waitForTitles[0].Value);
                if (matchedTitle != null)
                {
                    currentWaitingTitle = matchedTitle.TitleName;
                    request.WaitForTitle = currentWaitingTitle;
                    name = currentWaitingTitle;
                }
            }

            // Fixed legacy bug: append undo note once per request (never twice!)
            if (request.Descriptions != null && request.Descriptions.Contains("بازگشت داده شده"))
            {
                request.WaitForTitle = currentWaitingTitle + " بازگشت داده شده توسط " + currentWaitingTitle + " " + JalaliDate.FromDateTime(DateTime.Now);
            }

            return request;
        }

        private MyTime3.Models.Request BuildRequestViewModel(
            T5V3DBManager t5DBInstance,
            PersonnelSystem personnelSystem,
            List<Personnel.Model.TitleChart> allTitles,
            T5V3.Model.Request reqT5)
        {
            MyTime3.Models.Request request = new MyTime3.Models.Request
            {
                RequestT5 = reqT5
            };

            foreach (var item in from acc in t5DBInstance.RequestAcceptDA.GetByID(reqT5.RequestID)
                                 join tl in allTitles on acc.AccepterTitleID equals tl.TitleID
                                 select new { tl.TitleName, acc.IsCancel })
            {
                if (item.IsCancel)
                {
                    request.CanceledTitle = item.TitleName;
                }
                else if (string.IsNullOrEmpty(request.AcceptedTitles))
                {
                    request.AcceptedTitles = item.TitleName;
                }
                else
                {
                    request.AcceptedTitles = request.AcceptedTitles + "-" + item.TitleName;
                }
            }

            List<KeyValuePair<long, int>> waitForTitles = t5DBInstance.RequestDA.GetRequestAccepters(
                personnelSystem.DB.UserInfos.GetAllUserInfoTitles(),
                personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(),
                reqT5.RequestID);

            string currentWaitingTitle = "";
            if (waitForTitles.Count > 0)
            {
                Personnel.Model.TitleChart matchedTitle = allTitles.FirstOrDefault(t => t.TitleID == waitForTitles[0].Value);
                if (matchedTitle != null)
                {
                    currentWaitingTitle = matchedTitle.TitleName;
                    request.WaitForTitle = currentWaitingTitle;
                    name = currentWaitingTitle;
                }
            }

            if (request.Descriptions != null && request.Descriptions.Contains("بازگشت داده شده"))
            {
                request.WaitForTitle = currentWaitingTitle + " بازگشت داده شده توسط " + currentWaitingTitle + " " + DateConverter.ToHijriDate(DateTime.Now);
            }

            return request;
        }

        [HttpPost]
        public JsonResult GetRequests(string userName, string password, int nMonthAgo)
        {
            ShowRequestModel showRequestModel = new ShowRequestModel();

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                long? badge = adapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    PersianCalendar pc = new PersianCalendar();
                    DateTime monthStart = pc.AddMonths(
                        pc.ToDateTime(pc.GetYear(DateTime.Now), pc.GetMonth(DateTime.Now), 1, 0, 0, 0, 0),
                        -nMonthAgo);
                    DateTime monthEnd = pc.AddMonths(monthStart, 1);
                    var allTitleNodes = adapter.GetAllTitleNodes();
                    foreach (var dto in adapter.GetEmployeeRequestsInPeriod(badge.Value, monthStart, monthEnd))
                    {
                        showRequestModel.Requests.Add(BuildTestRequestViewModel(dto, allTitleNodes));
                    }
                }
                return Json(showRequestModel.Requests);
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            long badgeNumber = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);

            if (badgeNumber > 0)
            {
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersianCalendar persianCalendar = new PersianCalendar();
                DateTime monthStart = persianCalendar.AddMonths(
                    persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), persianCalendar.GetMonth(DateTime.Now), 1, 0, 0, 0, 0),
                    -nMonthAgo);
                DateTime monthEnd = persianCalendar.AddMonths(monthStart, 1);
                List<Personnel.Model.TitleChart> allTitles = personnelSystem.DB.TitlesCharts.GetAll();

                foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(badgeNumber, monthStart, monthEnd))
                {
                    showRequestModel.Requests.Add(BuildRequestViewModel(t5DBInstance, personnelSystem, allTitles, item));
                }
            }
            return Json(showRequestModel.Requests);
        }
    }
}
