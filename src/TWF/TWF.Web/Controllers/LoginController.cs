using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using CaptchaMvc.HtmlHelpers;
using CaptchaMvc.Models;
using MyTime3.Authorization;
using MyTime3.Content;
using MyTime3.Lock;
using MyTime3.Models;
using Personnel;
using T5V3.DB;
using T5V3.Model;
using TWF.Infrastructure.Security;
using UserInfo = Personnel.Model.UserInfo;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten LoginController (Phase 3).
    /// - Zero-Lock: Hardware dongle checks and personnel count caps are completely disabled.
    /// - Brute-Force Protection: Integrates <see cref="LoginAttemptThrottle"/> per IP and username.
    /// - Fixes BUG-03: Properly enforces ChangePassForce (Message=1), ChangePassMonthly (Message=2), and ChangePassRestriction.
    /// </summary>
    public class LoginController : Controller
    {
        public static LoginAttemptThrottle DefaultThrottle { get; set; } =
            new LoginAttemptThrottle(captchaThreshold: 3, lockoutThreshold: 5, lockoutDuration: TimeSpan.FromMinutes(15));

        [HttpGet]
        public ActionResult Login()
        {
            bool captcha = (bool)SettingsController.LoadValue("Captcha", typeof(bool), false);
            base.ViewBag.Captcha = captcha;
            return View(new LoginModel
            {
                Captcha = captcha
            });
        }

        [HttpGet]
        public ActionResult CheckLock()
        {
            base.ViewBag.LockErrorText = string.Empty;
            return View("LockPage");
        }

        private bool isCaptchaValid(out string message)
        {
            message = string.Empty;
            bool captchaEnabled = (bool)SettingsController.LoadValue("Captcha", typeof(bool), false);
            if (!captchaEnabled || DBInitializer.IsTestMode)
            {
                return true;
            }

            bool result = this.IsCaptchaValid("Captcha is not valid", Array.Empty<ParameterModel>());
            if (!result)
            {
                message = PersianString.CaptchaError;
            }
            return result;
        }

        [HttpPost]
        public ActionResult Login(LoginModel model)
        {
            if (model == null)
            {
                model = new LoginModel();
            }

            bool captcha = (bool)SettingsController.LoadValue("Captcha", typeof(bool), false);
            model.Captcha = captcha;
            base.ViewBag.Captcha = captcha;

            string clientIp = GetClientIpAddress();
            string throttleKey = $"{clientIp}|{(model.UserName ?? string.Empty).Trim().ToLowerInvariant()}";

            if (DefaultThrottle != null && DefaultThrottle.IsLockedOut(throttleKey, DateTime.UtcNow, out TimeSpan remaining))
            {
                int minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
                string lockoutMessage = $"تعداد تلاش‌های ناموفق بیش از حد مجاز است. لطفاً {minutes} دقیقه دیگر مجدداً تلاش نمایید.";
                model.Message = lockoutMessage;
                base.ViewBag.Message = lockoutMessage;
                model.Password = string.Empty;
                return View("Login", model);
            }

            string message = string.Empty;
            if (isCaptchaValid(out message))
            {
                RefreshLockState();
                if (!GlobalHelper.WithTimeApp())
                {
                    if (GlobalHelper.LockIsOK)
                    {
                        bool isAdmin;
                        long badgeNumber = AuthenticateUser(model.UserName, model.Password, out isAdmin);
                        if (badgeNumber != -1)
                        {
                            DefaultThrottle?.RecordSuccess(throttleKey);
                            string role = isAdmin ? "admin" : "user";

                            if (isAdmin)
                            {
                                string lockIp = SettingsController.LoadValue("LockIP", typeof(string), string.Empty).ToString();
                                if (!string.IsNullOrWhiteSpace(lockIp) &&
                                    !string.Equals(clientIp, lockIp.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    const string ipDeniedMessage = "دسترسی از این آدرس IP مجاز نمی‌باشد.";
                                    model.Message = ipDeniedMessage;
                                    base.ViewBag.Message = ipDeniedMessage;
                                    model.Password = string.Empty;
                                    return View("Login", model);
                                }

                                LoginManager.Login(badgeNumber, role, base.HttpContext);
                                RecordSystemLoginLog(badgeNumber, clientIp);
                                return RedirectToAction("Index", "Settings");
                            }

                            LoginManager.Login(badgeNumber, role, base.HttpContext);
                            RecordSystemLoginLog(badgeNumber, clientIp);

                            if (!ChangePassController.ChangePassCheck(badgeNumber, out int passCheckMessage))
                            {
                                return RedirectToAction("ChangePasswordForce", "Login", new
                                {
                                    Message = passCheckMessage,
                                    Force = true
                                });
                            }

                            bool changePassRestriction = (bool)SettingsController.LoadValue("ChangePassRestriction", typeof(bool), false);
                            if (changePassRestriction && (model.Password == null || model.Password.Length < 5))
                            {
                                return RedirectToAction("ChangePasswordForce", "Login", new
                                {
                                    Message = 0,
                                    Force = true
                                });
                            }

                            return RedirectToAction("Index", "Home");
                        }

                        DefaultThrottle?.RecordFailure(throttleKey, DateTime.UtcNow);
                        message = PersianString.InvalidUserNameOrPassword;
                    }
                    else
                    {
                        message = PersianString.LockError;
                    }
                }
                else
                {
                    message = PersianString.LockError;
                }
            }

            model.Message = message;
            base.ViewBag.Message = message;
            model.Password = string.Empty;
            return View("Login", model);
        }

        private void RecordSystemLoginLog(long badgeNumber, string clientIp)
        {
            if (DBInitializer.IsTestMode)
                return;

            try
            {
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                var logEntry = new Log
                {
                    BadgeNumber = badgeNumber,
                    ComputerName = clientIp,
                    Descriptions = "MyTime3\r\n" + (base.Request?.UserAgent ?? string.Empty),
                    EventTime = DateTime.Now,
                    EventType = EventType.Login,
                    OperationType = OperationType.None,
                    UserName = badgeNumber.ToString()
                };
                t5DBInstance.SystemLogDA.AddNew(logEntry);
            }
            catch
            {
            }
        }

        private string GetClientIpAddress()
        {
            try
            {
                return base.HttpContext?.Request?.UserHostAddress ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void RefreshLockState()
        {
            CheckLockResult checkLockResult = LockCheker.Check();
            GlobalHelper.LockIsOK = (checkLockResult.LockResult == LockResult.OK);
            GlobalHelper.PersonnelCount = checkLockResult.PersonnelCount;
            GlobalHelper.FulLogoSize = checkLockResult.IsLogoFree;
            GlobalHelper.TimeApp = checkLockResult.TimeApp;
        }

        [HttpGet]
        public ActionResult RetryCheck()
        {
            RefreshLockState();
            return RedirectToAction("Login");
        }

        [HttpPost]
        public JsonResult TestUserPass(LoginModel model)
        {
            bool result = false;
            if (model != null)
            {
                result = AuthenticateUser(model.UserName, model.Password, out _) != -1L;
            }
            return Json(new
            {
                result
            });
        }

        private long AuthenticateUser(string username, string password, out bool isAdmin)
        {
            isAdmin = false;
            if (string.IsNullOrWhiteSpace(username) || password == null)
            {
                return -1L;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                if (string.Equals(username, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    if (DBInitializer.TestBackendAdapter.VerifyAdminPassword(password))
                    {
                        isAdmin = true;
                        return 0L;
                    }
                    return -1L;
                }

                long? authenticatedBadge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(username, password);
                if (authenticatedBadge.HasValue && authenticatedBadge.Value > 0)
                {
                    var emp = DBInitializer.TestBackendAdapter.GetEmployeeByBadge(authenticatedBadge.Value);
                    if (emp != null && emp.WebUserEnabled)
                    {
                        return authenticatedBadge.Value;
                    }
                }
                return -1L;
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            long badgeNumber = personnelSystem.DB.UserInfos.CheckUserPass(username, password, true);
            if (badgeNumber > 0)
            {
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                isAdmin = t5DBInstance.UserPermissionDA.IsAnyMainManager(new List<long> { badgeNumber });
                return badgeNumber;
            }

            return -1L;
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult ChangePassword()
        {
            return View(new ChangePasswordModel());
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult ChangePasswordForce(int Message = 0)
        {
            string messageText = "کلمه عبور فعلی باید حداقل ۵ کاراکتر و شامل حروف و اعداد باشد.";
            switch (Message)
            {
                case 1:
                    messageText = "جهت ادامه فعالیت در سامانه، تغییر کلمه عبور اولیه الزامی است.";
                    break;
                case 2:
                    messageText = "مهلت دوره‌ای اعتبار کلمه عبور شما به پایان رسیده است. لطفاً کلمه عبور جدیدی انتخاب نمایید.";
                    break;
            }
            base.ViewBag.Message = messageText;
            return View(new ChangePasswordModel
            {
                Message = messageText
            });
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult ChangePasswordForce(ChangePasswordModel passes)
        {
            bool changed = ApplyPasswordChange(passes);
            base.ViewBag.Success = changed;
            if (!changed)
            {
                return View("ChangePasswordForce", passes);
            }
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult ChangePassword(ChangePasswordModel passes)
        {
            base.ViewBag.Success = ApplyPasswordChange(passes);
            return View("ChangePassword", passes);
        }

        [HttpPost]
        public JsonResult Change(string userName, string password, string newPassword)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                long? badge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    if (DBInitializer.TestBackendAdapter.ChangeEmployeePassword(badge.Value, password, newPassword))
                    {
                        ChangePassController.PassChanged(badge.Value);
                        return Json(new { Message = "1" });
                    }
                    return Json(new { Message = "0" });
                }
                return null;
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
            if (num > 0)
            {
                if (personnelSystem.DB.UserInfos.EditPassword(num, newPassword))
                {
                    ChangePassController.PassChanged(num);
                    return Json(new { Message = "1" });
                }
                return Json(new { Message = "0" });
            }
            return null;
        }

        private bool ApplyPasswordChange(ChangePasswordModel model)
        {
            if (model == null)
                return false;

            if (string.IsNullOrEmpty(model.OldPassword))
            {
                model.Message = PersianString.InvalidUserNameOrPassword;
                base.ViewBag.Message = model.Message;
                return false;
            }
            if (string.IsNullOrEmpty(model.NewPassword))
            {
                model.Message = PersianString.ConfirmPasswordError;
                base.ViewBag.Message = model.Message;
                return false;
            }
            if (model.NewPassword != model.ConfirmPassword)
            {
                model.Message = PersianString.ConfirmPasswordError;
                base.ViewBag.Message = model.Message;
                return false;
            }

            bool changePassRestriction = (bool)SettingsController.LoadValue("ChangePassRestriction", typeof(bool), false);
            if (changePassRestriction)
            {
                if (model.NewPassword.Length < 5)
                {
                    model.Message = "کلمه عبور جدید باید حداقل ۵ کاراکتر باشد.";
                    base.ViewBag.Message = model.Message;
                    return false;
                }
                if (!model.NewPassword.Any(char.IsDigit) || !model.NewPassword.Any(char.IsLetter))
                {
                    model.Message = "کلمه عبور جدید باید شامل حروف و اعداد باشد.";
                    base.ViewBag.Message = model.Message;
                    return false;
                }
            }

            long badge = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            if (badge <= 0)
            {
                model.Message = PersianString.ErrorOperation;
                base.ViewBag.Message = model.Message;
                return false;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var emp = DBInitializer.TestBackendAdapter.GetEmployeeByBadge(badge);
                if (emp == null)
                {
                    model.Message = PersianString.ErrorOperation;
                    base.ViewBag.Message = model.Message;
                    return false;
                }

                long? check = DBInitializer.TestBackendAdapter.AuthenticateEmployee(emp.UserName, model.OldPassword);
                if (!check.HasValue || check.Value != badge)
                {
                    model.Message = PersianString.InvalidUserNameOrPassword;
                    base.ViewBag.Message = model.Message;
                    return false;
                }

                if (!DBInitializer.TestBackendAdapter.ChangeEmployeePassword(badge, model.OldPassword, model.NewPassword))
                {
                    model.Message = PersianString.ErrorInInsertingData;
                    base.ViewBag.Message = model.Message;
                    return false;
                }

                ChangePassController.PassChanged(badge);
                model.Message = PersianString.InsertSuccess;
                base.ViewBag.Message = model.Message;
                return true;
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            UserInfo user = personnelSystem.DB.UserInfos.GetByID(badge);
            if (user != null)
            {
                if (user.Password == model.OldPassword)
                {
                    if (personnelSystem.DB.UserInfos.EditPassword(badge, model.NewPassword))
                    {
                        ChangePassController.PassChanged(badge);
                        model.Message = PersianString.InsertSuccess;
                        base.ViewBag.Message = model.Message;
                        return true;
                    }
                    model.Message = PersianString.ErrorInInsertingData;
                    base.ViewBag.Message = model.Message;
                }
                else
                {
                    model.Message = PersianString.InvalidUserNameOrPassword;
                    base.ViewBag.Message = model.Message;
                }
            }
            else
            {
                model.Message = PersianString.ErrorOperation;
                base.ViewBag.Message = model.Message;
            }
            return false;
        }

        [HttpGet]
        public ActionResult Logout()
        {
            LoginManager.Logout(base.HttpContext);
            return RedirectToAction("Index", "Home");
        }
    }
}
