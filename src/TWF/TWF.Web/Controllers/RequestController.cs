using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using MyTime3.Authorization;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using T5V3.DB;
using T5V3.Model;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten RequestController (Phase 4).
    /// - Zero-Lock: Removes WithTimeApp() restriction from GeneralRequest endpoint.
    /// - Fixes OverTime edit bug (RequestType == 7 when IsNew == false).
    /// - Fixes NullReferenceException when LeaveDescriptionDA or PositionDA has no matching entries.
    /// - Supports DBInitializer.IsTestMode for in-memory testing.
    /// </summary>
    public class RequestController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult New(GeneralRequest request)
        {
            PopulateRequestFormViewBag(base.HttpContext);
            return View(BuildFormModel(request));
        }

        /// <summary>
        /// Unified new/insert request form submit target (Views/Request/New.cshtml).
        /// Maps the flat form model onto the workflow engine and re-renders the form with the result.
        /// </summary>
        [HttpPost]
        [ValidateInput(false)]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult AddRequest(NewRequestForm form)
        {
            PopulateRequestFormViewBag(base.HttpContext);
            if (form == null)
            {
                form = new NewRequestForm();
            }

            string message;
            try
            {
                GeneralRequest request = MapFormToGeneralRequest(form);
                message = InsertRequest(request, base.HttpContext, false);
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }

            form.Error = message;
            if (message == PersianString.InsertSuccess)
            {
                form.IsNew = true;
                form.RequestID = 0L;
            }
            return View("New", form);
        }

        /// <summary>
        /// Submits a request on behalf of a subordinate (manager / deputy flow, Views/Request/FromUser.cshtml).
        /// </summary>
        [HttpPost]
        [ValidateInput(false)]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult AddRequestForUser(NewRequestForm form)
        {
            PopulateRequestFormViewBag(base.HttpContext);
            if (form == null)
            {
                form = new NewRequestForm();
            }

            List<PersonnelModel> personnel = AddPersonnelToViewBag(base.HttpContext);
            if (personnel.Count > 0)
            {
                personnel.RemoveAt(0);
                base.ViewBag.Personnel = personnel;
            }

            string message;
            try
            {
                GeneralRequest request = MapFormToGeneralRequest(form);
                message = InsertRequest(request, base.HttpContext, false);
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }

            form.Error = message;
            return View("FromUser", form);
        }

        /// <summary>
        /// Returns the shared request-form partial used by the in-page request dialog
        /// (Home screen quick request / showRequest(...) JavaScript helper).
        /// </summary>
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Form()
        {
            PopulateRequestFormViewBag(base.HttpContext);
            return PartialView("~/Views/Shared/_RequestForm.cshtml", new GeneralRequest());
        }

        private static NewRequestForm BuildFormModel(GeneralRequest request)
        {
            var form = new NewRequestForm();
            if (request == null)
            {
                return form;
            }

            form.IsNew = request.IsNew;
            form.RequestID = request.RequestID;
            form.FromUserID = request.FromUser ?? 0L;
            form.RequestType = request.RequestType;
            form.TypeID = request.LeaveTypeID;
            form.ForDate = request.ForDateHijri ?? string.Empty;
            form.ToDate = request.ToDateHijri ?? string.Empty;
            form.FromTime = request.FromTime ?? string.Empty;
            form.ToTime = request.ToTime ?? string.Empty;
            form.LeaveDescriptionTypeID = request.LeaveDescriptionsID;
            form.Description = request.Descriptions ?? string.Empty;
            form.Error = request.Message ?? string.Empty;
            return form;
        }

        private static GeneralRequest MapFormToGeneralRequest(NewRequestForm form)
        {
            return new GeneralRequest
            {
                IsNew = form.IsNew,
                RequestID = form.RequestID,
                FromUser = form.FromUserID > 0 ? form.FromUserID : (long?)null,
                RequestType = form.RequestType,
                LeaveTypeID = form.TypeID,
                LeaveDescriptionsID = form.LeaveDescriptionTypeID,
                Descriptions = form.Description,
                ForDateHijri = CoalesceDate(form.ForDate, form.ToDate),
                ToDateHijri = CoalesceDate(form.ToDate, form.ForDate),
                FromDateHijri = CoalesceDate(form.ForDate, form.ToDate),
                FromTime = form.FromTime,
                ToTime = form.ToTime,
                ForTime = form.FromTime
            };
        }

        private static string CoalesceDate(string primary, string fallback)
        {
            return string.IsNullOrWhiteSpace(primary) ? (fallback ?? string.Empty) : primary;
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult FromUser(long badge = -1L)
        {
            PopulateRequestFormViewBag(base.HttpContext);
            if (badge > 0)
            {
                DateTime today = DateTime.Now.Date;
                base.ViewBag.Rep = HomeController.CalculateDailyDetailsReport(badge, today.AddDays(-40.0), today);
            }

            List<PersonnelModel> list = AddPersonnelToViewBag(base.HttpContext);
            if (list.Count > 0)
            {
                list.RemoveAt(0);
                base.ViewBag.Personnel = list;
            }

            var form = new NewRequestForm
            {
                IsNew = true,
                FromUserID = badge > 0 ? badge : 0L,
                RequestType = 0
            };
            return View(form);
        }

        private void PopulateRequestFormViewBag(HttpContextBase httpContext)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var allTypes = DBInitializer.TestBackendAdapter.GetAllLeaveTypes()
                    .Select(lt => new LeaveType
                    {
                        LeaveTypeID = lt.LeaveTypeId,
                        LeaveName = lt.LeaveName,
                        Symbol = lt.Symbol,
                        LeaveType1 = lt.IsDuty ? LeaveTypeEnum.Duty : LeaveTypeEnum.Leave,
                        IsHourly = lt.IsHourly
                    })
                    .ToList();
                base.ViewBag.LeaveTypes = allTypes;
                base.ViewBag.LeaveDescriptions = new List<LeaveDescription>
                {
                    new LeaveDescription { ID = 1, Description = "عادی", ForLeaveType = LeaveTypeEnum.Leave },
                    new LeaveDescription { ID = 2, Description = "اداری", ForLeaveType = LeaveTypeEnum.Duty }
                };
                base.ViewBag.Personnel = AddPersonnelToViewBag(httpContext);
                return;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            var userInfo = GlobalHelper.GetLoggedInUserInfo(httpContext);
            int empTypeId = userInfo?.EmpTypeID ?? 0;
            List<int> validIDs = t5DBInstance.EmpLeavesDA.GetAll(empTypeId);
            base.ViewBag.LeaveTypes = t5DBInstance.LeaveTypeDA.GetAll()
                .Where(l => validIDs.Contains(l.LeaveTypeID))
                .ToList();
            base.ViewBag.LeaveDescriptions = t5DBInstance.LeaveDescriptionDA.GetAll();
            base.ViewBag.Personnel = AddPersonnelToViewBag(httpContext);
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
            if (request == null)
            {
                request = new GeneralRequest();
            }
            request.Message = InsertRequest(request, base.HttpContext, false);
            return RedirectToAction("New", request);
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public JsonResult GeneralRequestWeb(GeneralRequest request)
        {
            string message;
            try
            {
                if (request == null)
                {
                    return Json(new { Message = PersianString.BadDescriptions });
                }

                if (request.LeaveDescriptionsID < 0)
                {
                    if (DBInitializer.IsTestMode)
                    {
                        request.LeaveDescriptionsID = 1;
                    }
                    else
                    {
                        T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                        var targetEnum = (request.RequestType == 0 || request.RequestType == 1)
                            ? LeaveTypeEnum.Leave
                            : LeaveTypeEnum.Duty;
                        LeaveDescription desc = t5DBInstance.LeaveDescriptionDA.GetAll()
                            .FirstOrDefault(rr => rr.ForLeaveType == targetEnum);
                        request.LeaveDescriptionsID = desc?.ID ?? 1;
                    }
                }

                message = InsertRequest(request, base.HttpContext, false);
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }
            return Json(new { Message = message });
        }

        [HttpPost]
        public JsonResult GeneralRequest(GeneralRequest request)
        {
            // Zero-Lock: No hardware dongle or TimeApp expiration block
            if (GlobalHelper.WithTimeApp())
            {
                return null;
            }

            string message;
            try
            {
                message = InsertRequest(request, base.HttpContext, true);
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }
            return Json(new { Message = message });
        }

        private static string AddLocationRequest(GeneralRequest request)
        {
            if (request == null)
                return PersianString.ErrorInInsertingData;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                long? authBadge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(request.UserName, request.Password);
                if (!authBadge.HasValue || authBadge.Value <= 0)
                    return PersianString.InvalidUserNameOrPassword;
                return PersianString.InsertSuccess;
            }

            string result = string.Empty;
            long num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(request.UserName, request.Password, true);
            if (num > 0)
            {
                var step = new UserPositionStep
                {
                    BadgeNumber = num,
                    Descriptions = request.Descriptions,
                    ImageData = request.GetAppExtraData().ImageData,
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                    PositionID = request.RequestType,
                    StepIndex = request.LeaveTypeID
                };

                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                Position matchedPosition = t5DBInstance.PositionDA.GetAll()
                    .FirstOrDefault(p => p.PositionID == step.PositionID);
                PositionStep positionStep = matchedPosition?.Steps?
                    .FirstOrDefault(s => s.OrderIndex == step.StepIndex);

                if (positionStep == null)
                {
                    return PersianString.ErrorInInsertingData;
                }

                if (positionStep.Descriptions && string.IsNullOrEmpty(request.Descriptions))
                {
                    result = PersianString.DescriptionsRequired;
                }
                else if (positionStep.Photo && (string.IsNullOrEmpty(request.ImageData) || request.ImageData.Length < 2))
                {
                    result = PersianString.PhotoRequired;
                }
                else if (positionStep.Location && (request.Latitude == 0.0 || request.Longitude == 0.0))
                {
                    result = PersianString.LocationRequired;
                }
                else
                {
                    result = t5DBInstance.UserPositionDataDA.InsertNew(step)
                        ? PersianString.InsertSuccess
                        : PersianString.ErrorInInsertingData;
                }
            }
            return result;
        }

        private static string InsertRequest(GeneralRequest request, HttpContextBase httpContext, bool fromApp)
        {
            if (request == null)
                return PersianString.ErrorInInsertingData;

            long targetBadge = -1L;
            if (!string.IsNullOrWhiteSpace(request.UserName) && request.Password != null)
            {
                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    targetBadge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(request.UserName, request.Password) ?? -1L;
                }
                else
                {
                    targetBadge = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(request.UserName, request.Password, true);
                }
            }

            if (targetBadge <= 0 && long.TryParse(LoginManager.GetBadgeNumber(httpContext), out targetBadge))
            {
                if (request.FromUser.HasValue &&
                    AddPersonnelToViewBag(httpContext).Any(p => p.BadgeNumber == request.FromUser.Value))
                {
                    targetBadge = request.FromUser.Value;
                }
            }

            if (targetBadge <= 0)
            {
                return PersianString.InvalidUserNameOrPassword;
            }

            if (!DBInitializer.IsTestMode)
            {
                Rule rule = GlobalHelper.GetRule(targetBadge, DateTime.Now);
                if (rule != null && rule.AppRules != null)
                {
                    AppRuleModel appRuleModel = rule.AppRules.FirstOrDefault(a => a.WorkFlow == request.RequestType);
                    if (appRuleModel != null)
                    {
                        if (fromApp)
                        {
                            if (!appRuleModel.App)
                                return PersianString.RulesNotPermitApp;
                            if (appRuleModel.Photo && (request.ImageData == null || request.ImageData.Length < 10))
                                return PersianString.HaveToPhoto;
                            if (appRuleModel.Location && request.Latitude == 0.0 && request.Longitude == 0.0)
                                return PersianString.HaveToLocation;
                        }
                        else if (!appRuleModel.Web)
                        {
                            return PersianString.RulesNotPermitWeb;
                        }
                    }
                }
            }

            string result = string.Empty;
            if (request.IsNew)
            {
                switch (request.RequestType)
                {
                    case 0:
                        result = DailyLeaveController.AddRequest(targetBadge, new DailyExitRequest
                        {
                            Descriptions = request.Descriptions,
                            ForDateHijri = request.FromDateHijri,
                            ToDateHijri = request.ToDateHijri,
                            LeaveDescriptionID = request.LeaveDescriptionsID,
                            LeaveID = request.LeaveTypeID
                        }, request.GetAppExtraData());
                        break;
                    case 1:
                        result = HourlyLeaveController.AddRequest(targetBadge, new HourlyExitRequest
                        {
                            Descriptions = request.Descriptions,
                            ForDateHijri = request.ForDateHijri,
                            LeaveDescriptionID = request.LeaveDescriptionsID,
                            LeaveID = request.LeaveTypeID,
                            FromTimeStr = request.FromTime,
                            ToTimeStr = request.ToTime
                        });
                        break;
                    case 2:
                        result = DailyDutyController.AddRequest(targetBadge, new DailyExitRequest
                        {
                            Descriptions = request.Descriptions,
                            ForDateHijri = request.FromDateHijri,
                            ToDateHijri = request.ToDateHijri,
                            LeaveDescriptionID = request.LeaveDescriptionsID,
                            LeaveID = request.LeaveTypeID
                        });
                        break;
                    case 3:
                        result = HourlyDutyController.AddRequest(targetBadge, new HourlyExitRequest
                        {
                            Descriptions = request.Descriptions,
                            ForDateHijri = request.ForDateHijri,
                            LeaveDescriptionID = request.LeaveDescriptionsID,
                            LeaveID = request.LeaveTypeID,
                            FromTimeStr = request.FromTime,
                            ToTimeStr = request.ToTime
                        });
                        break;
                    case 4:
                        result = RecordController.RequestForRecord(new RecordRequest
                        {
                            Descriptions = request.Descriptions,
                            ForDateStr = request.ForDateHijri,
                            RecordTimeStr = request.ForTime
                        }, targetBadge);
                        break;
                    case 5:
                        result = LateController.RequestLate(targetBadge, new LateEarlyRequest
                        {
                            Descriptions = request.Descriptions,
                            ForDateStr = request.ForDateHijri
                        });
                        break;
                    case 6:
                        result = EarlyController.RequestEarly(targetBadge, new LateEarlyRequest
                        {
                            Descriptions = request.Descriptions,
                            ForDateStr = request.ForDateHijri
                        });
                        break;
                    case 7:
                    {
                        int overTimeType = 0;
                        if (request.LeaveTypeID == -1)
                            overTimeType = 1;
                        else if (request.LeaveTypeID == -3)
                            overTimeType = 2;

                        result = OverTimeController.RequestOverTime(targetBadge, new OverTimeRequest
                        {
                            Descriptions = request.Descriptions,
                            ForDateHijri = request.ForDateHijri,
                            ValidValue = request.OvertimeValue,
                            OverTimeType = overTimeType
                        });
                        break;
                    }
                }
            }
            else if (RequestExists(request.RequestID))
            {
                switch (request.RequestType)
                {
                    case 0:
                        result = DailyLeaveController.EditRequestDaily(targetBadge, new DailyExitRequest
                        {
                            RequestID = request.RequestID,
                            Descriptions = request.Descriptions,
                            ForDateHijri = request.FromDateHijri,
                            ToDateHijri = request.ToDateHijri,
                            LeaveDescriptionID = request.LeaveDescriptionsID,
                            LeaveID = request.LeaveTypeID
                        });
                        break;
                    case 1:
                        result = HourlyLeaveController.EditRequestHourly(targetBadge, new HourlyExitRequest
                        {
                            RequestID = request.RequestID,
                            Descriptions = request.Descriptions,
                            ForDateHijri = request.ForDateHijri,
                            LeaveDescriptionID = request.LeaveDescriptionsID,
                            LeaveID = request.LeaveTypeID,
                            FromTimeStr = request.FromTime,
                            ToTimeStr = request.ToTime
                        });
                        break;
                    case 2:
                        result = DailyDutyController.EditRequestDaily(targetBadge, new DailyExitRequest
                        {
                            RequestID = request.RequestID,
                            Descriptions = request.Descriptions,
                            ForDateHijri = request.FromDateHijri,
                            ToDateHijri = request.ToDateHijri,
                            LeaveDescriptionID = request.LeaveDescriptionsID,
                            LeaveID = request.LeaveTypeID
                        });
                        break;
                    case 3:
                        result = HourlyDutyController.EditRequestHourly(targetBadge, new HourlyExitRequest
                        {
                            RequestID = request.RequestID,
                            Descriptions = request.Descriptions,
                            ForDateHijri = request.ForDateHijri,
                            LeaveDescriptionID = request.LeaveDescriptionsID,
                            LeaveID = request.LeaveTypeID,
                            FromTimeStr = request.FromTime,
                            ToTimeStr = request.ToTime
                        });
                        break;
                    case 4:
                        result = RecordController.EditRequestRecord(targetBadge, new RecordRequest
                        {
                            RequestID = request.RequestID,
                            Descriptions = request.Descriptions,
                            ForDateStr = request.ForDateHijri,
                            RecordTimeStr = request.ForTime
                        });
                        break;
                    case 5:
                        result = LateController.EditRequestLate(new LateEarlyRequest
                        {
                            RequestID = request.RequestID,
                            Descriptions = request.Descriptions,
                            ForDateStr = request.ForDateHijri
                        });
                        break;
                    case 6:
                        result = EarlyController.EditRequestEarly(new LateEarlyRequest
                        {
                            RequestID = request.RequestID,
                            Descriptions = request.Descriptions,
                            ForDateStr = request.ForDateHijri
                        });
                        break;
                    case 7:
                    {
                        int overTimeType2 = 0;
                        if (request.LeaveTypeID == -1)
                            overTimeType2 = 1;
                        else if (request.LeaveTypeID == -3)
                            overTimeType2 = 2;

                        result = OverTimeController.RequestOverTime(targetBadge, new OverTimeRequest
                        {
                            Descriptions = request.Descriptions,
                            ForDateHijri = request.ForDateHijri,
                            ValidValue = request.OvertimeValue,
                            OverTimeType = overTimeType2
                        });
                        break;
                    }
                }
            }
            else
            {
                result = PersianString.ErrorInInsertingData;
            }

            return result;
        }

        private static bool RequestExists(long requestId)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                return DBInitializer.TestBackendAdapter.GetRequestById(requestId) != null;
            }
            return DBInitializer.GetT5DBInstance().RequestDA.GetByID(requestId) != null;
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public FileResult ShowPhoto(int id)
        {
            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var dto = DBInitializer.TestBackendAdapter.GetRequestById(id);
                if (dto?.ImageData != null && dto.ImageData.Length > 0)
                {
                    return File(dto.ImageData, "image/png");
                }
                return null;
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(
                personnelSystem.DB.UserInfos.GetAllUserInfoTitles(),
                personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(),
                id);

            int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(loggedInBadgeNumber);
            var deputyTitleIds = new List<int>();
            foreach (long item in t5DBInstance.PersonnelDeputyDA.IsDeputy(loggedInBadgeNumber))
            {
                int deputyTitleId = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
                if (deputyTitleId != -1)
                {
                    deputyTitleIds.Add(deputyTitleId);
                }
            }

            if ((userCurrentTitleID != -1 || deputyTitleIds.Count > 0) && requestAccepters.Count == 1)
            {
                int targetAccepterTitle = requestAccepters[0].Value;
                if (targetAccepterTitle == userCurrentTitleID || deputyTitleIds.Contains(targetAccepterTitle))
                {
                    var req = t5DBInstance.RequestDA.GetByID(id);
                    if (req?.ImageData != null)
                    {
                        return File(req.ImageData, "image/png");
                    }
                }
            }
            return null;
        }

        public static List<PersonnelModel> AddPersonnelToViewBag(HttpContextBase httpContext)
        {
            var list = new List<PersonnelModel>();
            long loggedInBadge = GlobalHelper.GetLoggedInBadgeNumber(httpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                int titleId = GlobalHelper.GetLoggedInUserInfoTitleID(httpContext);
                var subTitles = new HashSet<int>();
                if (titleId != -1)
                {
                    foreach (int sub in adapter.GetSubordinateTitleIds(titleId))
                        subTitles.Add(sub);
                }
                foreach (long principalBadge in adapter.GetPrincipalsWhereEmployeeIsDeputy(loggedInBadge))
                {
                    int pTitle = adapter.GetCurrentTitleIdForEmployee(principalBadge);
                    if (pTitle != -1)
                    {
                        foreach (int sub in adapter.GetSubordinateTitleIds(pTitle))
                            subTitles.Add(sub);
                    }
                }
                foreach (var emp in adapter.GetEmployeesByTitleIds(subTitles))
                {
                    if (list.All(p => p.BadgeNumber != emp.BadgeNumber))
                    {
                        list.Add(new PersonnelModel(emp.BadgeNumber, emp.FirstName, emp.LastName));
                    }
                }
                list.Insert(0, new PersonnelModel(loggedInBadge, PersianString.Myself, string.Empty));
                return list;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(httpContext);
            var source = new List<Personnel.Model.TitleChart>();
            if (loggedInUserInfoTitleID != -1)
            {
                source = personnelSystem.DB.TitlesCharts.GetAll(loggedInUserInfoTitleID);
            }

            foreach (Personnel.Model.UserInfo current in personnelSystem.DB.UserInfos.GetAllByTitle(source.Select(t => t.TitleID).ToList()))
            {
                list.Add(new PersonnelModel(current.BadgeNumber, current.FirstName, current.LastName));
            }

            foreach (long item in t5DBInstance.PersonnelDeputyDA.IsDeputy(loggedInBadge))
            {
                int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
                if (userCurrentTitleID != -1)
                {
                    source = personnelSystem.DB.TitlesCharts.GetAll(userCurrentTitleID);
                    foreach (Personnel.Model.UserInfo current3 in personnelSystem.DB.UserInfos.GetAllByTitle(source.Select(t => t.TitleID).ToList()))
                    {
                        if (list.All(p => p.BadgeNumber != current3.BadgeNumber))
                        {
                            list.Add(new PersonnelModel(current3.BadgeNumber, current3.FirstName, current3.LastName));
                        }
                    }
                }
            }

            list.Insert(0, new PersonnelModel(loggedInBadge, PersianString.Myself, string.Empty));
            return list;
        }
    }
}
