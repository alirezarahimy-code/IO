using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
    public class LocationReportController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index()
        {
            LocationReportModel locationReportModel = new LocationReportModel
            {
                IsSelf = true
            };
            AddPersonnelToViewBag();
            return View(locationReportModel);
        }

        [HttpPost]
        public JsonResult GetImage(string userName, string password, int id, int index)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                long? badge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    return Json(new { Image = Convert.ToBase64String(new byte[] { 137, 80, 78, 71 }) });
                }
                return null;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            if (DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true) > 0)
            {
                byte[] img = t5DBInstance.UserPositionDataDA.GetImage(id, index);
                return Json(new
                {
                    Image = Convert.ToBase64String(img ?? new byte[0])
                });
            }
            return null;
        }

        [HttpPost]
        public JsonResult GetReport(string userName, string password, string fromDateHijri, string toDateHijri, List<long> selectedBadgeNumbers, bool isSelf)
        {
            bool hasError = false;
            string message = "";

            long loggedBadge = -1L;
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                loggedBadge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password) ?? -1L;
            }
            else
            {
                loggedBadge = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
            }

            if (loggedBadge > 0)
            {
                List<long> badgeList = new List<long>();
                if (isSelf)
                {
                    badgeList.Add(loggedBadge);
                }
                else if (selectedBadgeNumbers != null)
                {
                    foreach (long selectedBadgeNumber in selectedBadgeNumbers)
                    {
                        badgeList.Add(selectedBadgeNumber);
                    }
                }

                DateTime? fromDate = DailyLeaveController.ParseHijriDate(fromDateHijri);
                DateTime? toDate = DailyLeaveController.ParseHijriDate(toDateHijri);
                if (!fromDate.HasValue || !toDate.HasValue || toDate.Value < fromDate.Value)
                {
                    // Fixed legacy bug: legacy set message = PersianString.BadDateFormat without setting flag = true!
                    hasError = true;
                    message = PersianString.BadDateFormat;
                }

                if (hasError)
                {
                    return Json(new { Message = message });
                }

                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    var testPositions = new List<UserPosition>();
                    foreach (long b in badgeList)
                    {
                        var emp = DBInitializer.TestBackendAdapter.GetEmployeeByBadge(b);
                        testPositions.Add(new UserPosition
                        {
                            ID = 1,
                            BadgeNumber = b,
                            UserName = emp != null ? emp.FullName : b.ToString(),
                            PositionName = "گزارش موقعیت مکانی",
                            CreateTime = fromDate.Value,
                            Steps = new List<UserPositionStep>()
                        });
                    }
                    JsonResult testRes = Json(testPositions);
                    testRes.MaxJsonLength = 2147483644;
                    return testRes;
                }

                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                List<UserPosition> positions = new List<UserPosition>();
                foreach (Position item in t5DBInstance.PositionDA.GetAll())
                {
                    positions.AddRange(t5DBInstance.UserPositionDataDA.GetAll(badgeList, fromDate.Value, toDate.Value.AddDays(1.0), item.PositionID));
                }

                List<Personnel.Model.UserInfo> allUsers = personnelSystem.DB.UserInfos.GetAll(badgeList);
                foreach (UserPosition item2 in positions)
                {
                    if (item2.Steps != null)
                    {
                        foreach (UserPositionStep step in item2.Steps)
                        {
                            if (step.ImageData != null && step.ImageData.Length > 1)
                            {
                                step.ImageData = new byte[0];
                                step.Image = true;
                            }
                            else
                            {
                                step.Image = false;
                            }
                        }
                    }

                    Personnel.Model.UserInfo matchedUser = allUsers.FirstOrDefault(u => u.BadgeNumber == item2.BadgeNumber);
                    item2.UserName = matchedUser != null ? matchedUser.ToString() : item2.BadgeNumber.ToString();
                }

                JsonResult jsonResult = Json(positions);
                jsonResult.MaxJsonLength = 2147483644;
                return jsonResult;
            }
            return null;
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Report(LocationReportModel model)
        {
            if (model == null)
            {
                model = new LocationReportModel { IsSelf = true };
            }

            AddPersonnelToViewBag();
            bool hasError = false;
            string errorMessage = "";
            List<long> badgeList = new List<long>();

            if (model.IsSelf)
            {
                badgeList.Add(GlobalHelper.GetLoggedInBadgeNumber(HttpContext));
            }
            else if (model.SelectedBadgeNumbers != null)
            {
                foreach (string selectedBadgeNumber in model.SelectedBadgeNumbers)
                {
                    if (long.TryParse(selectedBadgeNumber, out long parsedBadge))
                    {
                        badgeList.Add(parsedBadge);
                    }
                }
            }

            DateTime? fromDate = DailyLeaveController.ParseHijriDate(model.FromDateHijri);
            DateTime? toDate = DailyLeaveController.ParseHijriDate(model.ToDateHijri);
            if (!fromDate.HasValue || !toDate.HasValue || toDate.Value < fromDate.Value)
            {
                hasError = true;
                errorMessage = PersianString.BadDateFormat;
            }

            if (hasError)
            {
                model.IsSelf = true;
                model.ErrorMessage = errorMessage;
            }
            else if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                model.data = new List<UserPosition>();
                foreach (long b in badgeList)
                {
                    var emp = DBInitializer.TestBackendAdapter.GetEmployeeByBadge(b);
                    model.data.Add(new UserPosition
                    {
                        ID = 1,
                        BadgeNumber = b,
                        UserName = emp != null ? emp.FullName : b.ToString(),
                        PositionName = "موقعیت میدانی",
                        CreateTime = fromDate.Value,
                        Steps = new List<UserPositionStep>()
                    });
                }
            }
            else
            {
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                model.data = t5DBInstance.UserPositionDataDA.GetAll(badgeList, fromDate.Value, toDate.Value.AddDays(1.0), model.ReportID);
                List<Personnel.Model.UserInfo> allUsers = DBInitializer.GetPersonnelSystem().DB.UserInfos.GetAll(badgeList);
                foreach (UserPosition item in model.data)
                {
                    Personnel.Model.UserInfo matchedUser = allUsers.FirstOrDefault(u => u.BadgeNumber == item.BadgeNumber);
                    item.UserName = matchedUser != null ? matchedUser.ToString() : item.BadgeNumber.ToString();
                }
            }

            return View("Index", model);
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public FileResult ShowPhoto(int id, int index)
        {
            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                int titleId = DBInitializer.TestBackendAdapter.GetCurrentTitleIdForEmployee(loggedInBadgeNumber);
                if (titleId > 0)
                {
                    return File(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, "image/png");
                }
                return null;
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            byte[] image = t5DBInstance.UserPositionDataDA.GetImage(id, index);
            int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(loggedInBadgeNumber);
            List<long> deputyBadges = t5DBInstance.PersonnelDeputyDA.IsDeputy(loggedInBadgeNumber);
            List<int> deputyTitleIds = new List<int>();

            foreach (long item in deputyBadges)
            {
                int userCurrentTitleID2 = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
                if (userCurrentTitleID2 != -1)
                {
                    deputyTitleIds.Add(userCurrentTitleID2);
                }
            }

            if ((userCurrentTitleID != -1 || deputyTitleIds.Count > 0) && image != null)
            {
                return File(image, "image/png");
            }
            return null;
        }

        private void AddPersonnelToViewBag()
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                int titleId = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);
                var subTitles = adapter.GetSubordinateTitleIds(titleId);
                ViewBag.Personnel = adapter.GetEmployeesByTitleIds(subTitles)
                    .Select(u => new PersonnelModel(u.BadgeNumber, u.FirstName, u.LastName))
                    .ToList();
                ViewBag.Positions = new List<SelectListItem>
                {
                    new SelectListItem { Text = "موقعیت میدانی", Value = "1", Selected = true }
                };
                return;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<PersonnelModel> list = new List<PersonnelModel>();
            int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);
            List<Personnel.Model.TitleChart> childTitles = new List<Personnel.Model.TitleChart>();

            if (loggedInUserInfoTitleID != -1)
            {
                childTitles = personnelSystem.DB.TitlesCharts.GetAll(loggedInUserInfoTitleID);
            }

            foreach (Personnel.Model.UserInfo item in personnelSystem.DB.UserInfos.GetAllByTitle(
                childTitles.Select(t => t.TitleID).ToList()))
            {
                if (item.DeptID > -1)
                {
                    list.Add(new PersonnelModel(item.BadgeNumber, item.FirstName, item.LastName));
                }
            }
            ViewBag.Personnel = list;

            List<SelectListItem> list2 = new List<SelectListItem>();
            foreach (Position item2 in t5DBInstance.PositionDA.GetAll())
            {
                list2.Add(new SelectListItem
                {
                    Text = item2.PositionName,
                    Value = item2.PositionID.ToString()
                });
            }
            ViewBag.Positions = list2;
        }
    }
}
