using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using T5V3.DB;
using T5V3.Model;

namespace MyTime3.Controllers
{
    public class ShowUsersRequestController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin")]
        public ActionResult Index(long badge = -1L)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                ViewBag.LeaveTypes = adapter.GetAllLeaveTypes()
                    .Select(lt => new LeaveType
                    {
                        LeaveTypeID = lt.LeaveTypeId,
                        LeaveName = lt.LeaveName,
                        Symbol = lt.Symbol
                    }).ToList();
                ViewBag.LeaveDescriptions = new List<LeaveDescription>();
                List<PersonnelModel> testList = AddPersonnelToViewBag(HttpContext);
                if (testList.Count > 0)
                {
                    testList.RemoveAt(0);
                }
                ViewBag.Personnel = testList;
                return View(badge);
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            // Fixed legacy bug: guard against null when GetLoggedInUserInfo returns null for admin account
            Personnel.Model.UserInfo loggedInUser = GlobalHelper.GetLoggedInUserInfo(HttpContext);
            List<int> validIDs = loggedInUser != null
                ? t5DBInstance.EmpLeavesDA.GetAll(loggedInUser.EmpTypeID)
                : t5DBInstance.LeaveTypeDA.GetAll().Select(l => l.LeaveTypeID).ToList();

            ViewBag.LeaveTypes = t5DBInstance.LeaveTypeDA.GetAll()
                .Where(l => validIDs.Contains(l.LeaveTypeID))
                .ToList();
            ViewBag.LeaveDescriptions = t5DBInstance.LeaveDescriptionDA.GetAll();

            List<PersonnelModel> list = AddPersonnelToViewBag(HttpContext);
            if (list.Count > 0)
            {
                list.RemoveAt(0);
            }
            ViewBag.Personnel = list;
            return View(badge);
        }

        public static List<PersonnelModel> AddPersonnelToViewBag(HttpContextBase httpContext)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                return RequestController.AddPersonnelToViewBag(httpContext);
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<PersonnelModel> list = new List<PersonnelModel>();
            int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(httpContext);
            List<Personnel.Model.TitleChart> childTitles = new List<Personnel.Model.TitleChart>();
            if (loggedInUserInfoTitleID != -1)
            {
                childTitles = personnelSystem.DB.TitlesCharts.GetAll(loggedInUserInfoTitleID);
            }

            foreach (Personnel.Model.UserInfo current in personnelSystem.DB.UserInfos.GetAllByTitle(
                childTitles.Select(t => t.TitleID).ToList()))
            {
                list.Add(new PersonnelModel(current.BadgeNumber, current.FirstName, current.LastName));
            }

            foreach (long item in t5DBInstance.PersonnelDeputyDA.IsDeputy(GlobalHelper.GetLoggedInBadgeNumber(httpContext)))
            {
                int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
                if (userCurrentTitleID != -1)
                {
                    childTitles = personnelSystem.DB.TitlesCharts.GetAll(userCurrentTitleID);
                    foreach (Personnel.Model.UserInfo current3 in personnelSystem.DB.UserInfos.GetAllByTitle(
                        childTitles.Select(t => t.TitleID).ToList()))
                    {
                        if (!list.Any(p => p.BadgeNumber == current3.BadgeNumber))
                        {
                            list.Add(new PersonnelModel(current3.BadgeNumber, current3.FirstName, current3.LastName));
                        }
                    }
                }
            }

            list.Insert(0, new PersonnelModel(GlobalHelper.GetLoggedInBadgeNumber(httpContext), PersianString.Myself, ""));
            return list;
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ContentResult GetForUser()
        {
            ContentResult contentResult = Content("");
            contentResult.ContentType = "text/html; charset=UTF-8";

            string rawBadge = (Request != null && Request.Params != null ? Request.Params["badg"] : null)
                ?? (System.Web.HttpContext.Current != null && System.Web.HttpContext.Current.Request != null
                    ? System.Web.HttpContext.Current.Request.Params["badg"]
                    : null);

            if (long.TryParse(rawBadge, out long badg))
            {
                getStatus(badg);
                if (TitleChartClass.user != null)
                {
                    string fn = HttpUtility.HtmlEncode(TitleChartClass.user.FirstName ?? "");
                    string ln = HttpUtility.HtmlEncode(TitleChartClass.user.LastName ?? "");
                    string tc = HttpUtility.HtmlEncode(TitleChartClass.user.TitleChartName ?? "-");
                    string utc = HttpUtility.HtmlEncode(TitleChartClass.user.UpperTitleChartName ?? "-");
                    contentResult = Content(
                        "<div><div class='card-header text-right'><h3>مشخصات کاربر</h3></div><div class='card'>" +
                        "<table class='table table-hover table-bordered table-responsive table-striped'>" +
                        "<thead><tr><td>نام</td><td>نام خانوادگی</td><td>سمت</td><td>سمت بالا دست</td></tr></thead>" +
                        "<tbody><tr><td>" + fn + "</td><td>" + ln + "</td><td>" + tc + "</td><td>" + utc + "</td></tr></tbody>" +
                        "</table></div></div>");
                    contentResult.ContentType = "text/html; charset=UTF-8";
                }
            }
            return contentResult;
        }

        public void getStatus(long badg)
        {
            int userCurrentTitleID = (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                ? DBInitializer.TestBackendAdapter.GetCurrentTitleIdForEmployee(badg)
                : DBInitializer.GetPersonnelSystem().DB.UserInfos.GetUserCurrentTitleID(badg);
            TitleChartClass.user = TitleChartClass.GetUserListInfoById(badg).FirstOrDefault();
            if (TitleChartClass.user == null)
            {
                return;
            }

            TitleChartModel titleChartById = TitleChartClass.GetTitleChartById(userCurrentTitleID);
            if (titleChartById != null)
            {
                if (titleChartById.UpperTitleID != -1)
                {
                    TitleChartModel upperChart = TitleChartClass.GetTitleChartById(titleChartById.UpperTitleID);
                    if (upperChart != null)
                    {
                        TitleChartClass.user.UpperTitleChartName = upperChart.TitleName;
                    }
                }
                TitleChartClass.user.TitleChartName = titleChartById.TitleName;
            }
        }
    }
}
